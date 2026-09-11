using System.Text.Json;
using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.ProtoFlux;

namespace nadena.dev.resonity.remote.puppeteer.rpc;

// A separate eyelid expression (for example a smiling closed-eye shape) is
// additive with the selected blink even though it does not drive the same field.
// Detect that shared geometry once during conversion and serialize native gates.
internal static class ExpressionBlink
{
    internal const string GuardName = "Expression Blink Guard";

    // Unity face clips often key every shape to zero. A neutral key for the
    // automatically blinking channel should leave its live animation available.
    internal static bool IsNeutralBlink(float baseline, float value) => MathF.Abs(baseline) <= .0001f && MathF.Abs(value) <= .0001f;

    internal static void Build(Slot root, ExpressionGraph graph, ExpressionSettings settings,
        IReadOnlyList<(SkinnedMeshRenderer renderer, BlendShape shape)> targets,
        IReadOnlyList<HandExpressionRule> hands, IReadOnlyList<MenuExpressionEntry> menus,
        INodeValueOutput<int> handSelection, INodeValueOutput<int> menuSelection)
    {
        foreach (var driver in root.GetComponentsInChildren<ValueDriver<float>>()
                     .Where(d => d.Slot.Name == "BEP Selected Blink").ToArray())
        {
            var target = driver.DriveTarget.Target;
            var proxy = root.GetComponentsInChildren<ReferenceField<IField<float>>>()
                .FirstOrDefault(r => r.Slot.FindChild("Live Base")?.GetComponent<ValueField<float>>()?.Value == target);
            if (proxy != null) target = proxy.Reference.Target;
            var renderer = driver.Slot.GetComponentInParents<SkinnedMeshRenderer>();
            if (renderer?.Mesh.Asset?.Data == null || driver.ValueSource.Target == null) continue;
            var blinkIndex = Enumerable.Range(0, renderer.BlendShapeWeights.Count)
                .FirstOrDefault(i => renderer.BlendShapeWeights.GetElement(i) == target, -1);
            if (blinkIndex < 0) continue;
            var blink = renderer.Mesh.Asset.Data.BlendShapes.ElementAt(blinkIndex);
            var conflicts = Enumerable.Range(0, targets.Count)
                .Where(i => targets[i].renderer == renderer && targets[i].shape != blink
                    && SharesMovedVertices(blink, targets[i].shape)).ToArray();
            if (conflicts.Length == 0) continue;
            BuildGate(driver, graph, settings, conflicts, hands, menus, handSelection, menuSelection);
        }
    }

    internal static bool SharesMovedVertices(BlendShape blink, BlendShape expression)
    {
        if (blink.Mesh != expression.Mesh) return false;
        // Ignore normal-only changes and tiny export noise. Relative cutoffs keep
        // this test independent of an avatar's authoring scale.
        float Peak(BlendShape shape) => shape.Frames.SelectMany(f => f.RawPositions.Take(shape.Mesh.VertexCount))
            .Select(v => v.SqrMagnitude).DefaultIfEmpty(0).Max();
        var blinkPeak = Peak(blink);
        var expressionPeak = Peak(expression);
        if (blinkPeak == 0 || expressionPeak == 0) return false;
        var blinkCutoff = blinkPeak * .000001f;
        var expressionCutoff = expressionPeak * .000001f;
        for (var i = 0; i < blink.Mesh.VertexCount; i++)
            if (blink.Frames.Any(f => f.RawPositions[i].SqrMagnitude > blinkCutoff)
                && expression.Frames.Any(f => f.RawPositions[i].SqrMagnitude > expressionCutoff)) return true;
        return false;
    }

    internal static void BuildGate(ValueDriver<float> driver, ExpressionGraph graph, ExpressionSettings settings,
        int[] conflicts, IReadOnlyList<HandExpressionRule> hands, IReadOnlyList<MenuExpressionEntry> menus,
        INodeValueOutput<int> handSelection, INodeValueOutput<int> menuSelection)
    {
        bool Suppresses(IEnumerable<ExpressionValue>? selected)
        {
            var values = selected?.ToDictionary(v => v.target, v => v.value);
            return conflicts.Any(i => MathF.Abs(values != null && values.TryGetValue(i, out var value)
                ? value : settings.targets[i].baseline) > .0001f);
        }
        var baseline = graph.Constant(Suppresses(null));
        INodeValueOutput<bool> hand = baseline, menu = baseline;
        for (var i = hands.Count - 1; i >= 0; i--)
            hand = graph.Choose(graph.Equal(handSelection, i), graph.Constant(Suppresses(hands[i].values)), hand);
        for (var i = menus.Count - 1; i >= 0; i--)
            menu = graph.Choose(graph.Equal(menuSelection, i + 1), graph.Constant(Suppresses(menus[i].values)), menu);

        var guard = driver.Slot.AddSlot(GuardName);
        guard.AttachComponent<ValueField<string>>().Value.Value = JsonSerializer.Serialize(conflicts);
        var input = guard.AddSlot("Raw Blink Input").AttachComponent<ValueField<float>>();
        var inputDriver = input.Slot.AttachComponent<ValueDriver<float>>();
        inputDriver.ValueSource.Target = driver.ValueSource.Target;
        inputDriver.DriveTarget.ForceLink(input.Value);
        var suppressed = guard.AddSlot("Suppressed").AttachComponent<ValueField<bool>>();
        graph.Drive(suppressed.Value, graph.Choose(graph.Less(menuSelection, 0), hand, menu));
        var output = guard.AddSlot("Gated Blink Output").AttachComponent<ValueField<float>>();
        graph.Drive(output.Value, graph.Choose(graph.Read(suppressed.Value), graph.Constant(0f), graph.Read(input.Value)));
        driver.ValueSource.Target = output.Value;
    }
}
