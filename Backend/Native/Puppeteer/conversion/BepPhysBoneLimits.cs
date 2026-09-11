using System.Text.Json;
using Elements.Core;
using FrooxEngine;
using p = nadena.dev.ndmf.proto;

namespace nadena.dev.resonity.remote.puppeteer.rpc;

// Generates stock components only; this builder is not a runtime dependency.
internal static class BepPhysBoneLimits
{
    internal const string HostName = "[BEP] PhysBone Limits";
    internal sealed record Settings(int Type, float MaxAngleX, float MaxAngleZ,
        float3 Axis, floatQ Frame, floatQ Rest, float3 Scale);

    internal static Dictionary<Slot, Slot> Setup(DynamicBoneChain chain, Slot root,
        IReadOnlyList<(Slot Slot, p.DynamicBoneNode Spec)> bones, int limitType, string multiChildType)
    {
        var host = chain.Slot.AddSlot(HostName);
        host.AttachComponent<ReferenceField<DynamicBoneChain>>().Reference.Target = chain;
        host.AddSlot("Solver behavior").AttachComponent<ValueField<string>>().Value.Value =
            "Angle limits constrain rendered bones after native simulation. " +
            "Collision and grabbing use the simulation proxy and can differ at a limit.";
        var proxies = new Dictionary<Slot, Slot>();
        chain.SimulateTerminalBones.Value = false;
        // A nested chain follows its limited real parent, not an outer proxy.
        var anchor = root.Parent.AddSlot("[BEP] PhysBone Simulation - " + root.Name);
        var map = bones.Select(b => b.Slot).ToHashSet();
        foreach (var (bone, _) in bones) ClonePath(bone);
        foreach (var pair in proxies)
        {
            var binding = host.AddSlot("Mapped Bone " + pair.Key.Name);
            binding.AttachComponent<ReferenceField<Slot>>().Reference.Target = pair.Key;
            binding.AddSlot("Simulation").AttachComponent<ReferenceField<Slot>>().Reference.Target = pair.Value;
        }
        foreach (var (bone, spec) in bones)
        {
            var children = bone.Children.Where(child => map.Contains(child)
                || child.GetAllChildren().Any(map.Contains)).ToArray();
            if (multiChildType == "First") children = children.Take(1).ToArray();
            // No endpoint means no outgoing simulated segment. Do not invent
            // one from the incoming bone; doing so animates static nested roots.
            if (children.Length == 0) continue;
            if (bone.Rotation_Field.IsDriven)
                throw new InvalidOperationException("Multiple drivers target PhysBone " + bone.Name +
                    ". Check overlapping roots and ignore transforms.");
            var axis = children.Aggregate(float3.Zero, (sum, child) => sum + child.LocalPosition);
            if (axis.Magnitude < 1e-6f) continue;
            axis = axis.Normalized;
            if (MathF.Abs(bone.LocalScale.x) < .000001f || MathF.Abs(bone.LocalScale.y) < .000001f
                || MathF.Abs(bone.LocalScale.z) < .000001f)
                throw new InvalidOperationException("PhysBone angle limits require nonzero bone scale: " + bone.Name);
            var rotation = spec.LimitRotation?.Quat() ?? floatQ.Identity;
            var settings = new Settings(limitType,
                FiniteAngle(spec.HasMaxAngleX ? spec.MaxAngleX : 180f, 180f),
                FiniteAngle(spec.HasMaxAngleZ ? spec.MaxAngleZ : 90f, 90f), axis,
                floatQ.FromToRotation(float3.Up, axis) * rotation, bone.LocalRotation, bone.LocalScale);
            var output = host.AddSlot("Bone " + bone.Name);
            output.AttachComponent<ReferenceField<Slot>>().Reference.Target = bone;
            output.AddSlot("Simulation").AttachComponent<ReferenceField<Slot>>().Reference.Target = proxies[bone];
            output.AttachComponent<ValueField<string>>().Value.Value = JsonSerializer.Serialize(new {
                settings.Type, settings.MaxAngleX, settings.MaxAngleZ,
                Axis = new[] { axis.x, axis.y, axis.z },
                Frame = new[] { settings.Frame.x, settings.Frame.y, settings.Frame.z, settings.Frame.w },
                Rest = new[] { settings.Rest.x, settings.Rest.y, settings.Rest.z, settings.Rest.w },
                Scale = new[] { settings.Scale.x, settings.Scale.y, settings.Scale.z }
            });
            foreach (var child in children)
                output.AddSlot("Endpoint " + child.Name).AttachComponent<ReferenceField<Slot>>().Reference.Target = proxies[child];
            BepPhysBoneLimitGraph.Build(output.AddSlot("Logic"), chain.EnabledField,
                proxies[bone].Rotation_Field, children.Select(child => (IField<float3>)proxies[child].Position_Field).ToArray(),
                bone.Rotation_Field, settings);
        }
        return proxies;

        Slot ClonePath(Slot source)
        {
            if (proxies.TryGetValue(source, out var copy)) return copy;
            var parent = source == root ? anchor : ClonePath(source.Parent);
            copy = parent.AddSlot(source.Name);
            copy.LocalPosition = source.LocalPosition;
            copy.LocalRotation = source.LocalRotation;
            copy.LocalScale = source.LocalScale;
            proxies.Add(source, copy);
            return copy;
        }
    }

    internal static Settings ReadSettings(Slot host)
    {
        using var json = JsonDocument.Parse(host.GetComponent<ValueField<string>>().Value.Value);
        var o = json.RootElement;
        float[] Array(string name) => o.GetProperty(name).EnumerateArray().Select(v => v.GetSingle()).ToArray();
        var a = Array("Axis"); var f = Array("Frame"); var r = Array("Rest"); var scale = Array("Scale");
        return new Settings(o.GetProperty("Type").GetInt32(), o.GetProperty("MaxAngleX").GetSingle(),
            o.GetProperty("MaxAngleZ").GetSingle(), new float3(a[0], a[1], a[2]),
            new floatQ(f[0], f[1], f[2], f[3]), new floatQ(r[0], r[1], r[2], r[3]),
            new float3(scale[0], scale[1], scale[2]));
    }

    private static float FiniteAngle(float value, float maximum)
    {
        if (!float.IsFinite(value)) throw new InvalidOperationException("PhysBone angle is not finite.");
        return Math.Clamp(value, 0, maximum);
    }

}
