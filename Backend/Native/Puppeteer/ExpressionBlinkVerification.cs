using System.Text.Json;
using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using nadena.dev.resonity.remote.puppeteer.rpc;

namespace nadena.dev.resonity.remote.puppeteer;

internal static class ExpressionBlinkVerification
{
    internal static async Task<object> Verify(World world, Slot container, Slot host, ExpressionSettings settings)
    {
        var scene = world.RootSlot.AddSlot("BEP isolated blink conflict verification");
        var restore = new List<Action>();
        var checks = 0;
        var guards = container.GetAllChildren().Where(s => s.Name == ExpressionBlink.GuardName).ToArray();
        try
        {
            checks += VerifyGeometry();
            checks += await VerifyNativeGate(scene);
            var menu = host.FindChild("Menu Selection").GetComponent<ValueField<int>>().Value;
            var oldMenu = menu.Value;
            restore.Add(() => menu.Value = oldMenu);
            var hand = host.FindChild("Hand Rule Selection").GetComponent<ValueField<int>>().Value;
            var handDrive = hand.ActiveLink as FieldDrive<int>;
            if (handDrive != null)
            {
                handDrive.ForceLink(scene.AddSlot("Original hand rule sink").AttachComponent<ValueField<int>>().Value);
                restore.Add(() => handDrive.ForceLink(hand));
            }
            var channels = host.Children.Where(s => s.Name.StartsWith("Channel ")).ToArray();
            var hands = settings.handEnabled ? settings.handRules : new List<HandExpressionRule>();
            var menus = settings.menuEnabled ? settings.menuEntries : new List<MenuExpressionEntry>();
            foreach (var guard in guards)
            {
                var conflicts = JsonSerializer.Deserialize<int[]>(guard.GetComponent<ValueField<string>>().Value.Value)!;
                if (conflicts.Length == 0 || conflicts.Any(i => i < 0 || i >= settings.targets.Count))
                    throw new InvalidOperationException("Blink gate has invalid conflict targets.");
                var input = guard.FindChild("Raw Blink Input").GetComponent<ValueDriver<float>>();
                var oldSource = input.ValueSource.Target;
                restore.Add(() => input.ValueSource.Target = oldSource);
                var raw = scene.AddSlot("Injected raw blink").AttachComponent<ValueField<float>>().Value;
                input.ValueSource.Target = raw;
                var output = guard.Parent.GetComponent<ValueDriver<float>>();
                var suppressed = guard.FindChild("Suppressed").GetComponent<ValueField<bool>>().Value;
                if (output.ValueSource.Target != guard.FindChild("Gated Blink Output").GetComponent<ValueField<float>>().Value)
                    throw new InvalidOperationException("The actual blink driver bypasses expression suppression.");
                for (var menuIndex = -1; menuIndex <= menus.Count; menuIndex++)
                for (var handIndex = -1; handIndex < hands.Count; handIndex++)
                {
                    menu.Value = menuIndex; hand.Value = handIndex;
                    var selected = menuIndex > 0 ? menus[menuIndex - 1].values
                        : menuIndex < 0 && handIndex >= 0 ? hands[handIndex].values : new List<ExpressionValue>();
                    var values = selected.ToDictionary(v => v.target, v => v.value);
                    var expectedSuppressed = conflicts.Any(i => MathF.Abs(values.GetValueOrDefault(i, settings.targets[i].baseline)) > .0001f);
                    foreach (var sample in new[] { 0f, .4f, 1f })
                    {
                        raw.Value = sample; await Settle();
                        var expectedBlink = expectedSuppressed ? 0f : sample;
                        if (suppressed.Value != expectedSuppressed || MathF.Abs(output.DriveTarget.Target.Value - expectedBlink) > .0001f)
                            throw new InvalidOperationException($"Blink conflict menu={menuIndex}, hand={handIndex}, raw={sample}: suppressed={suppressed.Value}, output={output.DriveTarget.Target.Value}, expected={expectedBlink}.");
                        for (var i = 0; i < channels.Length; i++)
                        {
                            if (channels[i].FindChild("Automatic Blink Channel") == null
                                || channels[i].FindChild("Live Base").GetComponent<ValueField<float>>().Value != output.DriveTarget.Target) continue;
                            var expectedMesh = values.TryGetValue(i, out var explicitBlink)
                                && !ExpressionBlink.IsNeutralBlink(settings.targets[i].baseline, explicitBlink) ? explicitBlink : expectedBlink;
                            var meshWeight = channels[i].GetComponent<ReferenceField<IField<float>>>().Reference.Target.Value;
                            if (MathF.Abs(meshWeight - expectedMesh) > .0001f)
                                throw new InvalidOperationException("Explicit neutral blink key blocks live blink, or a fixed blink expression is lost.");
                        }
                        foreach (var index in conflicts)
                        {
                            var target = channels[index].GetComponent<ReferenceField<IField<float>>>().Reference.Target;
                            var fallback = channels[index].FindChild("Live Base").GetComponent<ValueField<float>>().Value.Value;
                            if (MathF.Abs(target.Value - values.GetValueOrDefault(index, fallback)) > .0001f)
                                throw new InvalidOperationException("Blink suppression changed the selected eyelid expression itself.");
                        }
                        checks++;
                    }
                }
                // A changing source while suppressed must be read anew on release,
                // rather than restoring the blink value saved when the face changed.
                menu.Value = 0; hand.Value = -1; raw.Value = .23f; await Settle();
                var baselineClosed = conflicts.Any(i => MathF.Abs(settings.targets[i].baseline) > .0001f);
                if (MathF.Abs(output.DriveTarget.Target.Value - (baselineClosed ? 0f : .23f)) > .0001f)
                    throw new InvalidOperationException("Clearing an expression does not restore the current blink input.");
                checks++;
            }
            return new { verified = true, guards = guards.Length, checks, geometryBased = true,
                differentBlendShapeConflicts = true, menuPriority = true, liveBlinkRestored = true };
        }
        finally
        {
            foreach (var action in restore.AsEnumerable().Reverse()) action();
            scene.Destroy();
            await Settle();
        }
    }

