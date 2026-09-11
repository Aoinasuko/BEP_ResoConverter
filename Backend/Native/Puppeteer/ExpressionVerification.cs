using System.Text.Json;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.CommonAvatar;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes;
using nadena.dev.resonity.remote.puppeteer.rpc;
using Renderite.Shared;
using PoseNode = FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Avatar.FingerPose;

namespace nadena.dev.resonity.remote.puppeteer;

internal static class ExpressionVerification
{
    internal static async Task<object?> Verify(World world, Slot container)
    {
        var host = container.GetAllChildren().FirstOrDefault(s => s.Name == "[BEP] Expressions");
        if (host == null) return null;
        var settings = JsonSerializer.Deserialize<ExpressionSettings>(host.FindChild("Configuration").GetComponent<ValueField<string>>().Value.Value)!;
        var hands = settings.handEnabled ? settings.handRules : new List<HandExpressionRule>();
        var menus = settings.menuEnabled ? settings.menuEntries : new List<MenuExpressionEntry>();
        var selection = host.FindChild("Menu Selection").GetComponent<ValueField<int>>().Value;
        var channels = host.Children.Where(s => s.Name.StartsWith("Channel ")).ToArray();
        var fields = channels.Select(s => s.GetComponent<ReferenceField<IField<float>>>().Reference.Target).ToArray();
        var live = channels.Select(s => s.FindChild("Live Base").GetComponent<ValueField<float>>().Value).ToArray();
        var scene = world.RootSlot.AddSlot("BEP expression verification inputs");
        var checks = new List<string>();
        var originalSelection = selection.Value;
        var originalUserRoot = world.LocalUser.Root;
        var wearerReference = host.FindChild("Avatar User")?.GetComponent<ReferenceField<User>>().Reference;
        var redirected = new List<(FieldDrive<float> drive, IField<float> target)>();
        var blinkSources = new List<(ValueDriver<float> driver, IValue<float> source)>();
        var liveBlinkChecks = 0;
        var previousFullUpdate = world.ForceFullUpdateCycle;
        // A headless world with one user normally pauses ordinary component
        // updates. Enable real updates only for this isolated verification.
        world.ForceFullUpdateCycle = true;
        try
        {
            async Task Settle() { for (var i = 0; i < 20; i++) await new NextUpdate(); }
            void CheckValues(IEnumerable<ExpressionValue>? selected, string label)
            {
                var values = selected?.ToDictionary(v => v.target, v => v.value) ?? new Dictionary<int, float>();
                for (var i = 0; i < fields.Length; i++)
                {
                    var expected = values.TryGetValue(i, out var value) ? value : live[i].Value;
                    if (MathF.Abs(fields[i].Value - expected) > .0001f)
                        throw new InvalidOperationException($"Expression {label}: channel {i} expected {expected}, got {fields[i].Value}.");
                }
                checks.Add(label);
            }
            void Press(int value)
            {
                var button = host.GetComponentsInChildren<ButtonValueSet<int>>().SingleOrDefault(b => b.SetValue.Value == value);
                if (button != null) button.Pressed(button.Slot.GetComponent<ContextMenuItemSource>(), default);
                else selection.Value = value;
            }
            if (hands.Count > 0)
            {
                Press(-1); await Settle();
                if (host.FindChild("Avatar User").GetComponent<ReferenceField<User>>().Reference.Target != null
                    || host.FindChild("Left Gesture").GetComponent<ValueField<int>>().Value.Value != -1
                    || host.FindChild("Right Gesture").GetComponent<ValueField<int>>().Value.Value != -1
                    || host.FindChild("Hand Rule Selection").GetComponent<ValueField<int>>().Value.Value != -1)
                    throw new InvalidOperationException("Unworn avatar unexpectedly activates a hand expression.");
                CheckValues(null, "unworn avatar has no hand override, including Any rules");
            }
            // Exercise the actual native blink ValueDriver through the proxy and
            // final mesh field, without replacing that driver's output link.
            foreach (var blink in container.GetComponentsInChildren<ValueDriver<float>>().Where(d => d.Slot.Name == "BEP Selected Blink"))
            {
                var channel = Array.IndexOf(live, blink.DriveTarget.Target);
                if (channel < 0) continue;
                blinkSources.Add((blink, blink.ValueSource.Target));
                var input = scene.AddSlot("Live blink source").AttachComponent<ValueField<float>>().Value;
                blink.ValueSource.Target = input;
                Press(0);
                foreach (var sample in new[] { 0f, .85f, .2f })
                {
                    input.Value = sample; await Settle();
                    if (MathF.Abs(live[channel].Value - sample) > .0001f || MathF.Abs(fields[channel].Value - sample) > .0001f)
                        throw new InvalidOperationException($"Native blink driver does not reach expression proxy: sample={sample}, live={live[channel].Value}, mesh={fields[channel].Value}, enabled={blink.Enabled}/{blink.Slot.IsActive}, link={blink.DriveTarget.IsLinkValid}, target={blink.DriveTarget.Target}, source={blink.ValueSource.Target?.Value}.");
                    liveBlinkChecks++;
                }
                checks.Add("native blink driver reaches original BlendShape through live proxy");
            }
            // Preserve the original native drive graph but supply stable test values
            // at its destinations. This verifies live fallback and sparse overrides.
            for (var i = 0; i < live.Length; i++)
            {
                if (live[i].ActiveLink is FieldDrive<float> drive)
                {
                    redirected.Add((drive, live[i]));
                    drive.ForceLink(scene.AddSlot("Original driver sink " + i).AttachComponent<ValueField<float>>().Value);
                    live[i].Value = .13f + i * .07f;
                }
            }
            Press(0); await Settle(); CheckValues(null, "initial state restores baseline and live values");
            for (var menu = 0; menu < menus.Count; menu++)
            {
                Press(menu + 1); await Settle(); CheckValues(menus[menu].values, "menu " + menu);
            }

            if (hands.Count > 0)
            {
                var userRoot = scene.AddSlot("Native stream test wearer").AttachComponent<UserRoot>();
                world.LocalUser.Root = userRoot;
                var source = userRoot.Slot.AttachComponent<FingerPoseStreamManager>();
                source.Setup(world.LocalUser, 1, 0);
                // Supply raw native streams without requiring a physical headset;
                // the standard component is still used for all source reads.
                source.User.Target = null;
                source.TracksMetacarpals.Value = true;
                userRoot.Slot.AttachComponent<AvatarFingerPoseInfo>().FingerPoseSource.Target = source;
                wearerReference!.Target = world.LocalUser;
                void Pose(Chirality side, int gesture)
                {
                    for (var bone = BodyNode.LeftThumb_Metacarpal.GetSide(side); bone <= BodyNode.LeftPinky_Tip.GetSide(side); bone++)
                    {
                        ExpressionHandPoses.GetPose(bone, gesture, out _, out var rotation);
                        var index = bone.GetFingerNodeIndex(out _) + (side == Chirality.Left ? 0 : 24);
                        source.Stream.Target[index] = rotation;
                    }
                    (side == Chirality.Left ? source.LeftIsTracking : source.RightIsTracking).Target.Value = true;
                }
                var leftOutput = host.FindChild("Left Gesture").GetComponent<ValueField<int>>().Value;
                var rightOutput = host.FindChild("Right Gesture").GetComponent<ValueField<int>>().Value;
                var ruleOutput = host.FindChild("Hand Rule Selection").GetComponent<ValueField<int>>().Value;
                for (var left = 0; left < 8; left++)
                for (var right = 0; right < 8; right++)
                {
                    Pose(Chirality.Left, left);
                    Pose(Chirality.Right, right);
                    Press(-1); await Settle();
                    if (leftOutput.Value != left || rightOutput.Value != right)
                        throw new InvalidOperationException($"Finger pose classifier expected ({left},{right}), got ({leftOutput.Value},{rightOutput.Value}); curls: "
                            + string.Join(", ", host.Children.Where(s => s.Name.Contains("Finger Curl")).Select(s => s.Name + "=" + s.GetComponent<ValueField<float>>().Value.Value)));
                    var index = hands.FindIndex(rule => (rule.left == "Any" || ExpressionGestures.Parse(rule.left) == left)
                        && (rule.right == "Any" || ExpressionGestures.Parse(rule.right) == right));
                    if (ruleOutput.Value != index) throw new InvalidOperationException("Hand rule priority is incorrect.");
                    CheckValues(index < 0 ? null : hands[index].values, $"hand pair {left}/{right}");
                }
                Pose(Chirality.Left, 1); Pose(Chirality.Right, 3);
                if (menus.Count > 0)
                {
                    Press(1); await Settle(); CheckValues(menus[0].values, "menu overrides an active hand expression");
                    Pose(Chirality.Left, 2); Pose(Chirality.Right, 7);
                    await Settle(); CheckValues(menus[0].values, "menu remains fixed when fingers change");
                    Press(0); await Settle(); CheckValues(null, "fixed initial ignores hand expression");
                    Press(-1); await Settle();
                    var index = hands.FindIndex(rule => (rule.left == "Any" || ExpressionGestures.Parse(rule.left) == 2)
                        && (rule.right == "Any" || ExpressionGestures.Parse(rule.right) == 7));
                    CheckValues(index < 0 ? null : hands[index].values, "return to current hand expression");
                }
                foreach (var tracking in new[] { source.LeftIsTracking.Target, source.RightIsTracking.Target })
                {
                    tracking.Value = false;
                    Press(-1); await Settle();
                    if (ruleOutput.Value != -1) throw new InvalidOperationException("Lost stream tracking retains a hand expression.");
                    CheckValues(null, "native stream tracking loss restores live base");
                    tracking.Value = true;
                    await Settle();
                }
                for (var index = 0; index < 48; index++) source.Stream.Target[index] = floatQ.Identity;
                await Settle();
                if (leftOutput.Value != 2 || rightOutput.Value != 2)
                    throw new InvalidOperationException("Tracked identity rotations are incorrectly treated as missing tracking.");
                checks.Add("zero-position and identity-rotation tracked stream stays valid");
                wearerReference.Target = null;
                await Settle();
                if (leftOutput.Value != -1 || rightOutput.Value != -1 || ruleOutput.Value != -1)
                    throw new InvalidOperationException("Removing the wearer/source retains the previous hand expression.");
                CheckValues(null, "removing wearer source releases the last hand expression");
                world.LocalUser.Root = originalUserRoot;
            }

            Press(0);
            for (var i = 0; i < live.Length; i++) live[i].Value += .09f;
            await Settle(); CheckValues(null, "live base changes propagate after initial reset");
            var items = host.GetComponentsInChildren<ContextMenuItemSource>().Select(s => s.Label.Value).ToArray();
            if (menus.Count > 0 && (!items.Contains("初期状態") || !items.Contains("表情")
                || host.GetComponentInChildren<RootContextMenuItem>() == null))
                throw new InvalidOperationException("Expression root menu or initial state is missing.");
            var wearerRegistration = await VerifyWearerRegistration(world, host, fields, checks);
            return new { verified = true, checks = checks.Count, handPosePairs = hands.Count > 0 ? 64 : 0,
                menuItems = items, liveDriverProxies = redirected.Count, liveBlinkChecks, wearerRegistration };
        }
        finally
        {
            await new ToWorld();
            if (wearerReference != null) wearerReference.Target = null;
            world.LocalUser.Root = originalUserRoot;
            foreach (var (drive, target) in redirected) drive.ForceLink(target);
            foreach (var (driver, source) in blinkSources) driver.ValueSource.Target = source;
            selection.Value = originalSelection;
            scene.Destroy();
            world.ForceFullUpdateCycle = previousFullUpdate;
        }
    }

