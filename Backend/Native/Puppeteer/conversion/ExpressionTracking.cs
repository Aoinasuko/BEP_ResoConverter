using Elements.Core;
using FrooxEngine;
using FrooxEngine.ProtoFlux;
using Renderite.Shared;

namespace nadena.dev.resonity.remote.puppeteer.rpc;

internal static class ExpressionTracking
{
    internal static INodeValueOutput<bool> Build(ExpressionGraph graph, Slot host,
        INodeObjectOutput<IFingerPoseSourceComponent> source, Chirality side)
    {
        var sentinels = new List<INodeValueOutput<bool>>();
        for (var i = 0; i < 2; i++)
        {
            var slot = host.AddSlot(side + " Tracking Sentinel " + i);
            var bone = slot.AddSlot("Input-only finger rotation");
            var poser = slot.AttachComponent<HandPoser>();
            poser.Side.Value = side;
            poser.HandRoot.Target = slot;
            poser.HandForward.Value = float3.Forward;
            poser.HandUp.Value = float3.Up;
            poser.HandRight.Value = float3.Right;
            poser.Thumb.Proximal.Root.Target = bone;
            poser.Thumb.Proximal.OriginalRotation.Value = floatQ.Identity;
            poser.Thumb.Proximal.CoordinateCompensation.Value = i == 0 ? floatQ.Identity : floatQ.Euler(180, 0, 0);
            poser.Thumb.Proximal.RotationDrive.ForceLink(bone.Rotation_Field);
            graph.DriveReference(poser.PoseSource, source);
            sentinels.Add(graph.Not(graph.Less(graph.Angle(graph.Read(bone.Rotation_Field), graph.Constant(floatQ.Identity)), .5f)));
        }
        // Standard FingerPoseStreamManager returns rotations with zero/absent
        // positions. HandPoser calls AreFingersTracking and resets these helper
        // bones when tracking stops. While tracked they receive q and q*180°:
        // both cannot be Identity, including an all-Identity input pose.
        var valid = host.AddSlot(side + " Finger Tracking Valid").AttachComponent<ValueField<bool>>();
        graph.Drive(valid.Value, graph.All(graph.NotNull(source), graph.Any(sentinels.ToArray())));
        return graph.Read(valid.Value);
    }
}
