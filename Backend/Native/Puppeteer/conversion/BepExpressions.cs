using System.Text.Json;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.CommonAvatar;
using FrooxEngine.ProtoFlux;
using Renderite.Shared;
using PoseSourceNode = FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Avatar.UserFingerPoseSource;
using p = nadena.dev.ndmf.proto;

namespace nadena.dev.resonity.remote.puppeteer.rpc;

public partial class RootConverter
{
    private async Task SetupBepExpressions(JsonElement json)
    {
        if (!_options.asAvatar || json.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return;
        var settings = json.Deserialize<ExpressionSettings>() ?? new ExpressionSettings();
        var handRules = settings.handEnabled ? settings.handRules : new List<HandExpressionRule>();
        var menuEntries = settings.menuEnabled ? settings.menuEntries : new List<MenuExpressionEntry>();
        if (settings.targets.Count == 0 || (handRules.Count == 0 && menuEntries.Count == 0)) return;
        foreach (var values in handRules.Select(r => r.values).Concat(menuEntries.Select(e => e.values)))
        {
            if (values.Any(v => v.target < 0 || v.target >= settings.targets.Count || !float.IsFinite(v.value)))
                throw new InvalidOperationException("An expression contains an invalid BlendShape target or value.");
            if (values.Select(v => v.target).Distinct().Count() != values.Count)
                throw new InvalidOperationException("An expression contains the same BlendShape more than once.");
        }
        foreach (var rule in handRules) { ExpressionGestures.Parse(rule.left); ExpressionGestures.Parse(rule.right); }

        var host = _root.AddSlot("[BEP] Expressions");
        host.AddSlot("Configuration").AttachComponent<ValueField<string>>().Value.Value = json.GetRawText();
        var graph = new ExpressionGraph(host.AddSlot("Logic"));
        var menu = host.AddSlot("Menu Selection").AttachComponent<ValueField<int>>();
        menu.Value.Value = handRules.Count > 0 ? -1 : 0;
        var menuSelection = graph.Read(menu.Value);
        INodeValueOutput<int> handSelection = graph.Constant(-1);
        if (handRules.Count > 0)
        {
            var owner = host.AddSlot("Avatar User").AttachComponent<ReferenceField<User>>();
            host.AttachComponent<AvatarUserReferenceAssigner>().References.Add(owner.Reference);
            var wearer = graph.ReadObject<User>(owner.Reference);
            var source = graph.Node<PoseSourceNode>("Wearer Finger Pose Source");
            source.User.Target = wearer;
            var sourceField = host.AddSlot("Wearer Finger Source").AttachComponent<ReferenceField<IFingerPoseSourceComponent>>();
            graph.DriveReference(sourceField.Reference, source);
            var sourceValue = graph.ReadObject<IFingerPoseSourceComponent>(sourceField.Reference);
            var controllers = new Dictionary<Chirality, ControllerExpressionState>();
            INodeValueOutput<int> Hand(Chirality side)
            {
                var tracking = ExpressionTracking.Build(graph, host, sourceValue, side);
                var gesture = ExpressionGestures.Build(graph, host, sourceValue, side, tracking);
                if (settings.controllerGestures)
                {
                    var controller = ExpressionControllers.Build(graph, host, wearer, side);
                    controllers[side] = controller;
                    gesture = graph.Choose(controller.Active, controller.Gesture, gesture);
                }
                var output = host.AddSlot(side + " Gesture").AttachComponent<ValueField<int>>();
                graph.Drive(output.Value, graph.Choose(graph.NotNull(wearer), gesture, graph.Constant(-1)));
                return graph.Read(output.Value);
            }
            var left = Hand(Chirality.Left);
            var right = Hand(Chirality.Right);
            if (settings.controllerGestures) ExpressionHandPoses.Build(_root, host, graph, wearer, controllers);
            for (var i = handRules.Count - 1; i >= 0; i--)
            {
                var rule = handRules[i];
                var leftPose = ExpressionGestures.Parse(rule.left);
                var rightPose = ExpressionGestures.Parse(rule.right);
                var condition = graph.All(graph.Not(graph.Less(left, 0)), graph.Not(graph.Less(right, 0)),
                    leftPose < 0 ? graph.Constant(true) : graph.Equal(left, leftPose),
                    rightPose < 0 ? graph.Constant(true) : graph.Equal(right, rightPose));
                handSelection = graph.Choose(condition, graph.Constant(i), handSelection);
            }
        }
        var handState = host.AddSlot("Hand Rule Selection").AttachComponent<ValueField<int>>();
        graph.Drive(handState.Value, handSelection);
        handSelection = graph.Read(handState.Value);

        var resolvedTargets = new List<(SkinnedMeshRenderer renderer, Elements.Assets.BlendShape shape)>();
        for (var targetIndex = 0; targetIndex < settings.targets.Count; targetIndex++)
        {
            var spec = settings.targets[targetIndex];
            if (!float.IsFinite(spec.baseline)) throw new InvalidOperationException("Invalid expression baseline.");
            var renderer = Object<SkinnedMeshRenderer>(new p.ObjectID { Id = spec.rendererId });
            if (renderer == null) throw new InvalidOperationException("Expression renderer is missing: " + spec.rendererId);
            if (await _context.WaitForAssetLoad(renderer.Mesh.Target) == null)
                throw new InvalidOperationException("Expression mesh did not load: " + renderer.Slot.Name);
            await new ToWorld();
            var shapes = renderer.Mesh.Asset.Data.BlendShapes.ToList();
            var shapeIndex = shapes.FindIndex(s => s.Name == spec.blendShape);
            if (shapeIndex < 0 || shapeIndex >= renderer.BlendShapeWeights.Count)
                throw new InvalidOperationException("Expression BlendShape is missing: " + spec.blendShape);
            resolvedTargets.Add((renderer, shapes[shapeIndex]));
            var weight = renderer.BlendShapeWeights.GetElement(shapeIndex);
            var automaticBlink = _root.GetComponentsInChildren<ValueDriver<float>>()
                .Any(d => d.Slot.Name == "BEP Selected Blink" && d.DriveTarget.Target == weight);
            var channel = host.AddSlot("Channel " + targetIndex + " " + spec.blendShape);
            channel.AttachComponent<ReferenceField<IField<float>>>().Reference.Target = weight;
            if (automaticBlink) channel.AddSlot("Automatic Blink Channel").AttachComponent<ValueField<bool>>().Value.Value = true;
            var live = channel.AddSlot("Live Base").AttachComponent<ValueField<float>>();
            live.Value.Value = spec.baseline;
            if (weight.ActiveLink is FieldDrive<float> previous)
                previous.ForceLink(live.Value);
            else if (weight.IsLinked)
                throw new InvalidOperationException("Expression BlendShape has an unsupported existing link: " + spec.blendShape);

            var fallback = graph.Read(live.Value);
            INodeValueOutput<float> handValue = fallback, menuValue = fallback;
            for (var i = handRules.Count - 1; i >= 0; i--)
            {
                var value = handRules[i].values.FirstOrDefault(v => v.target == targetIndex);
                if (value != null) handValue = graph.Choose(graph.Equal(handSelection, i),
                    automaticBlink && ExpressionBlink.IsNeutralBlink(spec.baseline, value.value) ? fallback : graph.Constant(value.value), handValue);
            }
            for (var i = menuEntries.Count - 1; i >= 0; i--)
            {
                var value = menuEntries[i].values.FirstOrDefault(v => v.target == targetIndex);
                if (value != null) menuValue = graph.Choose(graph.Equal(menuSelection, i + 1),
                    automaticBlink && ExpressionBlink.IsNeutralBlink(spec.baseline, value.value) ? fallback : graph.Constant(value.value), menuValue);
            }
            graph.Drive(weight, graph.Choose(graph.Less(menuSelection, 0), handValue, menuValue));
        }
        ExpressionBlink.Build(_root, graph, settings, resolvedTargets, handRules, menuEntries, handSelection, menuSelection);
        if (menuEntries.Count > 0) BuildExpressionMenu(host, menu.Value, menuEntries, handRules.Count > 0);
        await new NextUpdate();
    }

    private static void BuildExpressionMenu(Slot host, IField<int> selection, List<MenuExpressionEntry> entries, bool hands)
    {
        var menuSlot = host.AddSlot("Expression Menu");
        var source = menuSlot.AttachComponent<ContextMenuItemSource>();
        source.Label.Value = "表情";
        menuSlot.AttachComponent<RootContextMenuItem>().Item.Target = source;
        var items = menuSlot.AddSlot("Entries");
        menuSlot.AttachComponent<ContextMenuSubmenu>().ItemsRoot.Target = items;
        void Item(string name, int value)
        {
            var slot = items.AddSlot(name);
            var item = slot.AttachComponent<ContextMenuItemSource>();
            item.Label.Value = name;
            item.CloseMenuOnPress.Value = true;
            var set = slot.AttachComponent<ButtonValueSet<int>>();
            set.TargetValue.Target = selection;
            set.SetValue.Value = value;
        }
        Item("初期状態", 0);
        if (hands) Item("ハンドサインに戻す", -1);
        for (var i = 0; i < entries.Count; i++) Item(entries[i].name, i + 1);
    }
}