    private static int VerifyGeometry()
    {
        var mesh = new MeshX(); mesh.SetVertexCount(4);
        var blink = mesh.AddBlendShape("arbitrary_a");
        blink.AddFrame(1).SetPositionDelta(0, new float3(0, -.02f, 0));
        var closed = mesh.AddBlendShape("arbitrary_b");
        closed.AddFrame(1).SetPositionDelta(0, new float3(0, -.01f, 0));
        var mouth = mesh.AddBlendShape("eye_close_but_actually_mouth");
        mouth.AddFrame(1).SetPositionDelta(3, new float3(0, .01f, 0));
        // Small exported noise on a lid vertex must not turn a mouth shape into
        // a blink blocker. Names deliberately disagree with the real geometry.
        mouth[0].SetPositionDelta(0, new float3(0, 1e-8f, 0));
        var normals = mesh.AddBlendShape("normal_only");
        normals.AddFrame(1).SetNormalDelta(0, new float3(0, 1, 0));
        var multiFrame = mesh.AddBlendShape("multi_frame");
        multiFrame.AddFrame(.5f).SetPositionDelta(3, new float3(0, .01f, 0));
        multiFrame.AddFrame(1).SetPositionDelta(0, new float3(0, -.01f, 0));
        if (!ExpressionBlink.SharesMovedVertices(blink, closed)
            || ExpressionBlink.SharesMovedVertices(blink, mouth)
            || ExpressionBlink.SharesMovedVertices(blink, normals)
            || !ExpressionBlink.SharesMovedVertices(blink, multiFrame))
            throw new InvalidOperationException("Blink conflict geometry detection is incorrect.");
        var otherMesh = new MeshX(); otherMesh.SetVertexCount(4);
        var otherShape = otherMesh.AddBlendShape("arbitrary_b");
        otherShape.AddFrame(1).SetPositionDelta(0, new float3(0, -.01f, 0));
        if (ExpressionBlink.SharesMovedVertices(blink, otherShape))
            throw new InvalidOperationException("Blink geometry detector confuses different renderers.");
        return 5;
    }

    private static async Task<int> VerifyNativeGate(Slot scene)
    {
        var fixture = scene.AddSlot("Independent native blink gate fixture");
        var graph = new ExpressionGraph(fixture.AddSlot("Logic"));
        var raw = fixture.AddSlot("Raw").AttachComponent<ValueField<float>>().Value;
        var blink = fixture.AddSlot("Blink").AttachComponent<ValueField<float>>().Value;
        var driver = fixture.AddSlot("Native driver").AttachComponent<ValueDriver<float>>();
        driver.ValueSource.Target = raw; driver.DriveTarget.ForceLink(blink);
        var hand = fixture.AddSlot("Hand").AttachComponent<ValueField<int>>().Value;
        var menu = fixture.AddSlot("Menu").AttachComponent<ValueField<int>>().Value;
        var settings = new ExpressionSettings
        {
            targets = new() { new() { blendShape = "alternate_lid" }, new() { blendShape = "mouth" } },
            handRules = new()
            {
                new() { values = new() { new() { target = 0, value = .8f } } },
                new() { values = new() { new() { target = 1, value = 1f } } },
                new() { values = new() { new() { target = 0, value = 0f } } }
            },
            menuEntries = new()
            {
                new() { values = new() { new() { target = 1, value = 1f } } },
                new() { values = new() { new() { target = 0, value = 1f } } },
                new() { values = new() { new() { target = 0, value = 0f } } }
            }
        };
        ExpressionBlink.BuildGate(driver, graph, settings, new[] { 0 }, settings.handRules,
            settings.menuEntries, graph.Read(hand), graph.Read(menu));
        var checks = 0;
        foreach (var state in new[] { (-1, -1, false), (-1, 0, true), (-1, 1, false),
                     (1, 0, false), (2, 1, true), (0, 0, false), (-1, 2, false), (3, 0, false) })
        {
            menu.Value = state.Item1; hand.Value = state.Item2;
            foreach (var sample in new[] { 0f, .63f, 1f, .27f })
            {
                raw.Value = sample; await Settle();
                if (MathF.Abs(blink.Value - (state.Item3 ? 0f : sample)) > .0001f)
                    throw new InvalidOperationException($"Native blink gate fixture failed menu={state.Item1}, hand={state.Item2}, raw={sample}.");
                checks++;
            }
        }
        return checks;
    }

    private static async Task Settle() { for (var i = 0; i < 20; i++) await new NextUpdate(); }
}
