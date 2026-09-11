using Elements.Core;
using FrooxEngine;
using FrooxEngine.ProtoFlux;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Operators;
using Core = FrooxEngine.FrooxEngine.ProtoFlux.CoreNodes;

namespace nadena.dev.resonity.remote.puppeteer.rpc;

// Only native Resonite components are serialized. This builder is not a runtime
// dependency of the exported avatar.
internal sealed class ExpressionGraph(Slot root)
{
    public T Node<T>(string name) where T : Component, new() => root.AddSlot(name).AttachComponent<T>();

    public INodeValueOutput<T> Constant<T>(T value) where T : unmanaged
    {
        var input = Node<ValueInput<T>>("Constant " + value);
        input.Value.Value = value;
        return input;
    }

    public INodeObjectOutput<T> Reference<T>(T value) where T : class, IWorldElement
    {
        var input = Node<RefObjectInput<T>>("Reference " + typeof(T).Name);
        input.Target.Target = value;
        return input;
    }

    public INodeValueOutput<bool> NotNull<T>(INodeObjectOutput<T> value) where T : class
    {
        var node = Node<NotNull<T>>("Has " + typeof(T).Name);
        node.Instance.Target = value;
        return node;
    }

    public INodeValueOutput<T> Read<T>(IField<T> field) where T : unmanaged
    {
        var source = Node<Core.ValueSource<T>>("Read " + field.Name);
        if (!source.TrySetRootSource(field)) throw new InvalidOperationException("Cannot connect expression field source.");
        return source;
    }

    public INodeObjectOutput<T> ReadObject<T>(IWorldElement field) where T : class, IWorldElement
    {
        var source = Node<Core.ReferenceSource<T>>("Read object");
        if (!source.TrySetRootSource(field)) throw new InvalidOperationException("Cannot connect expression object source.");
        return source;
    }

    public void Drive<T>(IField<T> target, INodeValueOutput<T> value) where T : unmanaged
    {
        var drive = Node<Core.ValueFieldDrive<T>>("Drive " + target.Name);
        drive.Value.Target = value;
        if (!drive.TrySetRootTarget(target)) throw new InvalidOperationException("Cannot drive expression target.");
    }

    public INodeValueOutput<T> Continuous<T>(INodeValueOutput<T> value) where T : unmanaged
    {
        var relay = Node<ContinuouslyChangingValueRelay<T>>("Sample live finger pose");
        relay.Input.Target = value;
        return relay;
    }

    public INodeObjectOutput<T> ContinuousObject<T>(INodeObjectOutput<T> value) where T : class
    {
        var relay = Node<ContinuouslyChangingObjectRelay<T>>("Read current " + typeof(T).Name);
        relay.Input.Target = value;
        return relay;
    }

    public void DriveReference<T>(SyncRef<T> target, INodeObjectOutput<T> value) where T : class, IWorldElement
    {
        var drive = Node<Core.ReferenceDrive<T>>("Drive reference " + target.Name);
        drive.Target.Target = value;
        if (!drive.TrySetRootTarget(target)) throw new InvalidOperationException("Cannot drive expression reference.");
    }

    public INodeObjectOutput<T> ChooseObject<T>(INodeValueOutput<bool> condition, INodeObjectOutput<T> yes, INodeObjectOutput<T> no) where T : class
    {
        var node = Node<ObjectConditional<T>>("Choose " + typeof(T).Name);
        node.Condition.Target = condition; node.OnTrue.Target = yes; node.OnFalse.Target = no;
        return node;
    }

    public INodeValueOutput<T> Choose<T>(INodeValueOutput<bool> condition, INodeValueOutput<T> yes, INodeValueOutput<T> no) where T : unmanaged
    {
        var node = Node<ValueConditional<T>>("Choose");
        node.Condition.Target = condition;
        node.OnTrue.Target = yes;
        node.OnFalse.Target = no;
        return node;
    }

    public INodeValueOutput<bool> Equal<T>(INodeValueOutput<T> value, T other) where T : unmanaged
    {
        var node = Node<ValueEquals<T>>("Equals " + other);
        node.A.Target = value;
        node.B.Target = Constant(other);
        return node;
    }

    public INodeValueOutput<bool> Less<T>(INodeValueOutput<T> value, T other) where T : unmanaged
    {
        var node = Node<ValueLessThan<T>>("Below " + other);
        node.A.Target = value;
        node.B.Target = Constant(other);
        return node;
    }

    public INodeValueOutput<bool> Not(INodeValueOutput<bool> value)
    {
        var node = Node<NOT_Bool>("Not"); node.A.Target = value; return node;
    }

    public INodeValueOutput<bool> All(params INodeValueOutput<bool>[] values)
    {
        if (values.Length == 0) return Constant(true);
        var result = values[0];
        foreach (var value in values.Skip(1))
        {
            var node = Node<AND_Bool>("And"); node.A.Target = result; node.B.Target = value; result = node;
        }
        return result;
    }

    public INodeValueOutput<bool> Any(params INodeValueOutput<bool>[] values) => Not(All(values.Select(Not).ToArray()));

    public INodeValueOutput<float> Angle(INodeValueOutput<floatQ> a, INodeValueOutput<floatQ> b)
    {
        var node = Node<Angle_floatQ>("Joint bend degrees"); node.A.Target = a; node.B.Target = b; return node;
    }

    public INodeValueOutput<float> Add(INodeValueOutput<float> a, INodeValueOutput<float> b)
    {
        var node = Node<ValueAdd<float>>("Total finger bend"); node.A.Target = a; node.B.Target = b; return node;
    }
}
