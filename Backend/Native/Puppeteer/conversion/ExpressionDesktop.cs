using FrooxEngine;
using FrooxEngine.ProtoFlux;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Input.Keyboard;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Users;
using ProtoFlux.Runtimes.Execution;
using Renderite.Shared;
using Core = FrooxEngine.FrooxEngine.ProtoFlux.CoreNodes;
using UpdateNode = FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Actions.Update;

namespace nadena.dev.resonity.remote.puppeteer.rpc;

internal sealed record ExpressionInputMode(INodeValueOutput<bool> VR, INodeValueOutput<bool> Desktop);

internal static class ExpressionDesktop
{
    // Number-row keys deliberately avoid Resonite's global F-key shortcuts.
    internal static readonly Key[] Keys = { Key.Alpha1, Key.Alpha2, Key.Alpha3, Key.Alpha4,
        Key.Alpha5, Key.Alpha6, Key.Alpha7, Key.Alpha8 };

    internal static ExpressionInputMode BuildMode(ExpressionGraph graph, INodeObjectOutput<User> wearer)
    {
        var device = graph.Node<UserHeadOutputDevice>("Wearer output device"); device.User.Target = wearer;
        var vr = graph.Node<UserVR_Active>("Wearer VR active"); vr.User.Target = wearer;
        var known = graph.Any(new[] { HeadOutputDevice.Screen, HeadOutputDevice.Screen360,
            HeadOutputDevice.SteamVR, HeadOutputDevice.WindowsMR, HeadOutputDevice.Oculus,
            HeadOutputDevice.OculusQuest }.Select(d => graph.Equal(graph.Continuous<HeadOutputDevice>(device), d)).ToArray());
        var present = graph.All(graph.NotNull(wearer), known);
        var active = graph.Continuous<bool>(vr);
        return new(graph.All(present, active), graph.All(present, graph.Not(active)));
    }

    internal static Dictionary<Chirality, ControllerExpressionState> Build(ExpressionGraph graph, Slot host,
        INodeObjectOutput<User> wearer, ExpressionInputMode mode)
    {
        var local = graph.Node<IsLocalUser>("Keyboard belongs to wearer"); local.User.Target = wearer;
        var canWrite = graph.All(mode.Desktop, graph.Continuous<bool>(local));
        var functionKeys = Keys.Select(key => Held(graph, key)).ToArray();
        var updatingUser = host.AddSlot("Desktop Updating Wearer").AttachComponent<GlobalReference<User>>();
        graph.DriveReference(updatingUser.Reference, wearer);
        var skipNull = host.AddSlot("Desktop Never Update Without Wearer").AttachComponent<GlobalValue<bool>>();
        skipNull.Value.Value = true;
        var result = new Dictionary<Chirality, ControllerExpressionState>();
        foreach (var side in new[] { Chirality.Left, Chirality.Right })
        {
            var shift = Held(graph, side == Chirality.Left ? Key.LeftShift : Key.RightShift);
            var gesture = BuildKeys(graph, graph.All(canWrite, shift), functionKeys);
            var state = host.AddSlot(side + " Desktop Key Gesture").AttachComponent<ValueField<int>>();
            state.Value.Value = -1;
            // A field drive would evaluate the observer's keyboard independently.
            // Write synchronizes the wearer's result; every observer only reads it.
            var variable = graph.Node<Core.ValueSource<int>>(side + " Synced Desktop Gesture");
            variable.TrySetRootSource(state.Value);
            var write = graph.Node<ValueWrite<FrooxEngineContext, int>>(side + " Publish Desktop Gesture");
            write.Variable.Target = variable;
            write.Value.Target = gesture;
            var update = graph.Node<UpdateNode>(side + " Wearer-only Keyboard Update");
            update.UpdatingUser.Target = updatingUser;
            update.SkipIfNull.Target = skipNull;
            update.OnUpdate.Target = write;
            var read = graph.Read(state.Value);
            var active = graph.All(mode.Desktop, graph.Not(graph.Less(read, 0)), graph.Less(read, 8));
            result[side] = new(read, active, ExpressionControllers.BuildHolding(graph, host, wearer, side));
        }
        return result;
    }

    internal static INodeValueOutput<int> BuildKeys(ExpressionGraph graph, INodeValueOutput<bool> enabled,
        IReadOnlyList<INodeValueOutput<bool>> keys)
    {
        INodeValueOutput<int> result = graph.Constant(-1);
        // If several numbers are held, the lowest number wins deterministically.
        for (var i = 7; i >= 0; i--) result = graph.Choose(keys[i], graph.Constant(i), result);
        return graph.Choose(enabled, result, graph.Constant(-1));
    }

    private static INodeValueOutput<bool> Held(ExpressionGraph graph, Key key)
    {
        var node = graph.Node<KeyHeld>("Desktop key " + key);
        node.Key.Target = graph.Constant(key);
        // Native KeyHeld also suppresses text-field focus, the dash and unfocused worlds.
        return graph.Continuous<bool>(node);
    }
}
