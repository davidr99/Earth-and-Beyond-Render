using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using W3DImporter.Structs;
using W3DImporter.Structs.Mesh;
using W3DImporter.Structs.Hierarchy;
using W3DImporter.Structs.HLod;
using W3DImporter.Structs.Aggregate;
using W3DImporter.Parsers;

namespace W3DImporter.Builders
{
    public class W3DSceneBuilder
    {
        private readonly string _baseDirectory;
        private readonly W3DTextureResolver _textureResolver;

        public W3DSceneBuilder(string baseDirectory)
        {
            _baseDirectory = baseDirectory;
            _textureResolver = new W3DTextureResolver(baseDirectory);
        }

        public GameObject Build(W3DDataContext context)
        {
            W3DDataContext aggregateBaseModel = null;

            if (context.Aggregate != null)
            {
                aggregateBaseModel = LoadAggregateBaseModel(context);
            }

            if (context.Hierarchy == null)
                LoadExternalHierarchy(context);

            if (context.Aggregate != null)
                context.HLod = BuildHLodFromAggregate(context, aggregateBaseModel);

            string rootName = context.Aggregate?.Header?.Name
                ?? context.HLod?.Header?.ModelName
                ?? context.Hierarchy?.Header?.Name
                ?? "W3DModel";
            var root = new GameObject(rootName);

            var boneTransforms = new Dictionary<int, Transform>();
            if (context.Hierarchy != null)
            {
                BuildHierarchy(context.Hierarchy, root.transform, boneTransforms);
            }

            if (context.HLod != null)
            {
                bool hasMultipleLods = context.HLod.LodArrays.Count > 1;

                if (hasMultipleLods)
                {
                    var lodGroup = root.AddComponent<LODGroup>();
                    int lodCount = context.HLod.LodArrays.Count;
                    var lodList = new List<LOD>();

                    for (int i = lodCount - 1; i >= 0; i--)
                    {
                        var lodArray = context.HLod.LodArrays[i];
                        var renderers = new List<Renderer>();

                        foreach (var subObj in lodArray.SubObjects)
                        {
                            if (string.IsNullOrEmpty(subObj.Name) || subObj.Name == "NULL")
                                continue;

                            foreach (var mesh in context.Meshes)
                            {
                                if (mesh.Name == subObj.Name)
                                {
                                    bool rigSpace = mesh.IsSkin && mesh.VertInfs.Count == 0;
                                    Transform meshParent = root.transform;
                                    if (!rigSpace && boneTransforms.ContainsKey(subObj.BoneIndex))
                                        meshParent = boneTransforms[subObj.BoneIndex];

                                    Debug.Log($"[W3D Attach] {mesh.Name}: skin={mesh.IsSkin} infs={mesh.VertInfs.Count} bone={subObj.BoneIndex} parent={meshParent.name}");

                                    var meshGo = W3DMeshBuilder.Build(mesh, _textureResolver);
                                    meshGo.transform.SetParent(meshParent, false);
                                    var mr = meshGo.GetComponent<Renderer>();
                                    if (mr != null) renderers.Add(mr);
                                    break;
                                }
                            }
                        }

                        if (renderers.Count == 0) continue;

                        int unityLodIdx = lodList.Count;
                        float screenSize = Mathf.Lerp(0.01f, 0.5f, 1f - (float)unityLodIdx / Mathf.Max(1, lodCount - 1));
                        lodList.Add(new LOD(screenSize, renderers.ToArray()));
                        Debug.Log($"[W3D] LOD[{unityLodIdx}] (w3d {i}): {renderers.Count} renderers, threshold={screenSize:F2}");
                    }

                    if (lodList.Count > 0)
                    {
                        lodGroup.SetLODs(lodList.ToArray());
                        lodGroup.RecalculateBounds();
                    }
                    else
                    {
                        UnityEngine.Object.Destroy(lodGroup);
                    }
                }
                else
                {
                    foreach (var lodArray in context.HLod.LodArrays)
                    {
                        foreach (var subObj in lodArray.SubObjects)
                        {
                            foreach (var mesh in context.Meshes)
                            {
                                if (mesh.Name == subObj.Name)
                                {
                                    Transform meshParent = root.transform;
                                    if (!(mesh.IsSkin && mesh.VertInfs.Count == 0) &&
                                        boneTransforms.ContainsKey(subObj.BoneIndex))
                                    {
                                        meshParent = boneTransforms[subObj.BoneIndex];
                                    }

                                    var meshGo = W3DMeshBuilder.Build(mesh, _textureResolver);
                                    meshGo.transform.SetParent(meshParent, false);
                                    break;
                                }
                            }

                            foreach (var emitter in context.Emitters)
                            {
                                if (emitter.Name == subObj.Name)
                                {
                                    Transform boneParent = root.transform;
                                    if (boneTransforms.ContainsKey(subObj.BoneIndex))
                                        boneParent = boneTransforms[subObj.BoneIndex];
                                    W3DEmitterBuilder.Build(emitter, boneParent, _textureResolver);
                                    break;
                                }
                            }
                        }
                    }
                }

                if (hasMultipleLods)
                {
                    foreach (var lodArray in context.HLod.LodArrays)
                    {
                        foreach (var subObj in lodArray.SubObjects)
                        {
                            foreach (var emitter in context.Emitters)
                            {
                                if (emitter.Name == subObj.Name)
                                {
                                    Transform boneParent = root.transform;
                                    if (boneTransforms.ContainsKey(subObj.BoneIndex))
                                        boneParent = boneTransforms[subObj.BoneIndex];
                                    W3DEmitterBuilder.Build(emitter, boneParent, _textureResolver);
                                    break;
                                }
                            }
                        }
                        break;
                    }
                }
            }
            else
            {
                foreach (var mesh in context.Meshes)
                {
                    var meshGo = W3DMeshBuilder.Build(mesh, _textureResolver);
                    meshGo.transform.SetParent(root.transform, false);
                }
            }

            if (context.Aggregate != null)
                BuildAggregateSubModels(context, root.transform, boneTransforms);

            foreach (var box in context.CollisionBoxes)
            {
                W3DCollisionBoxBuilder.Build(box, root.transform, boneTransforms);
            }

            var matchedEmitters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (context.HLod != null)
            {
                foreach (var lodArray in context.HLod.LodArrays)
                    foreach (var subObj in lodArray.SubObjects)
                        matchedEmitters.Add(subObj.Name);
            }

            foreach (var emitter in context.Emitters)
            {
                if (!matchedEmitters.Contains(emitter.Name))
                    W3DEmitterBuilder.Build(emitter, root.transform, _textureResolver);
            }

            return root;
        }

