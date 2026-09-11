using Elements.Core;
using FrooxEngine;
using FrooxEngine.ProtoFlux;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Math;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Math.Quaternions;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Operators;

namespace nadena.dev.resonity.remote.puppeteer.rpc;

internal static class BepPhysBoneLimitGraph
{
    internal static void Build(Slot host, IField<bool> enabled, IField<floatQ> source,
        IReadOnlyList<IField<float3>> childPositions, IField<floatQ> target, BepPhysBoneLimits.Settings settings)
    {
        var g = new ExpressionGraph(host);
        var relative = Mul(g.Constant(settings.Rest.Inverted), g.Read(source));
        INodeValueOutput<float3> endpoint = g.Read(childPositions[0]);
        foreach (var position in childPositions.Skip(1))
        {
            var add = g.Node<ValueAdd<float3>>("Average branch endpoint");
            add.A.Target = endpoint; add.B.Target = g.Read(position); endpoint = add;
        }
        var normalized = g.Node<Normalized_Float3>("Simulated segment direction");
        var unscale = g.Node<ValueDiv<float3>>("Endpoint in authored rest transform");
        unscale.A.Target = Rotate(relative, Mul(endpoint, g.Constant(settings.Scale)));
        unscale.B.Target = g.Constant(settings.Scale);
        normalized.A.Target = unscale;
        var direction = g.Choose(g.Equal(endpoint, float3.Zero), g.Constant(settings.Axis), normalized);
        var local = Rotate(g.Constant(settings.Frame.Inverted), direction);
        var xyz = g.Node<Unpack_Float3>("Direction in authored limit frame"); xyz.V.Target = local;
        var maxPitch = settings.MaxAngleX * MathF.PI / 180f;
        var maxYaw = settings.MaxAngleZ * MathF.PI / 180f;
        INodeValueOutput<float3> bounded;
        if (settings.Type == 1)
        {
            var radial = Sqrt(g.Add(Mul(xyz.X, xyz.X), Mul(xyz.Z, xyz.Z)));
            var angle = Clamp(Atan(radial, xyz.Y), 0, maxPitch);
            var denominator = Clamp(radial, .000001f, 1f);
            var x = g.Choose(g.Less(radial, .000001f), g.Constant(0f), Divide(xyz.X, denominator));
            var z = g.Choose(g.Less(radial, .000001f), g.Constant(1f), Divide(xyz.Z, denominator));
            bounded = Pack(Mul(x, Sin(angle)), Cos(angle), Mul(z, Sin(angle)));
        }
        else
        {
            // Hinge is a great-circle arc about X. Polar adds bounded latitude.
            var pitch = Clamp(Atan(xyz.Z, xyz.Y), -maxPitch, maxPitch);
            var yaw = settings.Type == 2 ? g.Constant(0f) : Clamp(
                Atan(xyz.X, Sqrt(g.Add(Mul(xyz.Y, xyz.Y), Mul(xyz.Z, xyz.Z)))), -maxYaw, maxYaw);
            bounded = Pack(Sin(yaw), Mul(Cos(yaw), Cos(pitch)), Mul(Cos(yaw), Sin(pitch)));
        }
        var corrected = Rotate(g.Constant(settings.Frame), bounded);
        var correction = g.Node<FromToRotation_floatQ>("Keep simulated swing inside authored limits");
        correction.From.Target = g.Constant(settings.Axis); correction.To.Target = corrected;
        // Restore the authored rotation and swing its outgoing rest segment to
        // the bounded endpoint. First/Average branches also work when the native
        // solver leaves a branching parent's own rotation unchanged.
        var result = Mul(g.Constant(settings.Rest), correction);
        g.Drive(target, g.Choose(g.Read(enabled), result, g.Constant(settings.Rest)));

        INodeValueOutput<T> Mul<T>(INodeValueOutput<T> a, INodeValueOutput<T> b) where T : unmanaged
        { var n = g.Node<ValueMul<T>>("Multiply"); n.A.Target = a; n.B.Target = b; return n; }
        INodeValueOutput<float3> Rotate(INodeValueOutput<floatQ> q, INodeValueOutput<float3> v)
        { var n = g.Node<Mul_FloatQ_Float3>("Rotate direction"); n.A.Target = q; n.B.Target = v; return n; }
        INodeValueOutput<float> Divide(INodeValueOutput<float> a, INodeValueOutput<float> b)
        { var n = g.Node<ValueDiv<float>>("Normalize radial direction"); n.A.Target = a; n.B.Target = b; return n; }
        INodeValueOutput<float> Atan(INodeValueOutput<float> y, INodeValueOutput<float> x)
        { var n = g.Node<Atan2_Float>("Signed limit angle"); n.Y.Target = y; n.X.Target = x; return n; }
        INodeValueOutput<float> Sqrt(INodeValueOutput<float> x)
        { var n = g.Node<Sqrt_Float>("Plane radius"); n.N.Target = x; return n; }
        INodeValueOutput<float> Sin(INodeValueOutput<float> x)
        { var n = g.Node<Sin_Float>("Sine"); n.N.Target = x; return n; }
        INodeValueOutput<float> Cos(INodeValueOutput<float> x)
        { var n = g.Node<Cos_Float>("Cosine"); n.N.Target = x; return n; }
        INodeValueOutput<float> Clamp(INodeValueOutput<float> x, float min, float max)
        { var n = g.Node<ValueClamp<float>>("Authored angular bound"); n.Value.Target = x; n.Min.Target = g.Constant(min); n.Max.Target = g.Constant(max); return n; }
        INodeValueOutput<float3> Pack(INodeValueOutput<float> x, INodeValueOutput<float> y, INodeValueOutput<float> z)
        { var n = g.Node<Pack_Float3>("Bounded direction"); n.X.Target = x; n.Y.Target = y; n.Z.Target = z; return n; }
    }
}
