using Elements.Core;
using FrooxEngine;
using FrooxEngine.CommonAvatar;

namespace nadena.dev.resonity.remote.puppeteer;

internal static class ItemGrabVerification
{
    internal record Result(bool Verified, object Details);

    internal static async Task<Result> Inspect(World world, Slot container, MeshRenderer[] visibleRenderers)
    {
        await new ToWorld();
        var grabbable = container.GetComponentsInChildren<Grabbable>().SingleOrDefault();
        if (grabbable == null) throw new InvalidOperationException("Item must contain one root Grabbable.");
        var root = grabbable.Slot;
        var bounds = await BepBounds.Compute(visibleRenderers, root);
        await new ToWorld();
        var colliders = root.GetComponents<BoxCollider>().ToArray();
        var grabberSlot = world.RootSlot.AddSlot("BEP item grab verification hand");
        try
        {
            var grabber = grabberSlot.AttachComponent<Grabber>();
            var center = root.LocalPointToGlobal(bounds.Center);
            var distance = MathX.Max(root.LocalScale.Magnitude * bounds.Size.Magnitude, 0.1f) + 1f;
            var rayHits = new List<RaycastHit>();
            var rayTests = new[] { root.Forward, -root.Forward, root.Right, -root.Right, root.Up, -root.Up }
                .Select(direction =>
                {
                    var origin = center - direction * distance;
                    var hits = world.Physics.RaycastAll(origin, direction, distance * 2f,
                        c => colliders.Contains(c), false);
                    rayHits.AddRange(hits);
                    return hits.Count;
                }).ToArray();
            var colliderReports = colliders.Select(c => new
            {
                size = Vec(c.Size.Value), offset = Vec(c.Offset.Value),
                enabled = c.Enabled, slotActive = c.Slot.IsActive,
                type = c.Type.Value.ToString(), ignoreRaycasts = c.IgnoreRaycasts.Value,
                sameSlotAsGrabbable = c.Slot == root,
                grabbableFound = grabber.FindGrabbableInParents(c.Slot, null) == grabbable,
                physicsRegistered = c.ToString().Contains("BepuAllocated: True"),
                coversVisibleMeshes = Covers(c, bounds),
            }).ToArray();
            var canGrab = grabbable.CanGrab(grabber);
            var originalParent = root.Parent;
            var held = grabber.Grab(rayHits.Select(h => h.Collider).Distinct().ToList(),
                g => g == grabbable, includeDynamic: false, singleItem: true);
            var reparented = held && root.Parent == grabber.HolderSlot && grabbable.Grabber == grabber;
            if (held) grabber.Release(grabbable, true);
            root.SetParent(originalParent);
            // Exercise the same sphere query used by a physical hand, as well as
            // the laser's collider-to-parent-Grabbable path above.
            for (var i = 0; i < 2; i++) await new NextUpdate();
            var handOverlaps = world.Physics.SphereOverlap(center, 0.01f, c => colliders.Contains(c), false);
            var handGrabbed = grabber.Grab(handOverlaps, g => g == grabbable, includeDynamic: false, singleItem: true);
            var handReparented = handGrabbed && root.Parent == grabber.HolderSlot;
            if (handGrabbed) grabber.Release(grabbable, true);
            root.SetParent(originalParent);
            var sourceMeshesUnderGrabRoot = visibleRenderers.All(r => r.Slot.IsChildOf(root) || r.Slot == root);
            var verified = canGrab && reparented && handReparented && sourceMeshesUnderGrabRoot
                && rayTests.All(count => count > 0)
                && colliderReports.Length > 0 && colliderReports.All(c => c.enabled && c.slotActive
                    && !c.ignoreRaycasts && c.sameSlotAsGrabbable && c.grabbableFound
                    && c.physicsRegistered && c.coversVisibleMeshes);
            return new Result(verified, new
            {
                verified,
                root = root.Name, rootScale = Vec(root.LocalScale),
                rendererSize = Vec(bounds.Size), rendererCenter = Vec(bounds.Center),
                colliders = colliderReports, rayHits = rayHits.Count, rayTests,
                canGrab, actualGrabSucceeded = held && reparented,
                handOverlapCount = handOverlaps.Count, physicalGrabSucceeded = handReparented,
                sourceMeshesUnderGrabRoot,
                protections = root.GetComponentsInChildren<SimpleAvatarProtection>().Select(p => new
                {
                    ownerMatchesLocalUser = string.IsNullOrEmpty(p.Cloud.CurrentUserID)
                        ? (bool?)null : p.User.UserId == p.Cloud.CurrentUserID,
                    allowsLocalUser = p.CanUse(world.LocalUser),
                    reassignsOwner = p.ReassignUserOnPackageImport.Value,
                }).ToArray(),
            });
        }
        finally { grabberSlot.Destroy(); }
    }

    private static float[] Vec(float3 value) => new[] { value.x, value.y, value.z };

    private static bool Covers(BoxCollider collider, BoundingBox bounds)
    {
        var size = collider.Size.Value;
        var center = collider.Offset.Value;
        var extent = size * 0.5f - MathX.Abs(center - bounds.Center);
        return float.IsFinite(size.x) && float.IsFinite(size.y) && float.IsFinite(size.z)
            && size.x > 0 && size.y > 0 && size.z > 0
            && extent.x + 0.00001f >= bounds.Size.x * 0.5f
            && extent.y + 0.00001f >= bounds.Size.y * 0.5f
            && extent.z + 0.00001f >= bounds.Size.z * 0.5f;
    }
}
