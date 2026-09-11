using System.Reflection;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.CommonAvatar;
using FrooxEngine.ProtoFlux;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Actions;
using nadena.dev.resonity.remote.puppeteer.rpc;
using Renderite.Shared;

namespace nadena.dev.resonity.remote.puppeteer;

internal static class DesktopExpressionVerification
{
    internal static async Task<object?> Verify(World world, Slot host, UserRoot wearer, List<string> checks)
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<ExpressionSettings>(
            host.FindChild("Configuration").GetComponent<ValueField<string>>().Value.Value)!;
        if (!settings.handEnabled || settings.handRules.Count == 0) return null;
        if (host.FindChild("Left Desktop Key Gesture") == null)
            throw new InvalidOperationException("Desktop gesture inputs are missing from the exported avatar.");
        using var inputMode = new ExpressionInputVerificationScope(world);
        var scene = world.RootSlot.AddSlot("BEP desktop gesture verification");
        var menu = host.FindChild("Menu Selection").GetComponent<ValueField<int>>().Value;
        var originalMenu = menu.Value;
        var owner = host.FindChild("Avatar User").GetComponent<ReferenceField<User>>().Reference;
        var oldOwner = owner.Target;
        User? remoteWearer = null;
        var keys = ExpressionDesktop.Keys.Concat(new[] { Key.LeftShift, Key.RightShift, Key.Space, Key.W }).ToArray();
        var originalKeys = keys.ToDictionary(k => k, k => world.InputInterface.GetKeyState(k).Held);
        var groups = host.Children.Where(s => s.Name.EndsWith("Controller Hand Poses")).ToArray();
        var rawCases = 0;
        var actualFingerChecks = 0;
        var tracked = wearer.Slot.GetComponent<AvatarFingerPoseInfo>().FingerPoseSource.Target as FingerReferencePoseSource
            ?? throw new InvalidOperationException("Desktop movement fixture has no native reference pose source.");
        var savedTracking = new List<(Slot bone, float3 position, floatQ rotation)>();
        foreach (var side in new[] { Chirality.Left, Chirality.Right })
        for (var node = BodyNode.LeftThumb_Metacarpal.GetSide(side); node <= BodyNode.LeftPinky_Tip.GetSide(side); node++)
            if (tracked.Bones.TryGetTarget(node, out var bone)) savedTracking.Add((bone, bone.LocalPosition, bone.LocalRotation));
        void KeyState(Key key, bool pressed) => world.InputInterface.GetKeyState(key).UpdateState(pressed, 1f / 60);
        void Keys(params Key[] held) { foreach (var key in keys) KeyState(key, held.Contains(key)); }
        async Task Settle(int frames = 25) { for (var i = 0; i < frames; i++) await new NextUpdate(); }
        int Gesture(Chirality side) => host.FindChild(side + " Gesture").GetComponent<ValueField<int>>().Value.Value;
        int Rule() => host.FindChild("Hand Rule Selection").GetComponent<ValueField<int>>().Value.Value;
        void Face(int left, int right, bool active, string label)
        {
            var expectedRule = active ? settings.handRules.FindIndex(r =>
                (r.left == "Any" || ExpressionGestures.Parse(r.left) == left)
                && (r.right == "Any" || ExpressionGestures.Parse(r.right) == right)) : -1;
            if (Rule() != expectedRule) throw new InvalidOperationException($"Desktop {label}: rule {Rule()}, expected {expectedRule}.");
            var values = menu.Value > 0 ? settings.menuEntries[menu.Value - 1].values
                : menu.Value == 0 || expectedRule < 0 ? new List<ExpressionValue>() : settings.handRules[expectedRule].values;
            var channels = host.Children.Where(s => s.Name.StartsWith("Channel ")).ToArray();
            for (var i = 0; i < channels.Length; i++)
            {
                var baseline = channels[i].FindChild("Live Base").GetComponent<ValueField<float>>().Value.Value;
                var expected = values.FirstOrDefault(v => v.target == i)?.value ?? baseline;
                if (channels[i].FindChild("Automatic Blink Channel") != null
                    && ExpressionBlink.IsNeutralBlink(settings.targets[i].baseline, expected)) expected = baseline;
                var actual = channels[i].GetComponent<ReferenceField<IField<float>>>().Reference.Target.Value;
                if (MathF.Abs(actual - expected) > .001f)
                    throw new InvalidOperationException($"Desktop {label}: face channel {i} is {actual}, expected {expected}.");
            }
            rawCases++;
        }
        void Pose(Chirality side, int? expected)
        {
            foreach (var group in groups)
            {
                var poser = group.GetComponent<ReferenceField<HandPoser>>().Reference.Target;
                if (poser.Side.Value != side) continue;
                var source = expected.HasValue ? group.GetComponent<FingerPoseMultiplexer>()
                    : group.FindChild("Original Finger Source").GetComponent<ReferenceField<IFingerPoseSourceComponent>>().Reference.Target;
                if (poser.PoseSource.Target != source)
                    throw new InvalidOperationException($"Desktop {side} incorrectly replaces/preserves its original hand source.");
                if (!expected.HasValue) continue;
                for (var node = BodyNode.LeftThumb_Metacarpal.GetSide(side); node <= BodyNode.LeftPinky_Tip.GetSide(side); node++)
                {
                    ExpressionHandPoses.GetPose(node, expected.Value, out _, out var expectedRotation);
                    source.GetFingerData(node, out _, out var actualRotation);
                    if (MathX.Angle(expectedRotation, actualRotation) > 3f)
                        throw new InvalidOperationException($"Desktop {side} pose differs at {node}.");
                    actualFingerChecks++;
                }
            }
        }
        try
        {
            // The previous controller test reassigns the wearer immediately
            // before returning. Let native reference drives publish that change.
            await Settle();
            if (groups.Length == 0) throw new InvalidOperationException("Desktop gesture HandPoser output is missing.");
            // Verify the synchronization boundary in the serialized native graph.
            var updating = host.GetComponentsInChildren<Update>().Where(n => n.Slot.Name.EndsWith("Wearer-only Keyboard Update")).ToArray();
            if (updating.Length != 2 || updating.Any(n => n.SkipIfNull.Target?.Value != true
                || n.UpdatingUser.Target?.Value != world.LocalUser || n.OnUpdate.Target == null))
                throw new InvalidOperationException("Desktop input is not published exclusively by the equipped wearer.");
            foreach (var side in new[] { Chirality.Left, Chirality.Right })
                if (host.FindChild(side + " Desktop Key Gesture").GetComponent<ValueField<int>>().Value.IsDriven)
                    throw new InvalidOperationException("Desktop state must synchronize writes instead of per-observer keyboard field drives.");
            inputMode.SetMode(false); menu.Value = -1; Keys(); await Settle();
            Face(0, 0, false, "idle tracking cannot select Any/Idle rules"); Pose(Chirality.Left, null); Pose(Chirality.Right, null);
            Keys(Key.LeftShift, Key.Space, Key.W); await Settle();
            Face(0, 0, false, "jump/dash and Shift alone"); Pose(Chirality.Left, null); Pose(Chirality.Right, null);
            foreach (var pose in new[] { 1, 2, 4, 7 })
            {
                foreach (var side in new[] { Chirality.Left, Chirality.Right })
                for (var node = BodyNode.LeftThumb_Metacarpal.GetSide(side); node <= BodyNode.LeftPinky_Tip.GetSide(side); node++)
                {
                    if (!tracked.Bones.TryGetTarget(node, out var bone)) continue;
                    ExpressionHandPoses.GetPose(node, side == Chirality.Left ? pose : 7 - pose, out var position, out var rotation);
                    bone.LocalPosition = position; bone.LocalRotation = rotation;
                }
                await Settle();
                if (host.FindChild("Left Tracked Gesture").GetComponent<ValueField<int>>().Value.Value != pose
                    || host.FindChild("Right Tracked Gesture").GetComponent<ValueField<int>>().Value.Value != 7 - pose)
                    throw new InvalidOperationException("Desktop regression did not actually change the native finger source.");
                Face(0, 0, false, "changing simulated movement finger source " + pose);
                Pose(Chirality.Left, null); Pose(Chirality.Right, null);
            }
            for (var i = 0; i < 8; i++)
            {
                Keys(ExpressionDesktop.Keys[i]); await Settle(); Face(0, 0, false, "unmodified number " + i);
                foreach (var side in new[] { Chirality.Left, Chirality.Right })
                {
                    Keys(side == Chirality.Left ? Key.LeftShift : Key.RightShift, ExpressionDesktop.Keys[i]);
                    await Settle(45);
                    if (Gesture(side) != i || Gesture(side == Chirality.Left ? Chirality.Right : Chirality.Left) != 0)
                        throw new InvalidOperationException($"Desktop {side} number {i + 1} does not select its own hand only.");
                    Face(side == Chirality.Left ? i : 0, side == Chirality.Right ? i : 0, true, side + " key " + i);
                    Pose(side, i); Pose(side == Chirality.Left ? Chirality.Right : Chirality.Left, null);
                    Keys(side == Chirality.Left ? Key.LeftShift : Key.RightShift); await Settle();
                    Face(0, 0, false, "number release " + i); Pose(side, null);
                }
            }
            Keys(Key.LeftShift, Key.RightShift, ExpressionDesktop.Keys[6], ExpressionDesktop.Keys[2]); await Settle(45);
            Face(2, 2, true, "both hands and deterministic lowest number"); Pose(Chirality.Left, 2); Pose(Chirality.Right, 2);
            Keys(ExpressionDesktop.Keys[2]); await Settle(); Face(0, 0, false, "Shift release");
            Keys(Key.LeftShift, ExpressionDesktop.Keys[1]); await Settle(45);
            var editor = scene.AddSlot("Text input focus").AttachComponent<TextEditor>();
            world.LocalUser.Focus(editor); await Settle(); Face(0, 0, false, "text focus suppresses held chord"); Pose(Chirality.Left, null);
            world.LocalUser.ClearFocus(); await Settle(45); Face(1, 0, true, "leaving text focus");
            world.Focus = World.WorldFocus.Background; await Settle(); Face(0, 0, false, "unfocused world");
            world.Focus = World.WorldFocus.Focused; await Settle(45);
            if (settings.menuEnabled && settings.menuEntries.Count > 0)
            {
                menu.Value = 1; Keys(Key.RightShift, ExpressionDesktop.Keys[7]); await Settle(45);
                Face(0, 7, true, "fixed menu has priority over keyboard face"); Pose(Chirality.Right, 7);
                menu.Value = -1;
            }
            var grabber = wearer.Slot.AddSlot("Desktop test grabber").AttachComponent<Grabber>();
            grabber.CorrespondingBodyNode.Value = BodyNode.LeftHand;
            var item = scene.AddSlot("Desktop held test item"); var grabbable = item.AttachComponent<Grabbable>();
            var collider = item.AttachComponent<BoxCollider>(); collider.Size.Value = float3.One;
            Keys(Key.LeftShift, ExpressionDesktop.Keys[1]); await Settle(45);
            if (!grabber.Grab(new List<ICollider> { collider }, g => g == grabbable, includeDynamic: false, singleItem: true))
                throw new InvalidOperationException("Could not perform the native desktop holding test.");
            await Settle(); Pose(Chirality.Left, null); Face(1, 0, true, "holding preserves hand, face remains available");
            grabber.Release(grabbable, true); await Settle(45); Pose(Chirality.Left, 1);
            grabber.Slot.Destroy();
            foreach (var device in new[] { HeadOutputDevice.Autodetect, HeadOutputDevice.UNKNOWN, HeadOutputDevice.Headless })
            {
                inputMode.SetMode(false, device); await Settle(); Face(-1, -1, false, "unknown mode " + device); Pose(Chirality.Left, null);
            }
            inputMode.SetMode(true); await Settle();
            if (host.FindChild("Left Desktop Key Gesture").GetComponent<ValueField<int>>().Value.Value != -1)
                throw new InvalidOperationException("Switching to VR retains desktop keyboard state.");
            inputMode.SetMode(false); await Settle(45); Face(1, 0, true, "VR to desktop re-evaluates held key");
            remoteWearer = (User)typeof(World).GetMethod("CreateGuestUser", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(world, null)!;
            var initializing = typeof(User).GetField("InitializingEnabled", BindingFlags.Instance | BindingFlags.NonPublic)!;
            initializing.SetValue(remoteWearer, true);
            try
            {
                remoteWearer.UserName = "BEP desktop observer fixture";
                remoteWearer.MachineID = "BEP-Desktop-Verification";
                remoteWearer.HeadDevice = HeadOutputDevice.Screen;
            }
            finally { initializing.SetValue(remoteWearer, false); }
            owner.Target = remoteWearer;
            var leftPublished = host.FindChild("Left Desktop Key Gesture").GetComponent<ValueField<int>>().Value;
            var rightPublished = host.FindChild("Right Desktop Key Gesture").GetComponent<ValueField<int>>().Value;
            leftPublished.Value = rightPublished.Value = -1;
            Keys(Key.LeftShift, ExpressionDesktop.Keys[1]); await Settle();
            Face(0, 0, false, "observer keyboard cannot publish to remote wearer");
            leftPublished.Value = 4; rightPublished.Value = 7; Keys(Key.RightShift, ExpressionDesktop.Keys[2]); await Settle(45);
            Face(4, 7, true, "remote synchronized input survives unrelated observer keys");
            Pose(Chirality.Left, 4); Pose(Chirality.Right, 7);
            owner.Target = world.LocalUser; Keys(Key.LeftShift, ExpressionDesktop.Keys[1]); await Settle();
            Face(1, 0, true, "new local wearer replaces old synchronized state");
            owner.Target = null; await Settle(); Face(-1, -1, false, "dequipped avatar ignores keyboard"); Pose(Chirality.Left, null);
            checks.Add("desktop native keys: both sides 8 poses, release, idle movement, text/world focus, menu, grab, unknown mode and VR switching");
            checks.Add("desktop input uses wearer-only Update and synchronized Write, with SkipIfNull and no local observer drive");
            return new { verified = true, nativeKeyboardCases = rawCases, handPoseJointChecks = actualFingerChecks,
                controllerGestures = settings.controllerGestures, keys = "LeftShift/RightShift + Alpha1..Alpha8",
                synchronization = "wearer-only native Update + synchronized field Write", multiClientHardwareTested = false };
        }
        finally
        {
            world.LocalUser.ClearFocus(); owner.Target = oldOwner; menu.Value = originalMenu;
            foreach (var (key, held) in originalKeys) KeyState(key, held);
            foreach (var (bone, position, rotation) in savedTracking) { bone.LocalPosition = position; bone.LocalRotation = rotation; }
            if (remoteWearer != null)
                typeof(World).GetMethod("RemoveUser", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(world, new object[] { remoteWearer });
            scene.Destroy();
        }
    }
}
