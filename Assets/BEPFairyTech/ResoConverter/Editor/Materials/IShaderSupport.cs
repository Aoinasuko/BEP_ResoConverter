// Adapted from Modular Avatar Resonite. Copyright (c) 2025 bd_. MIT license; see COPYING.md.
#nullable enable
using System;
using UnityEngine;
using p = nadena.dev.ndmf.proto;

namespace BEPFairyTech.ResoConverter
{
    internal delegate bool TextureAssetImporter(Texture? tex, Texture? importReference,
        out p.AssetID? assetID, out p.Texture? translatedTex);

    internal interface IShaderTranslator : IDisposable
    {
        bool TryTranslateMaterial(Material material, out p.Material? outMaterial);
    }
}
