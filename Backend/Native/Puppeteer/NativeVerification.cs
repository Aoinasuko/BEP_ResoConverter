using System.Text.Json;
using FrooxEngine;
using FrooxEngine.CommonAvatar;

namespace nadena.dev.resonity.remote.puppeteer;

internal static class NativeVerification
{
    public static Task Verify(World world, string packagePath) => world.Coroutines.StartTask(async () =>
    {
        await new ToWorld();
        var container = world.RootSlot.AddSlot("BEP Native Roundtrip Verification");
        try
        {
            await PackageImporter.ImportPackage(packagePath, container);
            await new ToWorld();
            var sourceRenderers = container.GetComponentsInChildren<ReferenceField<MeshRenderer>>()
                .Where(r => r.Slot.Name == "[BEP] Exported Renderers")
                .Select(r => r.Reference.Target).Where(r => r != null).ToHashSet();
            foreach (var renderer in sourceRenderers)
            {
                for (var attempts = 0; renderer.Mesh.Asset == null && attempts < 600; attempts++)
                    await new NextUpdate();
                if (renderer.Mesh.Asset == null) throw new Exception("Roundtrip mesh failed to load: " + renderer.Slot.Name);
            }
            for (var i = 0; i < 15; i++) await new NextUpdate();
            var visibleRenderers = container.GetComponentsInChildren<ReferenceField<MeshRenderer>>()
                .Where(r => r.Slot.Name == "[BEP] Visible Exported Renderers")
                .Select(r => r.Reference.Target).Where(r => r != null).ToArray();
            var bounds = await BepBounds.Compute(visibleRenderers, world.RootSlot);
            await new ToWorld();
            var itemGrab = container.GetComponentsInChildren<AvatarRoot>().Any()
                ? null : await ItemGrabVerification.Inspect(world, container, visibleRenderers);
            var expressions = await ExpressionVerification.Verify(world, container);
            var eyeLook = await EyeLookVerification.Verify(world, container);
            var blinkTargets = new List<string>();
            foreach (var driver in container.GetComponentsInChildren<ValueDriver<float>>().Where(d => d.Slot.Name == "BEP Selected Blink"))
            {
                if (driver.ValueSource.Target == null || driver.DriveTarget.Target == null) throw new Exception("Roundtrip blink driver has missing references");
                var blinkTarget = driver.DriveTarget.Target;
                var proxyChannel = container.GetComponentsInChildren<ReferenceField<IField<float>>>()
                    .FirstOrDefault(r => r.Slot.FindChild("Live Base")?.GetComponent<ValueField<float>>()?.Value == blinkTarget);
                if (proxyChannel != null) blinkTarget = proxyChannel.Reference.Target;
                foreach (var renderer in sourceRenderers.OfType<SkinnedMeshRenderer>())
                    for (var i = 0; i < renderer.BlendShapeWeights.Count; i++)
                        if (renderer.BlendShapeWeights.GetElement(i) == blinkTarget)
                            blinkTargets.Add(renderer.Slot.Name + "/" + renderer.Mesh.Asset.Data.BlendShapes.ElementAt(i).Name);
            }
            var report = new
            {
                roundtripSucceeded = true,
                sourceRendererCount = sourceRenderers.Count,
                sourceMaterialTypes = sourceRenderers.SelectMany(r => r.Materials)
                    .Select(m => m?.GetType().Name).Distinct().OrderBy(n => n).ToArray(),
                meshVertices = sourceRenderers.Sum(r => r.Mesh.Asset.Data.VertexCount),
                heightMeters = bounds.Size.y,
                avatarRootCount = container.GetComponentsInChildren<AvatarRoot>().Count(),
                protectionCount = container.GetComponentsInChildren<SimpleAvatarProtection>().Count(),
                protectionReassignsOwner = container.GetComponentsInChildren<SimpleAvatarProtection>().All(p => p.ReassignUserOnPackageImport.Value),
                dynamicBoneChains = container.GetComponentsInChildren<DynamicBoneChain>().Count(d => d.Bones.Count > 0),
                sourcePhysicsMetadata = container.GetComponentsInChildren<ValueField<string>>().Count(v => v.Slot.Name == "BEP Source PhysBone (reference settings)"),
                blinkTargets,
                automaticBlinkTargets = container.GetComponentsInChildren<EyeLinearDriver>().Sum(d => d.Eyes.Count(e => e.OpenCloseTarget.Target != null)),
                directVisemeDrivers = container.GetComponentsInChildren<DirectVisemeDriver>().Count(d => d.Source.Target != null),
                jawDrivers = container.GetComponentsInChildren<VolumeMeter>().Count(d => d.Slot.Name == "BEP Voice Jaw"),
                grabbableCount = container.GetComponentsInChildren<Grabbable>().Count(),
                rootBoxColliders = container.GetComponentsInChildren<BoxCollider>().Count(),
                itemGrab = itemGrab?.Details,
                expressions,
                eyeLook,
            };
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            await new ToBackground();
            File.WriteAllText(packagePath + ".inspection.json", json);
            Console.WriteLine("BEP-VERIFY " + JsonSerializer.Serialize(report));
            if (itemGrab is { Verified: false })
                throw new InvalidOperationException("The exported item failed native collider/grab verification.");
        }
        finally
        {
            await new ToWorld();
            container.Destroy();
        }
    });
}
