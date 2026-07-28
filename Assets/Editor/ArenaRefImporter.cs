using UnityEditor;
using UnityEngine;

/// <summary>
/// Import settings for the third-party reference assets under Fight/Arena/Ref.
///
/// Written as a postprocessor rather than into .meta files because three of the five maps carry
/// data, not colour, and the vendor ships all of them flagged sRGB. A wrong sRGB flag on an
/// opacity mask is invisible until the silhouette comes out fat — a 0.5 threshold decodes to
/// 0.214 — and a wrong one on a roughness map moves its mean from 0.469 to 0.187.
///
/// Nothing here is referenced by a material, a prefab or a scene, so none of it ships in the
/// player: these are the sources the generated maps are baked from, read once per Rebuild Arena.
/// </summary>
internal sealed class ArenaRefImporter : AssetPostprocessor
{
    private const string RefDir = "Assets/Fight/Arena/Ref/";

    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(RefDir))
            return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        // Only the base-colour sheets are colour. Normal, roughness and opacity are data.
        importer.sRGBTexture = assetPath.EndsWith("_BC.png");
        importer.isReadable = true;      // every one of these is read from the CPU at bake time
        importer.mipmapEnabled = false;  // sampled once per texel by hand, never by the GPU
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
    }

    private void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(RefDir))
            return;
        var importer = (ModelImporter)assetImporter;
        // The pack's own materials are HDRP — HDLitMasterNode, _DiffusionProfileReferences — and
        // would arrive referencing a shader this project does not have.
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.animationType = ModelImporterAnimationType.None;
        importer.importAnimation = false;
    }
}
