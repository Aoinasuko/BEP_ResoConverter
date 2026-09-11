using Elements.Core;
using FrooxEngine;
using FrooxEngine.CommonAvatar;
using FrooxEngine.ProtoFlux;
using Renderite.Shared;

namespace nadena.dev.resonity.remote.puppeteer.rpc;

internal static class ExpressionHandPoses
{
    internal static void Build(Slot avatar, Slot host, ExpressionGraph graph, INodeObjectOutput<User> wearer,
        Dictionary<Chirality, ControllerExpressionState> controllers)
    {
        // Capture the actual avatar posers before adding any input-only helpers.
        var posers = avatar.GetComponentsInChildren<HandPoser>()
            .Where(p => !p.Slot.IsChildOf(host) && p.Slot != host).ToArray();
        foreach (var poser in posers)
        {
            var side = poser.Side.Value;
            if (!controllers.TryGetValue(side, out var controller)) continue;
            var slot = host.AddSlot(side + " Controller Hand Poses");
            slot.AttachComponent<ReferenceField<HandPoser>>().Reference.Target = poser;
            var original = slot.AddSlot("Original Finger Source").AttachComponent<ReferenceField<IFingerPoseSourceComponent>>();
            original.Reference.Target = poser.PoseSource.Target;
            var assigners = avatar.GetComponentsInChildren<AvatarHandDataAssigner>()
                .Where(a => a.TargetReference.Target == poser.PoseSource).ToArray();
            if (poser.PoseSource.IsLinked)
                throw new InvalidOperationException("Cannot preserve a pre-existing driven HandPoser source for " + side + ".");
            foreach (var assigner in assigners) assigner.TargetReference.Target = original.Reference;
            // Hand touch and haptic references on the assigner remain intact.
            // Equip/dequip now updates the original source without replacing our
            // final selection. Never modify the wearer's global pose source.
            var poses = slot.AttachComponent<FingerPoseMultiplexer>();
            poses.InterpolationSpeed.Value = 20;
            for (var gesture = 0; gesture < 8; gesture++)
                poses.Sources.Add(CreatePose(slot, side, gesture));
            graph.Drive(poses.Index, controller.Gesture);
            var useController = graph.All(graph.NotNull(wearer), controller.Active,
                graph.Not(graph.Less(controller.Gesture, 0)), graph.Not(controller.Holding));
            var useField = slot.AttachComponent<ValueField<bool>>();
            graph.Drive(useField.Value, useController);
            graph.DriveReference(poser.PoseSource, graph.ChooseObject(graph.Read(useField.Value),
                graph.Reference<IFingerPoseSourceComponent>(poses), graph.ReadObject<IFingerPoseSourceComponent>(original.Reference)));
        }
    }

    internal static FingerReferencePoseSource CreatePose(Slot parent, Chirality side, int gesture)
    {
        var source = parent.AddSlot(side + " " + ExpressionGestures.Names[gesture]).AttachComponent<FingerReferencePoseSource>();
        var hand = source.Slot.AddSlot("Reference Hand");
        source.Bones.Add(BodyNode.LeftHand.GetSide(side), hand);
        for (var node = BodyNode.LeftThumb_Metacarpal.GetSide(side); node <= BodyNode.LeftPinky_Tip.GetSide(side); node++)
        {
            var bone = hand.AddSlot(node.ToString());
            GetPose(node, gesture, out var position, out var rotation);
            bone.LocalPosition = position;
            bone.LocalRotation = rotation;
            source.Bones.Add(node, bone);
        }
        return source;
    }

    internal static void GetPose(BodyNode node, int gesture, out float3 position, out floatQ rotation)
    {
        var finger = node.GetFingerType();
        var extended = gesture switch
        {
            0 or 2 => true,
            3 or 6 => finger is FingerType.Index or FingerType.Thumb,
            4 => finger is FingerType.Index or FingerType.Middle,
            5 => finger is FingerType.Index or FingerType.Pinky,
            7 => finger == FingerType.Thumb,
            _ => false,
        };
        var preset = gesture == 3 ? FingerPosePresets.Point : extended ? FingerPosePresets.Idle : FingerPosePresets.Fist;
        preset.GetFingerData(node, out position, out rotation);
        if (gesture == 2 || (gesture is 6 or 7 && finger == FingerType.Thumb))
        {
            var proximal = finger.ComposeFinger(FingerSegmentType.Proximal, node.GetHandChirality());
            FingerPosePresets.Idle.GetFingerData(proximal, out _, out rotation);
        }
    }
}
