using Elements.Core;
using FrooxEngine;
using FrooxEngine.CommonAvatar;
using nadena.dev.resonity.remote.puppeteer.rpc;
using Renderite.Shared;

namespace nadena.dev.resonity.remote.puppeteer;

internal static class ControllerExpressionVerification
{
    internal static async Task<object?> Verify(World world, Slot host, UserRoot wearer, List<string> checks)
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<ExpressionSettings>(
            host.FindChild("Configuration").GetComponent<ValueField<string>>().Value.Value)!;
        if (!settings.handEnabled || !settings.controllerGestures || settings.handRules.Count == 0) return null;
        var scene = world.RootSlot.AddSlot("BEP raw controller verification");
        var restore = new List<Action>();
        var inputs = new Dictionary<Chirality, Inputs>();
        var poseChecks = 0;
        var rawCases = 0;
        try
        {
            foreach (var side in new[] { Chirality.Left, Chirality.Right })
                inputs[side] = new Inputs(world.LocalUser, side, restore);
            var menu = host.FindChild("Menu Selection").GetComponent<ValueField<int>>().Value;
            var oldSelection = menu.Value;
            restore.Add(() => menu.Value = oldSelection);
            var groups = host.Children.Where(s => s.Name.EndsWith("Controller Hand Poses")).ToArray();
            if (groups.Length == 0) throw new InvalidOperationException("Controller expressions contain no actual avatar HandPoser outputs.");
            async Task Settle(int count = 30) { for (var i = 0; i < count; i++) await new NextUpdate(); }
            int Gesture(Chirality side) => host.FindChild(side + " Gesture").GetComponent<ValueField<int>>().Value.Value;
            void CheckFace(int left, int right, string label)
            {
                var rule = settings.handRules.FindIndex(r => (r.left == "Any" || ExpressionGestures.Parse(r.left) == left)
                    && (r.right == "Any" || ExpressionGestures.Parse(r.right) == right));
                var state = host.FindChild("Hand Rule Selection").GetComponent<ValueField<int>>().Value.Value;
                if (state != rule) throw new InvalidOperationException("Raw controller inputs do not select the matching face rule: " + label);
                var values = rule >= 0 ? settings.handRules[rule].values : new List<ExpressionValue>();
                var channels = host.Children.Where(s => s.Name.StartsWith("Channel ")).ToArray();
                for (var i = 0; i < channels.Length; i++)
                {
                    var field = channels[i].GetComponent<ReferenceField<IField<float>>>().Reference.Target;
                    var fallback = channels[i].FindChild("Live Base").GetComponent<ValueField<float>>().Value.Value;
                    var expected = values.FirstOrDefault(v => v.target == i)?.value ?? fallback;
                    if (channels[i].FindChild("Automatic Blink Channel") != null
                        && ExpressionBlink.IsNeutralBlink(settings.targets[i].baseline, expected)) expected = fallback;
                    if (MathF.Abs(field.Value - expected) > .001f)
                        throw new InvalidOperationException($"Controller face {label} channel {i}: expected {expected}, got {field.Value}.");
                }
            }
            void CheckActualPose(Chirality side, int expected)
            {
                foreach (var group in groups)
                {
                    var poser = group.GetComponent<ReferenceField<HandPoser>>().Reference.Target;
                    if (poser.Side.Value != side) continue;
                    if (poser.PoseSource.Target != group.GetComponent<FingerPoseMultiplexer>())
                        throw new InvalidOperationException("Controller input is not driving the actual avatar HandPoser.");
                    var source = poser.PoseSource.Target;
                    foreach (var finger in new[] { poser.Thumb, poser.Index, poser.Middle, poser.Ring, poser.Pinky })
                    foreach (var segment in new[] { finger.Proximal, finger.Distal })
                    {
                        var bone = segment.Root.Target;
                        if (bone == null) continue;
                        var type = finger == poser.Thumb ? FingerType.Thumb : finger == poser.Index ? FingerType.Index
                            : finger == poser.Middle ? FingerType.Middle : finger == poser.Ring ? FingerType.Ring : FingerType.Pinky;
                        var part = segment == finger.Proximal ? FingerSegmentType.Proximal : FingerSegmentType.Distal;
                        var node = type.ComposeFinger(part, side);
                        ExpressionHandPoses.GetPose(node, expected, out _, out var referenceRotation);
                        source.GetFingerData(node, out _, out var actualSourceRotation);
                        if (MathX.Angle(referenceRotation, actualSourceRotation) > 3f)
                            throw new InvalidOperationException($"Controller {side}/{expected} source pose differs at {node}.");
                        var hand = poser.HandRoot.Target ?? poser.Slot;
                        var rotation = floatQ.LookRotation(poser.HandForward.Value, poser.HandUp.Value) * actualSourceRotation;
                        var expectedBone = bone.Parent.SpaceRotationToLocal(rotation, hand) * segment.CoordinateCompensation.Value;
                        if (MathX.Angle(expectedBone, bone.LocalRotation) > 3f)
                            throw new InvalidOperationException($"Controller {side}/{expected} does not move rendered finger bone {node}.");
                        poseChecks++;
                    }
                }
            }
            menu.Value = -1;
            for (var gesture = 0; gesture < 8; gesture++)
            {
                inputs[Chirality.Left].Pose(gesture);
                inputs[Chirality.Right].Pose(7 - gesture);
                await Settle(45);
                if (Gesture(Chirality.Left) != gesture || Gesture(Chirality.Right) != 7 - gesture)
                    throw new InvalidOperationException("Raw native Touch controller pose selection is incorrect at " + gesture);
                CheckActualPose(Chirality.Left, gesture); CheckActualPose(Chirality.Right, 7 - gesture);
                CheckFace(gesture, 7 - gesture, "8-pose " + gesture);
                rawCases += 2;
            }
            checks.Add("raw native Touch ValueStreams select all 8 gestures, actual finger bones and face rules");
            var leftInput = inputs[Chirality.Left];
            leftInput.Pose(1); leftInput.Trigger.Value = 0; leftInput.TriggerClick.Value = false;
            await Settle();
            if (Gesture(Chirality.Left) != 0) throw new InvalidOperationException("Resting on the trigger is mistaken for pulling a fist.");
            rawCases++;
            leftInput.TriggerTouch.Value = false; leftInput.Trigger.Value = 1;
            await Settle();
            if (Gesture(Chirality.Left) != 1) throw new InvalidOperationException("Trigger pull is lost when capacitive trigger touch is false.");
            rawCases++;
            foreach (var thumb in leftInput.ThumbInputs)
            {
                leftInput.Pose(2); thumb.Value = true; await Settle();
                if (Gesture(Chirality.Left) != 4) throw new InvalidOperationException("A supported thumb touch/press is ignored.");
                rawCases++;
            }
            leftInput.Pose(2); leftInput.ThumbRest.Value = true; await Settle();
            if (Gesture(Chirality.Left) != 2) throw new InvalidOperationException("Thumb rest unexpectedly changes the SteamVR default gesture.");
            rawCases++;
            checks.Add("trigger touch/pull are separate and SteamVR thumb inputs match the documented mapping");

            // The expression menu fixes the face, while the fingers continue to
            // follow the controller independently.
            if (settings.menuEnabled && settings.menuEntries.Count > 0)
            {
                menu.Value = 1;
                leftInput.Pose(5); await Settle(45); CheckActualPose(Chirality.Left, 5);
                if (menu.Value != 1) throw new InvalidOperationException("Controller fingers changed the fixed menu selection.");
                checks.Add("fixed face menu does not freeze controller hand poses");
            }
            var grabber = wearer.Slot.AddSlot("Controller test grabber").AttachComponent<Grabber>();
            grabber.CorrespondingBodyNode.Value = BodyNode.LeftHand;
            var item = scene.AddSlot("Held test item");
            var grabbable = item.AttachComponent<Grabbable>();
            var collider = item.AttachComponent<BoxCollider>(); collider.Size.Value = float3.One;
            await Settle();
            if (!grabber.Grab(new List<ICollider> { collider }, g => g == grabbable, includeDynamic: false, singleItem: true))
                throw new InvalidOperationException("Could not perform the native controller holding test.");
            await Settle();
            foreach (var group in groups)
            {
                var poser = group.GetComponent<ReferenceField<HandPoser>>().Reference.Target;
                if (poser.Side.Value == Chirality.Left && poser.PoseSource.Target != group.FindChild("Original Finger Source")
                    .GetComponent<ReferenceField<IFingerPoseSourceComponent>>().Reference.Target)
                    throw new InvalidOperationException("Holding an object does not restore the original finger pose source.");
            }
            grabber.Release(grabbable, true); await Settle(45); CheckActualPose(Chirality.Left, 5);
            checks.Add("native grab restores the original hand pose and release resumes controller fingers");
            grabber.Slot.Destroy();
            leftInput.Active.Value = false; await Settle();
            if (Gesture(Chirality.Left) != 1) throw new InvalidOperationException("Inactive/unsupported controller fails to restore original tracked pose classification.");
            checks.Add("inactive or unsupported controller falls back to the wearer's tracked fingers");
            var owner = host.FindChild("Avatar User").GetComponent<ReferenceField<User>>().Reference;
            leftInput.Active.Value = true; owner.Target = null; await Settle();
            if (Gesture(Chirality.Left) != -1 || Gesture(Chirality.Right) != -1)
                throw new InvalidOperationException("An unworn avatar reads somebody else's controller inputs.");
            owner.Target = world.LocalUser;
            checks.Add("missing wearer disables controller gestures even when raw inputs remain active");
            return new { verified = true, rawTouchCases = rawCases, actualFingerBoneChecks = poseChecks,
                source = "native TouchControllerProxy ValueStreams", steamVrHardwareTested = false,
                viveMode = "existing tracked pose fallback" };
        }
        finally
        {
            foreach (var action in restore.AsEnumerable().Reverse()) action();
            scene.Destroy();
        }
    }

    private sealed class Inputs
    {
        internal readonly Sync<bool> Active;
        internal readonly ValueStream<float> Trigger, Grip;
        internal readonly ValueStream<bool> TriggerTouch, TriggerClick, ThumbRest;
        internal readonly ValueStream<bool>[] ThumbInputs;
        private readonly List<ValueStream<bool>> booleans = new();
        internal Inputs(User user, Chirality side, List<Action> restore)
        {
            var proxy = user.GetComponent<TouchControllerProxy>(p => p.Side.Value == side) ?? user.AttachComponent<TouchControllerProxy>();
            proxy.Side.Value = side;
            Active = proxy.IsControllerActive;
            var oldActive = Active.Value; restore.Add(() => Active.Value = oldActive);
            Active.Value = true;
            ValueStream<T> Input<T>(SyncRef<ValueStream<T>> reference, string name) where T : unmanaged
            {
                var old = reference.Target; restore.Add(() => reference.Target = old);
                var stream = user.GetStreamOrAdd("BEP.ControllerVerification." + side + "." + name, (ValueStream<T> _) => { });
                reference.Target = stream;
                return stream;
            }
            ValueStream<bool> Bool(SyncRef<ValueStream<bool>> reference, string name)
            {
                var value = Input(reference, name); booleans.Add(value); return value;
            }
            Trigger = Input(proxy.Trigger, "Trigger"); Grip = Input(proxy.Grip, "Grip");
            TriggerTouch = Bool(proxy.TriggerTouch, "TriggerTouch"); TriggerClick = Bool(proxy.TriggerClick, "TriggerClick");
            Bool(proxy.GripClick, "GripClick");
            ThumbRest = Bool(proxy.ThumbRestTouch, "ThumbRestTouch");
            ThumbInputs = new[] { Bool(proxy.JoystickTouch, "JoystickTouch"), Bool(proxy.ButtonXA_Touch, "XA_Touch"),
                Bool(proxy.ButtonYB_Touch, "YB_Touch"), Bool(proxy.ButtonXA, "XA"), Bool(proxy.ButtonYB, "YB"), Bool(proxy.JoystickClick, "JoystickClick") };
        }

        internal void Pose(int gesture)
        {
            foreach (var value in booleans) value.Value = false;
            Trigger.Value = 0; Grip.Value = 0;
            (bool grip, bool touch, bool pull, bool thumb) raw = gesture switch
            {
                0 => (false, true, false, true),
                1 => (true, true, true, true),
                2 => (false, false, false, false),
                3 => (true, false, false, true),
                4 => (false, false, false, true),
                5 => (false, true, true, true),
                6 => (true, false, false, false),
                7 => (true, true, false, false),
                _ => throw new ArgumentOutOfRangeException(nameof(gesture))
            };
            Grip.Value = raw.grip ? 1 : 0; Trigger.Value = raw.pull ? 1 : 0;
            TriggerTouch.Value = raw.touch; ThumbInputs[0].Value = raw.thumb;
        }
    }
}
