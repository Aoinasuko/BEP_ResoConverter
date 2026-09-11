using Elements.Core;
using FrooxEngine;
using FrooxEngine.ProtoFlux;
using Renderite.Shared;
using PoseNode = FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Avatar.FingerPose;

namespace nadena.dev.resonity.remote.puppeteer.rpc;

internal static class ExpressionGestures
{
    internal static readonly string[] Names = { "Idle", "Fist", "Open", "Point", "Victory", "RockNRoll", "HandGun", "ThumbsUp" };

    internal static int Parse(string name)
    {
        if (name == "Any") return -1;
        var index = Array.IndexOf(Names, name);
        if (index < 0) throw new InvalidOperationException("Unknown hand expression pose: " + name);
        return index;
    }

    internal static INodeValueOutput<int> Build(ExpressionGraph graph, Slot host,
        INodeObjectOutput<IFingerPoseSourceComponent> source, Chirality side)
    {
        var proximal = new[] { BodyNode.LeftThumb_Proximal, BodyNode.LeftIndexFinger_Proximal,
            BodyNode.LeftMiddleFinger_Proximal, BodyNode.LeftRingFinger_Proximal, BodyNode.LeftPinky_Proximal };
        var distal = new[] { BodyNode.LeftThumb_Distal, BodyNode.LeftIndexFinger_Distal,
            BodyNode.LeftMiddleFinger_Distal, BodyNode.LeftRingFinger_Distal, BodyNode.LeftPinky_Distal };
        var curls = new INodeValueOutput<float>[5];
        var valid = new List<INodeValueOutput<bool>>();
        for (var finger = 0; finger < 5; finger++)
        {
            PoseNode Pose(BodyNode bone)
            {
                var node = graph.Node<PoseNode>(side + " " + bone);
                node.PoseSource.Target = source;
                node.FingerNode.Target = graph.Constant(bone.GetSide(side));
                return node;
            }
            var near = Pose(proximal[finger]);
            var far = Pose(distal[finger]);
            // Missing tracking returns a zero position and identity quaternion.
            // Do not mistake that for a fully extended hand.
            valid.Add(graph.Not(graph.Equal(graph.Continuous(near.Position), float3.Zero)));
            valid.Add(graph.Not(graph.Equal(graph.Continuous(far.Position), float3.Zero)));
            if (finger == 0) curls[finger] = graph.Angle(graph.Continuous(near.Rotation), graph.Continuous(far.Rotation));
            else
            {
                var middle = Pose(proximal[finger] + 1);
                valid.Add(graph.Not(graph.Equal(graph.Continuous(middle.Position), float3.Zero)));
                var nearRotation = graph.Continuous(near.Rotation);
                var middleRotation = graph.Continuous(middle.Rotation);
                var farRotation = graph.Continuous(far.Rotation);
                curls[finger] = graph.Add(graph.Angle(nearRotation, middleRotation), graph.Angle(middleRotation, farRotation));
            }
            var curlField = host.AddSlot(side + " Finger Curl " + finger).AttachComponent<ValueField<float>>();
            graph.Drive(curlField.Value, curls[finger]);
        }

        // Deliberately leave a gap between extended and folded fingers. Relaxed
        // or ambiguous combinations fall back to Idle. Open requires a much
        // straighter hand than Idle; the native idle presets bend 11-30 degrees.
        var extended = curls.Select(v => graph.Less(v, 50f)).ToArray();
        var folded = curls.Select(v => graph.Not(graph.Less(v, 75f))).ToArray();
        var thumbStraight = graph.Less(curls[0], 8f);
        var fourFolded = graph.All(folded[1], folded[2], folded[3], folded[4]);
        var pointing = graph.All(extended[1], folded[2], folded[3], folded[4]);
        var conditions = new INodeValueOutput<bool>[]
        {
            graph.Constant(false),
            graph.All(fourFolded, graph.Not(thumbStraight)),
            graph.All(curls.Skip(1).Select(v => graph.Less(v, 17f)).Append(thumbStraight).ToArray()),
            graph.All(pointing, graph.Not(thumbStraight)),
            graph.All(extended[1], extended[2], folded[3], folded[4]),
            graph.All(extended[1], folded[2], folded[3], extended[4]),
            graph.All(pointing, thumbStraight),
            graph.All(fourFolded, thumbStraight),
        };
        INodeValueOutput<int> pose = graph.Constant(0);
        for (var i = conditions.Length - 1; i > 0; i--)
            pose = graph.Choose(conditions[i], graph.Constant(i), pose);
        pose = graph.Choose(graph.All(valid.ToArray()), pose, graph.Constant(-1));
        var output = host.AddSlot(side + " Gesture").AttachComponent<ValueField<int>>();
        graph.Drive(output.Value, pose);
        return graph.Read(output.Value);
    }
}