        private void BuildHierarchy(W3DHierarchy hierarchy, Transform root, Dictionary<int, Transform> boneMap)
        {
            boneMap[0] = root;

            for (int i = 0; i < hierarchy.Pivots.Count; i++)
            {
                var pivot = hierarchy.Pivots[i];

                if (i == 0)
                {
                    root.localPosition = W3DCoordinateConverter.ConvertPosition(pivot.Translation);
                    root.localRotation = W3DCoordinateConverter.ConvertRotation(pivot.Rotation);
                    continue;
                }

                var boneGo = new GameObject(pivot.Name);
                boneGo.transform.localPosition = W3DCoordinateConverter.ConvertPosition(pivot.Translation);
                boneGo.transform.localRotation = W3DCoordinateConverter.ConvertRotation(pivot.Rotation);

                Transform parent = root;
                if (pivot.ParentId >= 0 && boneMap.ContainsKey(pivot.ParentId))
                    parent = boneMap[pivot.ParentId];
                boneGo.transform.SetParent(parent, false);

                boneMap[i] = boneGo.transform;
            }
        }

        private void LoadExternalHierarchy(W3DDataContext context)
        {
            string skelName = null;

            if (context.HLod != null && context.HLod.Header.ModelName != context.HLod.Header.HierarchyName)
                skelName = context.HLod.Header.HierarchyName;
            else if (context.Aggregate?.Info != null)
                skelName = context.Aggregate.Info.BaseModelName;
            else if (context.Animation != null)
                skelName = context.Animation.Header.HierarchyName;
            else if (context.CompressedAnimation != null)
                skelName = context.CompressedAnimation.Header.HierarchyName;

            if (!string.IsNullOrEmpty(skelName))
            {
                string skelPath = Path.Combine(_baseDirectory, skelName.ToLower() + ".w3d");
                if (File.Exists(skelPath))
                {
                    if (context.Aggregate != null)
                    {
                        var hierarchyContext = W3DFileParser.Parse(skelPath);
                        if (context.Hierarchy == null)
                            context.Hierarchy = hierarchyContext.Hierarchy;
                    }
                    else
                    {
                        W3DFileParser.ParseFile(skelPath, context);
                    }
                }
                else
                {
                    Debug.LogWarning($"[W3D] Hierarchy file not found: {skelPath}");
                }
            }
        }

