using Elements.Core;
using FrooxEngine;
using p = nadena.dev.ndmf.proto;

namespace nadena.dev.resonity.remote.puppeteer.rpc;

public partial class RootConverter
{
    private static BlendMode BepBlend(p.BlendMode mode) => mode == p.BlendMode.Fade ? BlendMode.Transparent : Enum.TryParse<BlendMode>(mode.ToString(), out var result) ? result : BlendMode.Opaque;

    private IAssetProvider<Material> CreateBepPbrMaterial(Slot slot, p.Material src)
    {
        var material = slot.AttachComponent<PBS_Metallic>();
        material.AlbedoColor.Value = src.MainColor?.ColorX() ?? colorX.White;
        material.EmissiveColor.Value = src.EmissionColor?.ColorX() ?? colorX.Clear;
        material.Metallic.Value = src.HasMetallic ? src.Metallic : 0;
        material.Smoothness.Value = src.HasSmoothness ? src.Smoothness : 0;
        material.BlendMode.Value = BepBlend(src.BlendMode);
        material.AlphaCutoff.Value = src.HasAlphaClip ? src.AlphaClip : .5f;
        if (src.HasUnityRenderQueue) material.RenderQueue.Value = src.UnityRenderQueue;
        if (src.MainTextureScaleOffset != null)
        {
            material.TextureScale.Value = src.MainTextureScaleOffset.Scale.Vec2();
            material.TextureOffset.Value = src.MainTextureScaleOffset.Offset.Vec2();
        }
        Defer(PHASE_RESOLVE_REFERENCES, "Setting PBR textures", () =>
        {
            material.AlbedoTexture.Value = AssetRefID<IAssetProvider<Texture2D>>(src.MainTexture);
            material.NormalMap.Value = AssetRefID<IAssetProvider<Texture2D>>(src.NormalMap);
            material.EmissiveMap.Value = AssetRefID<IAssetProvider<Texture2D>>(src.EmissionMap);
            material.MetallicMap.Value = AssetRefID<IAssetProvider<Texture2D>>(src.SmoothnessMetallicReflectionMap);
        });
        return material;
    }

    private IAssetProvider<Material> CreateBepUnlitMaterial(Slot slot, p.Material src)
    {
        var material = slot.AttachComponent<UnlitMaterial>();
        material.TintColor.Value = src.MainColor?.ColorX() ?? colorX.White;
        material.BlendMode.Value = BepBlend(src.BlendMode);
        material.AlphaCutoff.Value = src.HasAlphaClip ? src.AlphaClip : .5f;
        material.Sidedness.Value = src.CullMode == p.CullMode.None ? Sidedness.Double : src.CullMode == p.CullMode.Front ? Sidedness.Back : Sidedness.Front;
        if (src.HasZWrite) material.ZWrite.Value = src.ZWrite ? ZWrite.On : ZWrite.Off;
        if (src.HasUnityRenderQueue) material.RenderQueue.Value = src.UnityRenderQueue;
        if (src.MainTextureScaleOffset != null)
        {
            material.TextureScale.Value = src.MainTextureScaleOffset.Scale.Vec2();
            material.TextureOffset.Value = src.MainTextureScaleOffset.Offset.Vec2();
        }
        Defer(PHASE_RESOLVE_REFERENCES, "Setting unlit texture", () => material.Texture.Value = AssetRefID<IAssetProvider<Texture2D>>(src.MainTexture));
        return material;
    }
}