    private static async Task<object> VerifyWearerRegistration(World world, Slot host, IField<float>[] originalFields, List<string> checks)
    {
        var avatar = host.GetComponentInParents<AvatarRoot>()
            ?? throw new InvalidOperationException("Expression logic is outside the avatar root hierarchy.");
        var assigner = host.GetComponent<AvatarUserReferenceAssigner>();
        if (assigner != null)
        {
            var collected = false;
            AvatarObjectSlot.ForeachObjectComponent(avatar.Slot, c => collected |= c == assigner);
            if (!collected) throw new InvalidOperationException("Avatar equip traversal does not include the expression user assigner.");
        }
        var scene = world.RootSlot.AddSlot("BEP isolated wearer registration verification");
        var oldRoot = world.LocalUser.Root;
        try
        {
            var wearer = scene.AddSlot("Test wearer").AttachComponent<UserRoot>();
            world.LocalUser.Root = wearer;
            var avatarManager = wearer.Slot.AttachComponent<AvatarManager>();
            avatarManager.AutoAddNameBadge.Value = false;
            avatarManager.AutoAddIconBadge.Value = false;
            avatarManager.AutoAddLiveIndicator.Value = false;
            var tracked = wearer.Slot.AddSlot("Test wearer finger input").AttachComponent<FingerReferencePoseSource>();
            var trackedBones = new Dictionary<BodyNode, Slot>();
            foreach (var side in new[] { Chirality.Left, Chirality.Right })
            {
                var hand = tracked.Slot.AddSlot(side + " tracked hand");
                tracked.Bones.Add(BodyNode.LeftHand.GetSide(side), hand);
                for (var bone = BodyNode.LeftThumb_Metacarpal.GetSide(side); bone <= BodyNode.LeftPinky_Tip.GetSide(side); bone++)
                {
                    var boneSlot = hand.AddSlot(bone.ToString());
                    trackedBones[bone] = boneSlot;
                    tracked.Bones.Add(bone, boneSlot);
                }
            }
            SetPose(trackedBones, Chirality.Left, 1); SetPose(trackedBones, Chirality.Right, 3);
            wearer.Slot.AttachComponent<AvatarFingerPoseInfo>().FingerPoseSource.Target = tracked;
            var equipment = wearer.Slot.GetComponent<AvatarObjectSlot>();
            var copy = avatar.Slot.Duplicate(scene);
            var copyAvatar = copy.GetComponent<AvatarRoot>();
            var copyHost = copy.GetAllChildren().Single(s => s.Name == "[BEP] Expressions");
            var originalSelection = host.FindChild("Menu Selection").GetComponent<ValueField<int>>().Value.Value;
            var originalValues = originalFields.Select(f => f.Value).ToArray();
            for (var frame = 0; frame < 5; frame++) await new NextUpdate();
            equipment.Equip(copyAvatar);
            for (var frame = 0; frame < 20; frame++) await new NextUpdate();
            var copyOwner = copyHost.FindChild("Avatar User")?.GetComponent<ReferenceField<User>>();
            if (copyOwner != null && copyOwner.Reference.Target != world.LocalUser)
                throw new InvalidOperationException("Actual avatar equip did not assign the expression wearer.");
            if (copyOwner != null && (copyHost.FindChild("Left Gesture").GetComponent<ValueField<int>>().Value.Value != 1
                || copyHost.FindChild("Right Gesture").GetComponent<ValueField<int>>().Value.Value != 3))
                throw new InvalidOperationException("The actual wearer UserFingerPoseSource does not supply the left and right finger poses.");
            var menu = copyHost.GetComponentInChildren<RootContextMenuItem>();
            if (menu != null && (menu.Slot.ActiveUserRoot != wearer || !wearer.GetRegisteredComponents<RootContextMenuItem>().Contains(menu)))
                throw new InvalidOperationException("The expression menu did not register with the actual wearer root.");
            checks.Add("actual avatar equip assigns wearer and registers its root menu");
            checks.Add("actual wearer UserFingerPoseSource supplies both finger poses");
            var button = copyHost.GetComponentsInChildren<ButtonValueSet<int>>().FirstOrDefault(b => b.SetValue.Value == 1);
            button?.Pressed(button.Slot.GetComponent<ContextMenuItemSource>(), default);
            for (var frame = 0; frame < 20; frame++) await new NextUpdate();
            if (host.FindChild("Menu Selection").GetComponent<ValueField<int>>().Value.Value != originalSelection
                || originalFields.Where((f, i) => MathF.Abs(f.Value - originalValues[i]) > .0001f).Any())
                throw new InvalidOperationException("Selecting an expression on a second avatar changes the first avatar.");
            checks.Add("second avatar selection leaves the first avatar independent");
            var controller = await ControllerExpressionVerification.Verify(world, copyHost, wearer, checks);
            equipment.Dequip(null);
            copy.SetParent(scene);
            for (var frame = 0; frame < 20; frame++) await new NextUpdate();
            if (copyOwner?.Reference.Target != null || (menu != null && wearer.GetRegisteredComponents<RootContextMenuItem>().Contains(menu)))
                throw new InvalidOperationException("Dequipping the avatar retains its wearer or root menu registration.");
            checks.Add("actual avatar dequip clears wearer and removes menu registration");
            return new { verified = true, avatarRootSlot = avatar.Slot.Name, expressionParent = host.Parent.Name,
                equipTraversalIncludesAssigner = assigner != null, wearerFingerSourceVerified = copyOwner != null,
                menuRegisteredWithWearer = menu != null, independentCopy = true, controller };
        }
        finally
        {
            world.LocalUser.Root = oldRoot;
            scene.Destroy();
        }
    }

    private static void SetPose(Dictionary<BodyNode, Slot> bones, Chirality side, int gesture)
    {
        foreach (var (node, slot) in bones.Where(pair => pair.Key.GetHandChirality() == side))
        {
            var finger = node.GetFingerType();
            var extended = gesture switch
            {
                0 => true,
                2 => true,
                3 or 6 => finger == FingerType.Index || finger == FingerType.Thumb,
                4 => finger == FingerType.Index || finger == FingerType.Middle,
                5 => finger == FingerType.Index || finger == FingerType.Pinky,
                7 => finger == FingerType.Thumb,
                _ => false,
            };
            var preset = gesture == 3 ? FingerPosePresets.Point : extended ? FingerPosePresets.Idle : FingerPosePresets.Fist;
            preset.GetFingerData(node, out var position, out var rotation);
            if (gesture == 2 || (gesture is 6 or 7 && finger == FingerType.Thumb))
            {
                var proximal = finger.ComposeFinger(FingerSegmentType.Proximal, side);
                FingerPosePresets.Idle.GetFingerData(proximal, out _, out rotation);
            }
            slot.LocalPosition = position;
            slot.LocalRotation = rotation;
        }
    }
}
