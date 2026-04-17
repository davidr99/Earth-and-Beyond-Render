using System.IO;
using UnityEngine;
using UnityEditor.AssetImporters;
using W3DImporter.Parsers;
using W3DImporter.Builders;

namespace W3DImporter.Editor
{
    [ScriptedImporter(1, "w3d")]
    public class W3DScriptedImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            string filePath = ctx.assetPath;
            string baseDir = Path.GetDirectoryName(Path.GetFullPath(filePath));

            Debug.Log($"[W3D Editor] Importing: {filePath}");

            var context = W3DFileParser.Parse(Path.GetFullPath(filePath));
            var builder = new W3DSceneBuilder(baseDir);
            var root = builder.Build(context);

            // Register all created objects as sub-assets
            ctx.AddObjectToAsset("root", root);
            ctx.SetMainObject(root);

            // Register meshes and materials as sub-assets
            RegisterSubAssets(ctx, root);
        }

        private void RegisterSubAssets(AssetImportContext ctx, GameObject root)
        {
            int meshIndex = 0;
            int matIndex = 0;

            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh != null)
                {
                    ctx.AddObjectToAsset($"mesh_{meshIndex}", mf.sharedMesh);
                    meshIndex++;
                }
            }

            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr.sharedMaterials != null)
                {
                    foreach (var mat in mr.sharedMaterials)
                    {
                        if (mat != null)
                        {
                            ctx.AddObjectToAsset($"mat_{matIndex}", mat);
                            matIndex++;

                            // Register textures
                            if (mat.mainTexture != null && mat.mainTexture is Texture2D tex)
                            {
                                ctx.AddObjectToAsset($"tex_{tex.name}", tex);
                            }
                        }
                    }
                }
            }
        }
    }
}
