using System.Text.Json;
using Elements.Core;
using FrooxEngine;
using nadena.dev.resonity.remote.puppeteer.rpc;
using p = nadena.dev.ndmf.proto;

namespace nadena.dev.resonity.remote.puppeteer;

internal static class PhysBoneLimitVerification
{
    internal static async Task<object?> Inspect(World world, Slot container)
    {
        var hosts = container.GetAllChildren().Where(s => s.Name == BepPhysBoneLimits.HostName).ToArray();
        if (hosts.Length == 0) return null;
        var records = new List<object>();
        var activeMeshBones = new HashSet<Slot>();
        // MeshLoadingFilter temporarily drives renderer.Enabled=false while
        // shaders/materials load (which may never finish in a headless engine).
        // Use the exporter's authored visibility snapshot, then current slot
        // activity, exactly as the physics visibility relay does.
        var sourceMeshes = container.GetComponentsInChildren<ReferenceField<MeshRenderer>>()
            .Where(r => r.Slot.Name == "[BEP] Visible Exported Renderers")
            .Select(r => r.Reference.Target).OfType<SkinnedMeshRenderer>().Where(r => r.Slot.IsActive).ToArray();
        foreach (var mesh in sourceMeshes)
        foreach (var binding in mesh.Mesh.Asset?.Data?.RawBoneBindings ?? [])
        for (var i = 0; i < 4; i++)
            if (binding[i].weight > 0 && binding[i].boneIndex < mesh.Bones.Count && mesh.Bones[binding[i].boneIndex] is { } b)
                activeMeshBones.Add(b);
        var liveChecks = 0; var enabledChains = 0;
        foreach (var host in hosts)
        {
            var chain = host.GetComponent<ReferenceField<DynamicBoneChain>>().Reference.Target;
            if (chain == null || chain.SimulateTerminalBones.Value)
                throw new InvalidOperationException("Limited chain has missing simulation or an invented endpoint.");
            var shouldSimulate = host.Children.Where(s => s.Name.StartsWith("Mapped Bone "))
                .Select(s => s.GetComponent<ReferenceField<Slot>>().Reference.Target).Any(activeMeshBones.Contains);
            if (shouldSimulate && !chain.Enabled)
                throw new InvalidOperationException("Visible limited chain was disabled by renderer gating: " + chain.Slot.Parent.Name);
            if (chain.Enabled) enabledChains++;
            foreach (var boneHost in host.Children.Where(s => s.Name.StartsWith("Bone ")))
            {
                var bone = boneHost.GetComponent<ReferenceField<Slot>>().Reference.Target;
                var proxy = boneHost.FindChild("Simulation").GetComponent<ReferenceField<Slot>>().Reference.Target;
                var config = BepPhysBoneLimits.ReadSettings(boneHost);
                if (bone == null || proxy == null || !proxy.IsChildOf(container) || !bone.Rotation_Field.IsDriven
                    || chain.Bones.Any(b => b.BoneSlot.Target == bone) || !chain.Bones.Any(b => b.BoneSlot.Target == proxy))
                    throw new InvalidOperationException("PhysBone limit graph has missing or competing roundtrip references.");
                records.Add(new { bone = bone.Name, type = config.Type, maxAngleX = config.MaxAngleX,
                    maxAngleZ = config.MaxAngleZ, initialEnabled = chain.Enabled, rendererUsesChain = shouldSimulate });
            }
        }
        var previousFull = world.ForceFullUpdateCycle; world.ForceFullUpdateCycle = true;
        var restore = new List<Action>();
        var renderedMotion = new List<object>();
        try
        {
            var constrained = hosts.SelectMany(h => h.Children.Where(s => s.Name.StartsWith("Bone ")))
                .Select(s => s.GetComponent<ReferenceField<Slot>>().Reference.Target).ToArray();
            var affectedMeshes = sourceMeshes.Where(m => (m.Mesh.Asset?.Data?.RawBoneBindings ?? [])
                .SelectMany(b => Enumerable.Range(0, 4).Select(i => b[i])).Where(b => b.weight > 0)
                .Select(b => m.Bones[b.boneIndex]).Any(b => constrained.Any(c => b.IsChildOf(c, includeSelf: true)))).ToArray();
            var before = new Dictionary<SkinnedMeshRenderer, float3[]>();
            var spaces = new Dictionary<SkinnedMeshRenderer, Slot>();
            var indices = new Dictionary<SkinnedMeshRenderer, int[]>();
            foreach (var mesh in affectedMeshes)
            {
                var weighted = mesh.Mesh.Asset.Data.RawBoneBindings.SelectMany(b => Enumerable.Range(0,4).Select(i => b[i]))
                    .Where(b => b.weight > 0).Select(b => mesh.Bones[b.boneIndex]).Distinct().ToArray();
                var parents = constrained.Where(c => weighted.Any(b => b.IsChildOf(c, includeSelf: true))).Select(c => c.Parent).ToArray();
                var common = parents[0];
                while (parents.Any(p => !p.IsChildOf(common, includeSelf: true))) common = common.Parent;
                spaces[mesh] = common;
                indices[mesh] = mesh.Mesh.Asset.Data.RawBoneBindings.Select((b, index) => (b, index))
                    .Where(v => Enumerable.Range(0,4).Any(i => v.b[i].weight > 0
                        && constrained.Any(c => mesh.Bones[v.b[i].boneIndex].IsChildOf(c, includeSelf: true))))
                    .Select(v => v.index).ToArray();
                before[mesh] = await Points(mesh);
            }
            foreach (var host in hosts)
            {
                var chain = host.GetComponent<ReferenceField<DynamicBoneChain>>().Reference.Target;
                if (!chain.Enabled) continue;
                var force = chain.LocalForce.Value; var elasticity = chain.Elasticity.Value;
                var damping = chain.Damping.Value; var stiffness = chain.Stiffness.Value;
                restore.Add(() => { chain.LocalForce.Value = force; chain.Elasticity.Value = elasticity;
                    chain.Damping.Value = damping; chain.Stiffness.Value = stiffness; });
                chain.LocalForce.Value = new float3(30, 10, -15); chain.Elasticity.Value = 10;
                chain.Damping.Value = 30; chain.Stiffness.Value = 0;
            }
            for (var sample = 0; sample < 10; sample++)
            {
                for (var frame = 0; frame < 12; frame++) await new NextUpdate();
                foreach (var host in hosts)
                {
                    var chain = host.GetComponent<ReferenceField<DynamicBoneChain>>().Reference.Target;
                    if (!chain.Enabled) continue;
                    foreach (var boneHost in host.Children.Where(s => s.Name.StartsWith("Bone ")))
                    {
                        var bone = boneHost.GetComponent<ReferenceField<Slot>>().Reference.Target;
                        var config = BepPhysBoneLimits.ReadSettings(boneHost);
                        CheckBounds(config, config.Rest.Inverted * bone.LocalRotation * config.Axis, "imported " + bone.Name);
                        liveChecks++;
                    }
                }
            }
            foreach (var mesh in affectedMeshes)
            {
                var after = await Points(mesh); var first = before[mesh];
                if (after.Length != first.Length) throw new InvalidOperationException("Skinned mesh changed during physics verification.");
                var maxMotion = 0f; var moved = 0;
                foreach (var i in indices[mesh])
                {
                    var distance = (after[i] - first[i]).Magnitude;
                    maxMotion = MathF.Max(maxMotion, distance); if (distance > .00001f) moved++;
                }
                renderedMotion.Add(new { mesh = mesh.Slot.Name, vertices = after.Length,
                    affectedVertices = indices[mesh].Length, referenceBone = spaces[mesh].Name,
                    movedVertices = moved, maxMovementInReferenceSpace = maxMotion });
            }

            async Task<float3[]> Points(SkinnedMeshRenderer mesh)
            {
                var points = new List<float3>();
                await mesh.ForeachExactBoundedPoint(spaces[mesh], p => points.Add(p));
                await new ToWorld();
                return points.ToArray();
            }
        }
        finally
        {
            foreach (var action in restore) action();
            world.ForceFullUpdateCycle = previousFull;
        }
        return new { chainCount = hosts.Length, constrainedBoneCount = records.Count,
            initialEnabledChains = enabledChains, liveForceBoundChecks = liveChecks,
            visibilitySource = "authored visible renderer references and live slot activity",
            rendererLoadingGatesPending = sourceMeshes.Count(m => !m.Enabled),
            renderedBoneLimits = true, renderedMeshMotion = renderedMotion,
            collisionAndGrabbingUseSimulationProxy = true, bones = records };
    }

