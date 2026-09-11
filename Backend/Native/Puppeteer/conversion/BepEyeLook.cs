using System.Text.Json;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.CommonAvatar;
using FrooxEngine.ProtoFlux;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Transform;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Math;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Math.Quaternions;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Operators;
using p = nadena.dev.ndmf.proto;

namespace nadena.dev.resonity.remote.puppeteer.rpc;

internal sealed class EyeLookSettings
{
    public bool configured { get; set; }
    public bool enabled { get; set; }
    public EyeLookBone? left { get; set; }
    public EyeLookBone? right { get; set; }
}

internal sealed class EyeLookBone
{
    public ulong boneId { get; set; }
    public EyeLookRotation straight { get; set; } = new();
    public EyeLookRotation up { get; set; } = new();
    public EyeLookRotation down { get; set; } = new();
    public EyeLookRotation left { get; set; } = new();
    public EyeLookRotation right { get; set; } = new();
}

internal sealed class EyeLookRotation
{
    public float x { get; set; }
    public float y { get; set; }
    public float z { get; set; }
    public float w { get; set; } = 1;

    public floatQ Rotation()
    {
        var length = MathF.Sqrt(x * x + y * y + z * z + w * w);
        if (!float.IsFinite(length) || length < .00001f)
            throw new InvalidOperationException("The avatar contains an invalid eye-look rotation.");
        return new floatQ(x / length, y / length, z / length, w / length);
    }
}

public partial class RootConverter
{
    private void SetupBepEyeLook(p.AvatarDescriptor spec)
    {
        if (_options.eyeLook.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return;
        var settings = _options.eyeLook.Deserialize<EyeLookSettings>()!;
        if (!settings.configured) return;

        // AvatarCreator inserts aligned pivots and drives them with a generic
        // circular 10-degree limit. Restore their neutral orientation before
        // applying the author's eye poses in the original bone-parent space.
        foreach (var automatic in _root.GetComponentsInChildren<EyeRotationDriver>().ToArray())
        {
            foreach (var eye in automatic.Eyes)
            {
                var pivot = eye.Root.Target;
                eye.Rotation.Target = null;
                if (pivot == null) continue;
                pivot.LocalRotation = floatQ.LookRotation(eye.ForwardDirection.Value, eye.Up.Value);
                foreach (var bone in pivot.Children.ToArray()) bone.SetParent(pivot.Parent);
                pivot.Destroy();
            }
            automatic.Destroy();
        }

        var host = _root.AddSlot("[BEP] Eye Look");
        host.AddSlot("Configuration").AttachComponent<ValueField<string>>().Value.Value = _options.eyeLook.GetRawText();
        var manager = _root.GetComponentInChildren<EyeManager>();
        if (settings.enabled && manager == null)
        {
            var head = Object<Slot>(spec.Bones.Head) ?? _root;
            var reference = head.AddSlot("Eye Manager");
            reference.GlobalPosition = _root.LocalPointToGlobal(spec.EyePosition.Vec3());
            reference.GlobalRotation = _root.GlobalRotation;
            manager = reference.AttachComponent<EyeManager>();
            reference.AttachComponent<AvatarEyeDataSourceAssigner>().TargetReference.Target = manager.EyeDataSource;
            reference.AttachComponent<AvatarUserReferenceAssigner>().References.Add(manager.SimulatingUser);
        }
        foreach (var (side, config) in new[] { (EyeSide.Left, settings.left), (EyeSide.Right, settings.right) })
        {
            if (config == null || config.boneId == 0) continue;
            var bone = Object<Slot>(new p.ObjectID { Id = config.boneId })
                ?? throw new InvalidOperationException("The eye-look bone is missing: " + config.boneId);
            bone.Rotation_Field.ActiveLink?.ReleaseLink();
            bone.LocalRotation = config.straight.Rotation();
            var eyeHost = host.AddSlot(side.ToString());
            eyeHost.AttachComponent<ReferenceField<Slot>>().Reference.Target = bone;
            if (settings.enabled) BepEyeLookGraph.Build(eyeHost, manager!, side, bone, config);
        }
    }
}

internal static class BepEyeLookGraph
{
    // Input gaze reaches each authored directional pose at 30 degrees. This is
    // an input-response range, not an additional rotation applied to the mesh.
    internal const float InputRangeDegrees = 30;

