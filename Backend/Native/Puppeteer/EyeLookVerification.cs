using System.Text.Json;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.CommonAvatar;
using nadena.dev.resonity.remote.puppeteer.rpc;

namespace nadena.dev.resonity.remote.puppeteer;

internal static class EyeLookVerification
{
    internal static async Task<object?> Verify(World world, Slot container)
    {
        var host = container.GetAllChildren().FirstOrDefault(s => s.Name == "[BEP] Eye Look");
        if (host == null) return null;
        var settings = JsonSerializer.Deserialize<EyeLookSettings>(host.FindChild("Configuration").GetComponent<ValueField<string>>().Value.Value)!;
        if (container.GetComponentsInChildren<EyeRotationDriver>().Any())
            throw new InvalidOperationException("A VRC-configured avatar retains generic eye rotation drivers.");
        var scene = world.RootSlot.AddSlot("BEP eye-look verification inputs");
        var previousFullUpdate = world.ForceFullUpdateCycle;
        world.ForceFullUpdateCycle = true;
        var checks = 0;
        var headFollowChecks = 0;
        var eyeCount = 0;
        try
        {
            async Task Settle() { for (var i = 0; i < 15; i++) await new NextUpdate(); }
            foreach (var (side, config) in new[] { (EyeSide.Left, settings.left), (EyeSide.Right, settings.right) })
            {
                if (config == null || config.boneId == 0) continue;
                eyeCount++;
                var eyeHost = host.FindChild(side.ToString());
                var bone = eyeHost.GetComponent<ReferenceField<Slot>>().Reference.Target;
                var parent = bone.Parent;
                var originalPosition = bone.LocalPosition;
                if (!settings.enabled)
                {
                    if (bone.Rotation_Field.IsDriven || RotationError(bone.LocalRotation, config.straight.Rotation()) > .1f)
                        throw new InvalidOperationException("Disabled VRC eye look does not preserve the authored eye pose.");
                    checks++;
                    continue;
                }
                var driver = eyeHost.FindChild("Gaze Target").GetComponent<ValueDriver<float3>>();
                var source = driver.ValueSource.Target;
                var manager = container.GetComponentsInChildren<EyeManager>().Single(m =>
                    source == m.LeftEyeTargetPoint || source == m.RightEyeTargetPoint);
                var input = scene.AddSlot(side + " target").AttachComponent<ValueField<float3>>().Value;
                driver.ValueSource.Target = input;
                var reference = manager.Slot;
                var head = reference.Parent;
                var headRotation = head.LocalRotation;
                var headDrive = head.Rotation_Field.ActiveLink as FieldDrive<floatQ>;
                if (head.Rotation_Field.IsLinked && headDrive == null)
                    throw new InvalidOperationException("Cannot isolate head rotation for native eye verification.");
                var sink = scene.AddSlot("Original head drive sink").AttachComponent<ValueField<floatQ>>().Value;
                headDrive?.ForceLink(sink);
                try
                {
                    // Exact directional endpoints, partial gaze, diagonal gaze,
                    // outside-range targets, behind-head targets and zero input.
                    foreach (var (yaw, pitch) in new[]
                    {
                        (0f, 0f), (-30f, 0f), (30f, 0f), (0f, 30f), (0f, -30f),
                        (-15f, 0f), (15f, 0f), (0f, 15f), (0f, -15f),
                        (-30f, 30f), (30f, -30f), (-80f, 70f), (80f, -70f), (170f, 0f)
                    })
                    {
                        var direction = Direction(yaw, pitch);
                        input.Value = reference.LocalPointToGlobal(direction * 3);
                        await Settle();
                        Check(Expected(config, direction), $"{side} gaze {yaw}/{pitch}");
                        checks++;
                    }
                    input.Value = reference.GlobalPosition;
                    await Settle();
                    Check(config.straight.Rotation(), side + " coincident gaze target");
                    checks++;

                    // Keep the *world* target constant, then rotate the head.
                    // This exercises continuous transform evaluation, including
                    // the case where the source target field never changes.
                    input.Value = reference.LocalPointToGlobal(new float3(.25f, .2f, 3));
                    foreach (var rotation in new[] { new float3(0, 35, 0), new float3(-25, -30, 12), new float3(40, 70, -20) })
                    {
                        head.LocalRotation = headRotation * floatQ.Euler(rotation);
                        await Settle();
                        Check(Expected(config, reference.GlobalPointToLocal(input.Value)), side + " head turn " + rotation);
                        headFollowChecks++;
                    }
                }
                finally
                {
                    head.LocalRotation = headRotation;
                    headDrive?.ForceLink(head.Rotation_Field);
                    driver.ValueSource.Target = source;
                    await Settle();
                }

                void Check(floatQ expected, string label)
                {
                    var actual = bone.LocalRotation;
                    if (!float.IsFinite(actual.x + actual.y + actual.z + actual.w)
                        || RotationError(actual, expected) > .12f)
                        throw new InvalidOperationException($"Eye look {label}: expected {expected}, got {actual}.");
                    if (bone.Parent != parent || MathX.Distance(bone.LocalPosition, originalPosition) > .00001f)
                        throw new InvalidOperationException($"Eye look {label} moved the eye origin away from its head parent.");
                }
            }
            return new { verified = true, enabled = settings.enabled, eyes = eyeCount, gazeChecks = checks, headFollowChecks,
                source = "VRC authored local eye rotations; native EyeManager target inputs", hardwareEyeTrackingTested = false };
        }
        finally
        {
            world.ForceFullUpdateCycle = previousFullUpdate;
            scene.Destroy();
        }
    }

    private static float3 Direction(float yaw, float pitch)
    {
        yaw *= MathF.PI / 180; pitch *= MathF.PI / 180;
        return new float3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch));
    }

    private static float RotationError(floatQ actual, floatQ expected)
    {
        // MathX.Slerp's close-angle branch can return a slightly non-unit
        // quaternion. Measure directions after normalization, as Slot does.
        return MathX.Angle(actual.Normalized, expected.Normalized);
    }

    private static floatQ Expected(EyeLookBone config, float3 direction)
    {
        var yaw = MathF.Atan2(direction.x, direction.z) * 180 / MathF.PI;
        var pitch = MathF.Atan2(direction.y, MathF.Sqrt(direction.x * direction.x + direction.z * direction.z)) * 180 / MathF.PI;
        var neutral = config.straight.Rotation();
        var horizontal = MathX.Slerp(neutral, (yaw < 0 ? config.left : config.right).Rotation(), Math.Clamp(MathF.Abs(yaw) / BepEyeLookGraph.InputRangeDegrees, 0, 1));
        var vertical = MathX.Slerp(neutral, (pitch < 0 ? config.down : config.up).Rotation(), Math.Clamp(MathF.Abs(pitch) / BepEyeLookGraph.InputRangeDegrees, 0, 1));
        return vertical * neutral.Inverted * horizontal;
    }
}
