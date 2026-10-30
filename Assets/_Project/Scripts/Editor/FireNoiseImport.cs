using UnityEditor;

namespace ArcaneStrike.EditorTools
{
    /// <summary>The fire shader's noise is data, not a picture: no colour conversion, no compression artefacts, it tiles.</summary>
    class FireNoiseImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.EndsWith("FX/T_FireNoise.png")) return;
            var importer = (TextureImporter)assetImporter;
            importer.sRGBTexture = false;
            importer.wrapMode = UnityEngine.TextureWrapMode.Repeat;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
        }
    }
}