    internal static void Build(Slot host, EyeManager manager, EyeSide side, Slot bone, EyeLookBone config)
    {
        var graph = new ExpressionGraph(host.AddSlot("Logic"));
        var input = host.AddSlot("Gaze Target").AttachComponent<ValueField<float3>>();
        input.Slot.AttachComponent<ValueDriver<float3>>().ValueSource.Target = side == EyeSide.Left
            ? manager.LeftEyeTargetPoint : manager.RightEyeTargetPoint;
        input.Slot.GetComponent<ValueDriver<float3>>().DriveTarget.Target = input.Value;
        var local = graph.Node<GlobalPointToLocal>("Gaze in head reference space");
        local.Instance.Target = graph.Reference(manager.Slot);
        local.GlobalPoint.Target = graph.Read(input.Value);
        // Transform nodes depend on moving ancestors, not only their explicit
        // inputs. Sample continuously so head turns never leave stale eye input.
        var xyz = graph.Node<Unpack_Float3>("Gaze direction");
        xyz.V.Target = graph.Continuous(local);
        var horizontalSquared = graph.Add(Multiply(xyz.X, xyz.X), Multiply(xyz.Z, xyz.Z));
        var coincident = graph.Less(graph.Add(horizontalSquared, Multiply(xyz.Y, xyz.Y)), .0000000001f);
        var yaw = graph.Choose(coincident, graph.Constant(0f), Atan2(xyz.X, xyz.Z));
        var horizontalLength = graph.Node<Sqrt_Float>("Horizontal gaze distance");
        horizontalLength.N.Target = horizontalSquared;
        var pitch = graph.Choose(coincident, graph.Constant(0f), Atan2(xyz.Y, horizontalLength));
        var straight = config.straight.Rotation();
        var horizontal = Axis(yaw, config.left.Rotation(), config.right.Rotation());
        var vertical = Axis(pitch, config.down.Rotation(), config.up.Rotation());
        // Compose two bounded authored offsets. If up/down equal straight, the
        // vertical factor remains identity even when looking far up or down.
        var verticalOffset = Multiply(vertical, graph.Constant(straight.Inverted));
        graph.Drive(bone.Rotation_Field, Multiply(verticalOffset, horizontal));

        INodeValueOutput<float> Atan2(INodeValueOutput<float> y, INodeValueOutput<float> x)
        {
            var node = graph.Node<Atan2_Float>("Gaze angle radians");
            node.Y.Target = y; node.X.Target = x; return node;
        }
        INodeValueOutput<T> Multiply<T>(INodeValueOutput<T> a, INodeValueOutput<T> b) where T : unmanaged
        {
            var node = graph.Node<ValueMul<T>>("Apply eye offset");
            node.A.Target = a; node.B.Target = b; return node;
        }
        INodeValueOutput<floatQ> Axis(INodeValueOutput<float> angle, floatQ negative, floatQ positive)
        {
            var abs = graph.Node<ValueAbs<float>>("Unsigned gaze angle"); abs.N.Target = angle;
            var normalized = Multiply(abs, graph.Constant(180f / (MathF.PI * InputRangeDegrees)));
            var clamp = graph.Node<ValueClamp<float>>("Keep authored eye range");
            clamp.Value.Target = normalized; clamp.Min.Target = graph.Constant(0f); clamp.Max.Target = graph.Constant(1f);
            var result = graph.Node<Slerp_floatQ>("Authored eye pose");
            result.From.Target = graph.Constant(straight);
            result.To.Target = graph.Choose(graph.Less(angle, 0f), graph.Constant(negative), graph.Constant(positive));
            result.Lerp.Target = clamp;
            return result;
        }
    }
}