        private W3DDataContext LoadAggregateBaseModel(W3DDataContext context)
        {
            var baseModelName = context.Aggregate?.Info?.BaseModelName;
            if (string.IsNullOrEmpty(baseModelName))
                return null;

            string modelPath = Path.Combine(_baseDirectory, baseModelName.ToLower() + ".w3d");
            if (!File.Exists(modelPath))
            {
                Debug.LogWarning($"[W3D] Aggregate base model not found: {modelPath}");
                return null;
            }

            Debug.Log($"[W3D] Loading aggregate base model: {modelPath}");
            var baseContext = W3DFileParser.Parse(modelPath);

            MergeAggregateBaseModel(context, baseContext);
            if (context.Hierarchy == null)
                context.Hierarchy = baseContext.Hierarchy;

            return baseContext;
        }

        private void MergeAggregateBaseModel(W3DDataContext context, W3DDataContext baseContext)
        {
            var meshIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mesh in context.Meshes)
                meshIds.Add(mesh.Identifier);

            foreach (var mesh in baseContext.Meshes)
            {
                if (meshIds.Add(mesh.Identifier))
                    context.Meshes.Add(mesh);
            }

            var emitterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var emitter in context.Emitters)
                emitterNames.Add(emitter.Name);

            foreach (var emitter in baseContext.Emitters)
            {
                if (emitterNames.Add(emitter.Name))
                    context.Emitters.Add(emitter);
            }

            foreach (var box in baseContext.CollisionBoxes)
                context.CollisionBoxes.Add(box);
        }

        private void BuildAggregateSubModels(W3DDataContext context, Transform root, Dictionary<int, Transform> boneTransforms)
        {
            if (context.Aggregate?.Info == null)
                return;

            foreach (var sub in context.Aggregate.Info.SubObjects)
            {
                if (string.IsNullOrWhiteSpace(sub.SubObjectName))
                    continue;

                string modelPath = Path.Combine(_baseDirectory, sub.SubObjectName.ToLower() + ".w3d");
                if (!File.Exists(modelPath))
                {
                    Debug.LogWarning($"[W3D] Aggregate sub-model not found: {modelPath}");
                    continue;
                }

                var subContext = W3DFileParser.Parse(modelPath);
                var subRoot = new W3DSceneBuilder(_baseDirectory).Build(subContext);

                Transform parent = root;
                if (context.Hierarchy != null)
                {
                    int boneIndex = context.Hierarchy.FindBoneIndex(sub.BoneName);
                    if (boneTransforms.TryGetValue(boneIndex, out var boneTransform))
                        parent = boneTransform;
                }

                subRoot.transform.SetParent(parent, false);
                subRoot.transform.localPosition = Vector3.zero;
                subRoot.transform.localRotation = Quaternion.identity;
                subRoot.transform.localScale = Vector3.one;
                Debug.Log($"[W3D Aggregate] Attached '{sub.SubObjectName}' to bone '{sub.BoneName}' under '{parent.name}'");
            }
        }

        private W3DHLod BuildHLodFromAggregate(W3DDataContext context, W3DDataContext baseModelContext)
        {
            var agg = context.Aggregate;
            var header = new W3DHLodHeader
            {
                ModelName = agg.Header?.Name ?? "",
                HierarchyName = agg.Info?.BaseModelName ?? agg.Header?.Name ?? ""
            };

            var lodArrays = new List<W3DHLodArray>();
            if (baseModelContext?.HLod != null && baseModelContext.HLod.LodArrays.Count > 0)
            {
                foreach (var sourceArray in baseModelContext.HLod.LodArrays)
                {
                    var array = new W3DHLodArray
                    {
                        Header = new W3DHLodArrayHeader
                        {
                            ModelCount = sourceArray.Header?.ModelCount ?? sourceArray.SubObjects.Count,
                            MaxScreenSize = sourceArray.Header?.MaxScreenSize ?? 0f
                        }
                    };

                    foreach (var sub in sourceArray.SubObjects)
                    {
                        array.SubObjects.Add(new W3DHLodSubObject
                        {
                            BoneIndex = sub.BoneIndex,
                            Identifier = sub.Identifier,
                            Name = sub.Name
                        });
                    }

                    array.Header.ModelCount = array.SubObjects.Count;
                    lodArrays.Add(array);
                }
            }
            else
            {
                var subObjects = new List<W3DHLodSubObject>();

                if (baseModelContext != null)
                {
                    foreach (var mesh in baseModelContext.Meshes)
                    {
                        subObjects.Add(new W3DHLodSubObject
                        {
                            BoneIndex = 0,
                            Identifier = mesh.Identifier,
                            Name = mesh.Name
                        });
                    }
                }

                lodArrays.Add(new W3DHLodArray
                {
                    Header = new W3DHLodArrayHeader { ModelCount = subObjects.Count },
                    SubObjects = subObjects
                });
            }

            return new W3DHLod
            {
                Header = header,
                LodArrays = lodArrays
            };
        }
    }
}


