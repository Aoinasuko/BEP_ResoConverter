using Elements.Core;
using FrooxEngine;

namespace nadena.dev.resonity.remote.puppeteer;

internal static class BepBounds
{
    internal static async Task<BoundingBox> Compute(IEnumerable<MeshRenderer> renderers, Slot space)
    {
        var bounds = BoundingBox.Empty();
        foreach (var renderer in renderers)
        {
            await renderer.ForeachExactBoundedPoint(space, point => bounds.Encapsulate(point));
            await new ToWorld();
        }
        return bounds;
    }
}
