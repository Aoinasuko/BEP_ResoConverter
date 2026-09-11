using FrooxEngine;
using FrooxEngine.ProtoFlux;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Collections;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Interaction;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Interaction.Tools;
using Renderite.Shared;
using TouchNode = FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Input.Controllers.TouchController;

namespace nadena.dev.resonity.remote.puppeteer.rpc;

internal sealed record ControllerExpressionState(INodeValueOutput<int> Gesture,
    INodeValueOutput<bool> Active, INodeValueOutput<bool> Holding);

internal static class ExpressionControllers
{
    internal static ControllerExpressionState Build(ExpressionGraph graph, Slot host,
        INodeObjectOutput<User> wearer, Chirality side)
    {
        var touch = graph.Node<TouchNode>(side + " Touch controller inputs");
        touch.User.Target = wearer;
        touch.Node.Target = graph.Constant(side);
        var active = graph.All(graph.NotNull(wearer), touch.IsActive);
        var grip = graph.Any(graph.Not(graph.Less(touch.Grip, .5f)), touch.GripClick);
        var pulling = graph.Any(graph.Not(graph.Less(touch.Trigger, .5f)), touch.TriggerClick);
        var indexDown = graph.Any(touch.TriggerTouch, graph.Not(graph.Less(touch.Trigger, .05f)), touch.TriggerClick);
        var indexUp = graph.Not(indexDown);
        // VRChat's SteamVR default Touch bindings omit the thumb-rest sensor.
        var thumbDown = graph.Any(touch.JoystickTouch, touch.ButtonXA_Touch,
            touch.ButtonYB_Touch, touch.ButtonXA, touch.ButtonYB, touch.JoystickClick);
        var thumbUp = graph.Not(thumbDown);
        // Touch is capacitive: resting the index on the trigger is different
        // from pulling it. Unlisted combinations retain the relaxed Idle pose.
        var conditions = new INodeValueOutput<bool>[]
        {
            graph.Constant(false),
            graph.All(grip, pulling, thumbDown),
            graph.All(graph.Not(grip), indexUp, thumbUp),
            graph.All(grip, indexUp, thumbDown),
            graph.All(graph.Not(grip), indexUp, thumbDown),
            graph.All(graph.Not(grip), pulling, thumbDown),
            graph.All(grip, indexUp, thumbUp),
            graph.All(grip, indexDown, thumbUp),
        };
        INodeValueOutput<int> gesture = graph.Constant(0);
        for (var pose = 7; pose >= 1; pose--)
            gesture = graph.Choose(conditions[pose], graph.Constant(pose), gesture);
        gesture = graph.Choose(active, gesture, graph.Constant(-1));
        var state = host.AddSlot(side + " Controller Gesture").AttachComponent<ValueField<int>>();
        graph.Drive(state.Value, gesture);

        var grabber = graph.Node<GetUserGrabber>(side + " Wearer grabber");
        grabber.User.Target = wearer;
        grabber.Node.Target = graph.Constant(BodyNode.LeftHand.GetSide(side));
        var held = graph.Node<GrabbedGrabbables>(side + " Held objects");
        held.Grabber.Target = graph.ContinuousObject<Grabber>(grabber);
        var count = graph.Node<ReadOnlyCount<IReadOnlyList<IGrabbable>, IGrabbable>>(side + " Held count");
        count.Collection.Target = graph.ContinuousObject<IReadOnlyList<IGrabbable>>(held);
        var tool = graph.Node<HasTool>(side + " Wearer tool");
        tool.User.Target = wearer; tool.Side.Target = graph.Constant(side);
        var holding = graph.Any(graph.Not(graph.Less(graph.Continuous<int>(count), 1)), graph.Continuous<bool>(tool));
        var holdingField = host.AddSlot(side + " Holding Object Or Tool").AttachComponent<ValueField<bool>>();
        graph.Drive(holdingField.Value, holding);
        return new ControllerExpressionState(graph.Read(state.Value), active, graph.Read(holdingField.Value));
    }
}
