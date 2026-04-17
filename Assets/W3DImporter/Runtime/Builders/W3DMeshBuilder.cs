using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using W3DImporter.Structs.Mesh;

namespace W3DImporter.Builders
{
    public static class W3DMeshBuilder
    {
        public static GameObject Build(W3DMesh w3dMesh, W3DTextureResolver textureResolver)
        {
            var go = new GameObject(w3dMesh.Name);

            if (w3dMesh.EffectiveHidden)
                go.SetActive(false);

            var mesh = new UnityEngine.Mesh();
            mesh.name = w3dMesh.Name;

            if (w3dMesh.Verts.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;

            var vertices = new Vector3[w3dMesh.Verts.Count];
            for (int i = 0; i < w3dMesh.Verts.Count; i++)
                vertices[i] = W3DCoordinateConverter.ConvertPosition(w3dMesh.Verts[i]);
            mesh.vertices = vertices;

            if (w3dMesh.Normals.Count == w3dMesh.Verts.Count)
            {
                var normals = new Vector3[w3dMesh.Normals.Count];
                for (int i = 0; i < w3dMesh.Normals.Count; i++)
                    normals[i] = W3DCoordinateConverter.ConvertNormal(w3dMesh.Normals[i]);
                mesh.normals = normals;
            }

            var triangles = new int[w3dMesh.Triangles.Count * 3];
            for (int i = 0; i < w3dMesh.Triangles.Count; i++)
            {
                triangles[i * 3 + 0] = (int)w3dMesh.Triangles[i].VertIds[0];
                triangles[i * 3 + 1] = (int)w3dMesh.Triangles[i].VertIds[1];
                triangles[i * 3 + 2] = (int)w3dMesh.Triangles[i].VertIds[2];
            }
            mesh.triangles = triangles;

            SetUVs(mesh, w3dMesh);
            SetVertexColors(mesh, w3dMesh);

            if (mesh.normals == null || mesh.normals.Length == 0)
                mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            var materials = W3DMaterialBuilder.Build(w3dMesh, textureResolver);
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = w3dMesh.CastsShadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = w3dMesh.CastsShadow;
            AttachUvScroller(go, w3dMesh);
            AttachBillboard(go, w3dMesh);

            Debug.Log($"[W3D] Mesh '{w3dMesh.Name}': verts={mesh.vertexCount}, tris={mesh.triangles.Length/3}, " +
                      $"uvs={mesh.uv?.Length ?? 0}, materials={materials.Length}, " +
                      $"textures={w3dMesh.Textures.Count}, passes={w3dMesh.MaterialPasses.Count}, " +
                      $"camAligned={w3dMesh.IsCameraAligned}, camOriented={w3dMesh.IsCameraOriented}");
            for (int i = 0; i < materials.Length; i++)
            {
                var tex = materials[i]?.mainTexture;
                Debug.Log($"  mat[{i}] shader='{materials[i]?.shader?.name}' texture='{tex?.name ?? "NULL"}' " +
                         $"size={tex?.width ?? 0}x{tex?.height ?? 0}");
            }

            return go;
        }

        private static void AttachUvScroller(GameObject go, W3DMesh w3dMesh)
        {
            var effect = w3dMesh.EffectPass;
            if (effect?.CycleTime == null || effect.CycleTime <= 0f)
                return;

            var scroller = go.AddComponent<W3DUVScroller>();
            scroller.CycleTime = effect.CycleTime.Value;
            scroller.StartV = effect.StartVCoord ?? 0f;
            scroller.EndV = effect.EndVCoord ?? 1f;
        }

        private static void AttachBillboard(GameObject go, W3DMesh w3dMesh)
        {
            if (w3dMesh.IsCameraOriented)
            {
                var billboard = go.AddComponent<W3DBillboard>();
                billboard.Mode = W3DBillboard.BillboardMode.CameraOriented;
                return;
            }

            if (w3dMesh.IsCameraAligned)
            {
                var billboard = go.AddComponent<W3DBillboard>();
                billboard.Mode = W3DBillboard.BillboardMode.CameraAligned;
            }
        }

        private static void SetUVs(UnityEngine.Mesh mesh, W3DMesh w3dMesh)
        {
            if (w3dMesh.DirectUVs.Count > 0)
            {
                var directUVs = new Vector2[mesh.vertexCount];
                for (int i = 0; i < mesh.vertexCount; i++)
                {
                    if (i < w3dMesh.DirectUVs.Count)
                    {
                        var uv = w3dMesh.DirectUVs[i];
                        directUVs[i] = new Vector2(uv.x, 1f - uv.y);
                    }
                    else
                    {
                        directUVs[i] = Vector2.zero;
                    }
                }

                mesh.uv = directUVs;
                return;
            }

            if (w3dMesh.MaterialPasses.Count == 0)
            {
                Debug.LogWarning($"[W3D UV] {w3dMesh.Name}: no material passes");
                return;
            }

            var pass = w3dMesh.MaterialPasses[0];
            List<Vector2> rawUVs = null;

            if (pass.TxStages.Count > 0 && pass.TxStages[0].TxCoords.Count > 0)
            {
                rawUVs = pass.TxStages[0].TxCoords[0];
                Debug.Log($"[W3D UV] {w3dMesh.Name}: from TxStage[0], {rawUVs.Count} UVs, {mesh.vertexCount} verts");
            }
            else if (pass.TxCoords.Count > 0)
            {
                rawUVs = pass.TxCoords;
                Debug.Log($"[W3D UV] {w3dMesh.Name}: from pass TxCoords, {rawUVs.Count} UVs, {mesh.vertexCount} verts");
            }
            else
            {
                Debug.LogWarning($"[W3D UV] {w3dMesh.Name}: no UV coords found! " +
                                 $"stages={pass.TxStages.Count}, passCoords={pass.TxCoords.Count}");
                return;
            }

            var finalUVs = new Vector2[mesh.vertexCount];
            for (int i = 0; i < mesh.vertexCount; i++)
            {
                if (i < rawUVs.Count)
                    finalUVs[i] = new Vector2(rawUVs[i].x, 1f - rawUVs[i].y);
                else
                    finalUVs[i] = Vector2.zero;
            }
            mesh.uv = finalUVs;

            if (rawUVs.Count != mesh.vertexCount)
                Debug.LogWarning($"[W3D UV] {w3dMesh.Name}: UV count ({rawUVs.Count}) != vertex count ({mesh.vertexCount}), padded");

            if (rawUVs.Count > 0)
                Debug.Log($"[W3D UV] {w3dMesh.Name}: sample UV[0]={rawUVs[0]}, UV[1]={(rawUVs.Count > 1 ? rawUVs[1].ToString() : "N/A")}");

            if (pass.TxStages.Count > 1 && pass.TxStages[1].TxCoords.Count > 0)
            {
                var txCoords2 = pass.TxStages[1].TxCoords[0];
                if (txCoords2.Count == mesh.vertexCount)
                    mesh.uv2 = txCoords2.ToArray();
            }
        }

        private static void SetVertexColors(UnityEngine.Mesh mesh, W3DMesh w3dMesh)
        {
            if (w3dMesh.DirectVertexColors.Count > 0)
            {
                mesh.colors32 = w3dMesh.DirectVertexColors.ToArray();
                return;
            }

            if (w3dMesh.MaterialPasses.Count == 0) return;
            var pass = w3dMesh.MaterialPasses[0];

            if (pass.Dcg.Count > 0)
            {
                mesh.colors32 = pass.Dcg.ToArray();
            }
        }
    }
}
