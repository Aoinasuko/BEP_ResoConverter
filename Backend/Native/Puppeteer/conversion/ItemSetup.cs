using System.Diagnostics;
using Elements.Core;
using FrooxEngine;

namespace nadena.dev.resonity.remote.puppeteer.rpc;

public partial class RootConverter
{
    private async Task ConfigureItemGrab(MeshRenderer[] visibleRenderers)
    {
        if (visibleRenderers.Length == 0)
            throw new InvalidOperationException("アイテムの掴み判定を作れません。有効な MeshRenderer が必要です。");

        // The native point iterator does not load Mesh.Asset. Item exports have no
        // AvatarCreator to wait for meshes, so bounds must wait explicitly here.
        var timeout = Stopwatch.StartNew();
        while (visibleRenderers.Any(r => r.Mesh.Asset == null) && timeout.Elapsed < TimeSpan.FromSeconds(30))
            await new NextUpdate();
        if (visibleRenderers.Any(r => r.Mesh.Asset == null))
            throw new InvalidOperationException("アイテムのメッシュ読み込みが完了せず、掴み判定を作成できませんでした。");

        var bounds = BoundingBox.Empty();
        var points = 0;
        foreach (var renderer in visibleRenderers)
        {
            await renderer.ForeachExactBoundedPoint(_root, point =>
            {
                if (!IsFinite(point)) throw new InvalidOperationException("アイテムの頂点座標が不正です。");
                bounds.Encapsulate(point);
                points++;
            });
            await new ToWorld();
        }
        if (points == 0 || !IsFinite(bounds.Size) || !IsFinite(bounds.Center))
            throw new InvalidOperationException("アイテムの頂点から掴み判定の範囲を計算できませんでした。");

        var grabbable = _root.AttachComponent<Grabbable>();
        grabbable.Enabled = true;
        grabbable.EditModeOnly.Value = false;
        grabbable.AllowOnlyPhysicalGrab.Value = false;

        var collider = _root.AttachComponent<BoxCollider>();
        collider.Enabled = true;
        collider.Type.Value = ColliderType.Static;
        collider.IgnoreRaycasts.Value = false;
        // float3(singleValue) initializes only X in Elements.Core. Give every axis
        // a minimum thickness so planar meshes remain reachable by laser and hand.
        collider.Size.Value = MathX.Max(bounds.Size, new float3(0.001f, 0.001f, 0.001f));
        collider.Offset.Value = bounds.Center;
    }

    private static bool IsFinite(float3 value) =>
        float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
}