    public static Task VerifyFixtures(World world, string reportPath) => world.Coroutines.StartTask(async () =>
    {
        await new ToWorld();
        var scene = world.RootSlot.AddSlot("BEP PhysBone limit verification");
        var previous = world.ForceFullUpdateCycle;
        world.ForceFullUpdateCycle = true;
        var boundChecks = 0; var exactChecks = 0; var physicsChecks = 0; var grabChecks = 0;
        try
        {
            async Task Settle(int frames = 12) { for (var i = 0; i < frames; i++) await new NextUpdate(); }
            var unit = new float3(1, 1, 1);
            var rest = floatQ.Euler(17, -21, 12);
            var arbitraryAxis = new float3(1, 2, 3).Normalized;
            var xyz = floatQ.AxisAngle(float3.Forward, 40) * floatQ.AxisAngle(float3.Up, 30)
                * floatQ.AxisAngle(float3.Right, 20);
            var configs = new[] {
                Config(1, 0, 0), Config(1, 5, 45), Config(1, 30, 45), Config(1, 180, 90),
                Config(2, 0, 0), Config(2, 20, 45), Config(2, 180, 90),
                Config(3, 30, 10), Config(3, 180, 90),
                new BepPhysBoneLimits.Settings(1, 10, 45, arbitraryAxis,
                    floatQ.FromToRotation(float3.Up, arbitraryAxis) * xyz, rest, unit),
                Config(3, 30, 10) with { Scale = new float3(2, .5f, 1.5f) },
            };
            for (var c = 0; c < configs.Length; c++)
            {
                var config = configs[c];
                var group = scene.AddSlot("Math case " + c);
                var enabled = group.AttachComponent<ValueField<bool>>().Value; enabled.Value = true;
                var source = group.AddSlot("Raw rotation").AttachComponent<ValueField<floatQ>>().Value;
                source.Value = config.Rest;
                var endpoint = group.AddSlot("Raw endpoint").AttachComponent<ValueField<float3>>().Value;
                var output = group.AddSlot("Rendered bone");
                BepPhysBoneLimitGraph.Build(group.AddSlot("Graph"), enabled, source,
                    new IField<float3>[] { endpoint }, output.Rotation_Field, config);
                foreach (var direction in Directions())
                {
                    endpoint.Value = direction;
                    await Settle();
                    var actual = config.Rest.Inverted * output.LocalRotation * config.Axis;
                    CheckBounds(config, actual, "native graph case " + c); boundChecks++;
                }
                enabled.Value = false; await Settle();
                if (MathX.Angle(output.LocalRotation, config.Rest) > .05f)
                    throw new InvalidOperationException("Disabled PhysBone does not restore authored pose.");
                boundChecks++;
                group.Destroy();
            }

            // Independent expected vectors, computed from the documented frame
            // definitions outside the graph implementation (including XYZ order).
            await Exact(configs[9], new float3(-.61885275f, .30942637f, .72199487f),
                new float3(-.43270937f, .32862205f, .83950590f));
            await Exact(Config(2, 20, 45), new float3(.42426407f, .56568542f, .70710678f),
                new float3(0, .93969262f, .34202014f));
            await Exact(Config(3, 30, 10), new float3(-.64278761f, .38302222f, .66341395f),
                new float3(-.17364818f, .85286853f, .49240388f));

            // Actual native DynamicBoneChain under a large force. The source
            // must visibly exceed the limit, while the rendered chain stays in it.
            var baseSlot = scene.AddSlot("Moving parent");
            var bone = baseSlot.AddSlot("Root");
            var tip = bone.AddSlot("Tip"); tip.LocalPosition = new float3(0, .3f, 0);
            var chain = scene.AddSlot("Physics").AttachComponent<DynamicBoneChain>();
            var spec = new p.DynamicBoneNode { MaxAngleX = 10, MaxAngleZ = 45,
                LimitRotation = new p.Quaternion { W = 1 }, Radius = .01f };
            var map = BepPhysBoneLimits.Setup(chain, bone, new[] { (bone, spec), (tip, spec.Clone()) }, 1, "Average");
            foreach (var pair in map.OrderBy(kv => kv.Key.HierachyDepth)) chain.Bones.Add().Assign(pair.Value);
            chain.BaseBoneRadius.Value = .01f; chain.DynamicPlayerCollision.Value = false;
            chain.Stiffness.Value = 0; chain.Elasticity.Value = 10; chain.Damping.Value = 30;
            chain.Gravity.Value = float3.Zero; chain.LocalForce.Value = new float3(30, 0, 0);
            chain.IsGrabbable.Value = true; chain.GrabTerminalBones.Value = true;
            chain.GrabReleaseDistance.Value = 10; chain.GrabSlipping.Value = false;
            var observedLargeMotion = false;
            for (var i = 0; i < 12; i++)
            {
                baseSlot.LocalRotation = floatQ.Euler(0, i * 13, 0);
                await Settle(15);
                CheckBounds(Config(1, 10, 45) with { Rest = floatQ.Identity }, bone.LocalRotation * float3.Up, "forced chain");
                var raw = map[bone].LocalRotation * map[tip].LocalPosition;
                observedLargeMotion |= MathX.Angle(float3.Up, raw) > 20;
                physicsChecks++;
            }
            if (!observedLargeMotion) throw new InvalidOperationException("Physics fixture never exercised an angular violation.");
            var hold = scene.AddSlot("Physical hand"); hold.GlobalPosition = map[tip].GlobalPosition;
            var grabber = hold.AttachComponent<Grabber>();
            if (chain.Grab(grabber, hold) != chain) throw new InvalidOperationException("Limited PhysBone cannot be grabbed at its simulation bone.");
            for (var i = 0; i < 4; i++)
            {
                hold.GlobalPosition += new float3(.07f, .03f, .02f);
                await Settle(15);
                if (!chain.IsGrabbed) throw new InvalidOperationException("Limited PhysBone unexpectedly released its grab.");
                CheckBounds(Config(1, 10, 45) with { Rest = floatQ.Identity }, bone.LocalRotation * float3.Up, "grabbed chain");
                grabChecks++;
            }
            chain.Release(grabber); await Settle();
            if (chain.IsGrabbed) throw new InvalidOperationException("Limited PhysBone does not release.");
            grabChecks++;
            var outputReport = new { passed = true, boundChecks, exactChecks, physicsChecks, grabChecks,
                collisionAndGrabbingUseSimulationProxy = true,
                physicalVrDeviceTested = false };
            File.WriteAllText(reportPath, JsonSerializer.Serialize(outputReport, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("BEP-PHYSBONE-VERIFY " + JsonSerializer.Serialize(outputReport));

            BepPhysBoneLimits.Settings Config(int type, float x, float z) =>
                new(type, x, z, float3.Up, floatQ.Identity, rest, unit);
            async Task Exact(BepPhysBoneLimits.Settings config, float3 input, float3 expected)
            {
                var group = scene.AddSlot("Independent fixture");
                var enabled = group.AttachComponent<ValueField<bool>>().Value; enabled.Value = true;
                var q = group.AddSlot("q").AttachComponent<ValueField<floatQ>>().Value; q.Value = config.Rest;
                var v = group.AddSlot("v").AttachComponent<ValueField<float3>>().Value; v.Value = input;
                var target = group.AddSlot("output");
                BepPhysBoneLimitGraph.Build(group.AddSlot("graph"), enabled, q, new IField<float3>[] { v }, target.Rotation_Field, config);
                await Settle(20);
                var actual = config.Rest.Inverted * target.LocalRotation * config.Axis;
                if ((actual - expected).Magnitude > .0005f)
                    throw new InvalidOperationException($"Independent limit vector differs: {actual} expected {expected}.");
                exactChecks++; group.Destroy();
            }
        }
        finally { scene.Destroy(); world.ForceFullUpdateCycle = previous; }
    });

    private static IEnumerable<float3> Directions()
    {
        yield return float3.Up; yield return -float3.Up; yield return float3.Right; yield return -float3.Right;
        yield return float3.Forward; yield return -float3.Forward;
        for (var i = 0; i < 24; i++)
            yield return new float3(MathF.Cos(i * 2.399963f), (i - 11.5f) / 12f, MathF.Sin(i * 2.399963f)).Normalized;
    }

    private static void CheckBounds(BepPhysBoneLimits.Settings config, float3 direction, string label)
    {
        var d = config.Frame.Inverted * direction.Normalized;
        var pitch = MathF.Atan2(d.z, d.y) * 180 / MathF.PI;
        var yaw = MathF.Atan2(d.x, MathF.Sqrt(d.y * d.y + d.z * d.z)) * 180 / MathF.PI;
        var cone = MathF.Acos(Math.Clamp(d.y, -1, 1)) * 180 / MathF.PI;
        if (!float.IsFinite(cone) || (config.Type == 1 ? cone > config.MaxAngleX + .1f
                : MathF.Abs(pitch) > config.MaxAngleX + .1f || MathF.Abs(yaw) > (config.Type == 2 ? .1f : config.MaxAngleZ + .1f)))
            throw new InvalidOperationException($"PhysBone angle escaped {label}: cone {cone}, pitch {pitch}, yaw {yaw}.");
    }
}
