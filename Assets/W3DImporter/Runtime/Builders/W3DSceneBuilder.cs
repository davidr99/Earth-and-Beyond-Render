using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using W3DImporter.Structs;
using W3DImporter.Structs.Mesh;
using W3DImporter.Structs.Hierarchy;
using W3DImporter.Structs.HLod;
using W3DImporter.Structs.Aggregate;
using W3DImporter.Structs.Animation;
using W3DImporter.Structs.Emitter;
using W3DImporter.Parsers;

namespace W3DImporter.Builders
{
    public class W3DSceneBuilder
    {
        private const bool EnableVerboseBuildLogging = false;
        private static readonly Dictionary<string, string[]> SharedW3dFileListCache = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        private readonly string _baseDirectory;
        private readonly W3DTextureResolver _textureResolver;
        private readonly Dictionary<string, GameObject> _aggregateSubModelTemplateCache;

        public W3DSceneBuilder(string baseDirectory)
            : this(baseDirectory, new W3DTextureResolver(baseDirectory), new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase))
        {
        }

        private W3DSceneBuilder(string baseDirectory, W3DTextureResolver textureResolver, Dictionary<string, GameObject> aggregateSubModelTemplateCache)
        {
            _baseDirectory = baseDirectory;
            _textureResolver = textureResolver ?? new W3DTextureResolver(baseDirectory);
            _aggregateSubModelTemplateCache = aggregateSubModelTemplateCache ?? new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
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
                ?? context.LodModel?.Header?.Name
                ?? context.HLod?.Header?.ModelName
                ?? context.Hierarchy?.Header?.Name
                ?? "W3DModel";
            var root = new GameObject(rootName);
            var meshLookup = CreateMeshLookup(context.Meshes);
            var emitterLookup = CreateEmitterLookup(context.Emitters);
            bool builtLodModel = context.LodModel != null && BuildLodModel(context, root.transform, meshLookup);

            var boneTransforms = new Dictionary<int, Transform>();
            if (context.Hierarchy != null)
            {
                BuildHierarchy(context.Hierarchy, root.transform, boneTransforms);
            }

            if (!builtLodModel && context.HLod != null)
            {
                ZeroBillboardOnlyBoneRotations(context, boneTransforms, meshLookup);

                var lodArray = GetHighestDetailHLodArray(context.HLod);
                if (lodArray != null)
                {
                    foreach (var subObj in lodArray.SubObjects)
                    {
                        if (meshLookup.TryGetValue(subObj.Name, out var mesh))
                        {
                            Transform meshParent = root.transform;
                            if (!(mesh.IsSkin && mesh.VertInfs.Count == 0) &&
                                boneTransforms.ContainsKey(subObj.BoneIndex))
                            {
                                meshParent = boneTransforms[subObj.BoneIndex];
                            }

                            if (EnableVerboseBuildLogging)
                                Debug.Log($"[W3D Attach] {mesh.Name}: skin={mesh.IsSkin} infs={mesh.VertInfs.Count} bone={subObj.BoneIndex} parent={meshParent.name}");

                            var meshGo = W3DMeshBuilder.Build(mesh, _textureResolver);
                            meshGo.transform.SetParent(meshParent, false);
                        }

                        if (emitterLookup.TryGetValue(subObj.Name, out var emitter))
                        {
                            Transform boneParent = root.transform;
                            if (boneTransforms.ContainsKey(subObj.BoneIndex))
                                boneParent = boneTransforms[subObj.BoneIndex];
                            W3DEmitterBuilder.Build(emitter, boneParent, _textureResolver);
                        }
                    }
                }
            }
            else if (!builtLodModel)
            {
                foreach (var mesh in context.Meshes)
                {
                    var meshGo = W3DMeshBuilder.Build(mesh, _textureResolver);
                    meshGo.transform.SetParent(root.transform, false);
                }
            }

            if (context.Aggregate != null)
                BuildAggregateSubModels(context, root.transform, boneTransforms);

            AttachAnimation(root, context);

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

        private void AttachAnimation(GameObject root, W3DDataContext context)
        {
            if (context.Hierarchy == null)
                return;

            LoadExternalAnimations(context);

            var clips = new List<AnimationClip>();
            var clipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddClip(context.Animation, context.Hierarchy, clips, clipNames);
            for (int i = 0; i < context.Animations.Count; i++)
                AddClip(context.Animations[i], context.Hierarchy, clips, clipNames);

            AddClip(context.CompressedAnimation, context.Hierarchy, clips, clipNames);
            for (int i = 0; i < context.CompressedAnimations.Count; i++)
                AddClip(context.CompressedAnimations[i], context.Hierarchy, clips, clipNames);

            if (clips.Count == 0)
                return;

            if (!root.TryGetComponent<Animation>(out var animation) || animation == null)
                animation = root.AddComponent<Animation>();

            if (animation == null)
            {
                Debug.LogWarning($"[W3D] Unable to attach Animation component to '{root.name}'; skipping {clips.Count} clip(s)");
                return;
            }

            for (int i = 0; i < clips.Count; i++)
            {
                if (clips[i] == null)
                    continue;
                animation.AddClip(clips[i], clips[i].name);
            }

            animation.clip = clips[0];
            animation.wrapMode = WrapMode.Loop;
            animation.playAutomatically = true;
            animation.Play(clips[0].name);

            if (EnableVerboseBuildLogging)
                Debug.Log($"[W3D] Attached {clips.Count} animation clip(s); playing '{clips[0].name}'");
        }

        private void AddClip(W3DAnimation animationData, W3DHierarchy hierarchy, List<AnimationClip> clips, HashSet<string> clipNames)
        {
            if (animationData == null)
                return;

            var clip = W3DAnimationBuilder.Build(animationData, hierarchy);
            if (clip != null && clipNames.Add(clip.name))
                clips.Add(clip);
        }

        private void AddClip(W3DCompressedAnimation animationData, W3DHierarchy hierarchy, List<AnimationClip> clips, HashSet<string> clipNames)
        {
            if (animationData == null)
                return;

            var clip = W3DCompressedAnimationBuilder.Build(animationData, hierarchy);
            if (clip != null && clipNames.Add(clip.name))
                clips.Add(clip);
        }

        // W3D runtime discards pivot rotation for CAMERA_ALIGNED / CAMERA_ORIENTED meshes
        // (Renegade dx8renderer.cpp Obj_Look_At replaces rotation at render time, keeping only
        // the pivot's world position). Unity inherits the authored pivot rotation through
        // transform.parent, so in edit mode and in play mode before the first LateUpdate the
        // billboard renders with whatever 180° Z etc. the artist baked into the pivot. Zeroing
        // localRotation on bones that exclusively host billboard meshes matches runtime semantics
        // without disturbing translation or any unrelated non-billboard children on the same bone.
        private void ZeroBillboardOnlyBoneRotations(W3DDataContext context, Dictionary<int, Transform> boneTransforms, Dictionary<string, W3DMesh> meshLookup)
        {
            var boneHasBillboard = new HashSet<int>();
            var boneHasNonBillboard = new HashSet<int>();

            foreach (var lodArray in context.HLod.LodArrays)
            {
                foreach (var subObj in lodArray.SubObjects)
                {
                    if (string.IsNullOrEmpty(subObj.Name) || subObj.Name == "NULL")
                        continue;

                    if (!meshLookup.TryGetValue(subObj.Name, out var matched))
                        continue;

                    if (matched.IsCameraAligned || matched.IsCameraOriented)
                        boneHasBillboard.Add(subObj.BoneIndex);
                    else
                        boneHasNonBillboard.Add(subObj.BoneIndex);
                }
            }

            foreach (int boneIndex in boneHasBillboard)
            {
                if (boneHasNonBillboard.Contains(boneIndex))
                    continue;
                if (!boneTransforms.TryGetValue(boneIndex, out var bone))
                    continue;
                if (bone == null || bone.localRotation == Quaternion.identity)
                    continue;

                if (EnableVerboseBuildLogging)
                    Debug.Log($"[W3D Billboard] Zeroing localRotation on bone '{bone.name}' (hosts only billboard meshes)");
                bone.localRotation = Quaternion.identity;
            }
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

        private void LoadExternalAnimations(W3DDataContext context)
        {
            string hierarchyName = context.Hierarchy?.Header?.Name;
            if (string.IsNullOrWhiteSpace(hierarchyName) || string.IsNullOrWhiteSpace(_baseDirectory) || !Directory.Exists(_baseDirectory))
                return;

            var knownUncompressed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (context.Animation?.Header != null)
                knownUncompressed.Add(context.Animation.Header.Name);
            for (int i = 0; i < context.Animations.Count; i++)
            {
                if (context.Animations[i]?.Header != null)
                    knownUncompressed.Add(context.Animations[i].Header.Name);
            }

            var knownCompressed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (context.CompressedAnimation?.Header != null)
                knownCompressed.Add(context.CompressedAnimation.Header.Name);
            for (int i = 0; i < context.CompressedAnimations.Count; i++)
            {
                if (context.CompressedAnimations[i]?.Header != null)
                    knownCompressed.Add(context.CompressedAnimations[i].Header.Name);
            }

            foreach (var path in GetW3dFiles(_baseDirectory))
            {
                var foundAnimations = new List<W3DAnimation>();
                var foundCompressedAnimations = new List<W3DCompressedAnimation>();
                W3DFileParser.CollectAnimations(path, hierarchyName, foundAnimations, foundCompressedAnimations);

                for (int i = 0; i < foundAnimations.Count; i++)
                {
                    var animation = foundAnimations[i];
                    if (animation?.Header == null || !knownUncompressed.Add(animation.Header.Name))
                        continue;

                    context.Animations.Add(animation);
                    if (context.Animation == null && context.CompressedAnimation == null)
                        context.Animation = animation;
                }

                for (int i = 0; i < foundCompressedAnimations.Count; i++)
                {
                    var animation = foundCompressedAnimations[i];
                    if (animation?.Header == null || !knownCompressed.Add(animation.Header.Name))
                        continue;

                    context.CompressedAnimations.Add(animation);
                    if (context.Animation == null && context.CompressedAnimation == null)
                        context.CompressedAnimation = animation;
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

            if (EnableVerboseBuildLogging)
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

            var boneTransformLookup = CreateBoneTransformLookup(context.Hierarchy, boneTransforms);

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

                string normalizedModelPath = Path.GetFullPath(modelPath);
                GameObject subRoot;
                if (_aggregateSubModelTemplateCache.TryGetValue(normalizedModelPath, out var template) && template != null)
                {
                    subRoot = UnityEngine.Object.Instantiate(template);
                }
                else
                {
                    var subContext = W3DFileParser.Parse(normalizedModelPath);
                    subRoot = new W3DSceneBuilder(_baseDirectory, _textureResolver, _aggregateSubModelTemplateCache).Build(subContext);
                    _aggregateSubModelTemplateCache[normalizedModelPath] = subRoot;
                }

                Transform parent = root;
                if (!string.IsNullOrWhiteSpace(sub.BoneName) &&
                    boneTransformLookup.TryGetValue(sub.BoneName, out var boneTransform))
                    parent = boneTransform;

                subRoot.transform.SetParent(parent, false);
                subRoot.transform.localPosition = Vector3.zero;
                subRoot.transform.localRotation = Quaternion.identity;
                subRoot.transform.localScale = Vector3.one;
                if (EnableVerboseBuildLogging)
                    Debug.Log($"[W3D Aggregate] Attached '{sub.SubObjectName}' to bone '{sub.BoneName}' under '{parent.name}'");
            }
        }

        private static Dictionary<string, W3DMesh> CreateMeshLookup(List<W3DMesh> meshes)
        {
            var lookup = new Dictionary<string, W3DMesh>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < meshes.Count; i++)
            {
                var mesh = meshes[i];
                if (mesh != null && !string.IsNullOrWhiteSpace(mesh.Name) && !lookup.ContainsKey(mesh.Name))
                    lookup.Add(mesh.Name, mesh);
            }

            return lookup;
        }

        private static Dictionary<string, W3DEmitter> CreateEmitterLookup(List<W3DEmitter> emitters)
        {
            var lookup = new Dictionary<string, W3DEmitter>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < emitters.Count; i++)
            {
                var emitter = emitters[i];
                if (emitter != null && !string.IsNullOrWhiteSpace(emitter.Name) && !lookup.ContainsKey(emitter.Name))
                    lookup.Add(emitter.Name, emitter);
            }

            return lookup;
        }

        private static Dictionary<string, Transform> CreateBoneTransformLookup(W3DHierarchy hierarchy, Dictionary<int, Transform> boneTransforms)
        {
            var lookup = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
            if (hierarchy == null || boneTransforms == null)
                return lookup;

            for (int i = 0; i < hierarchy.Pivots.Count; i++)
            {
                var pivot = hierarchy.Pivots[i];
                if (pivot == null || string.IsNullOrWhiteSpace(pivot.Name))
                    continue;

                if (boneTransforms.TryGetValue(i, out var boneTransform) && boneTransform != null && !lookup.ContainsKey(pivot.Name))
                    lookup.Add(pivot.Name, boneTransform);
            }

            return lookup;
        }

        private static W3DHLodArray GetHighestDetailHLodArray(W3DHLod hlod)
        {
            if (hlod == null || hlod.LodArrays.Count == 0)
                return null;

            return hlod.LodArrays[hlod.LodArrays.Count - 1];
        }

        private bool BuildLodModel(W3DDataContext context, Transform root, Dictionary<string, W3DMesh> localMeshes)
        {
            if (context?.LodModel == null || context.LodModel.Levels.Count == 0)
                return false;

            for (int i = 0; i < context.LodModel.Levels.Count; i++)
            {
                var level = context.LodModel.Levels[i];
                if (level == null || string.IsNullOrWhiteSpace(level.RenderObjName))
                    continue;

                var levelRoot = new GameObject(level.RenderObjName);
                levelRoot.transform.SetParent(root, false);

                bool built = false;
                string localMeshName = GetRenderObjLeafName(level.RenderObjName);
                if (localMeshes.TryGetValue(localMeshName, out var localMesh))
                {
                    var meshGo = W3DMeshBuilder.Build(localMesh, _textureResolver);
                    meshGo.transform.SetParent(levelRoot.transform, false);
                    built = true;
                }
                else if (TryLoadReferencedRenderObject(level.RenderObjName, context.SourcePath, out var referencedRoot))
                {
                    referencedRoot.transform.SetParent(levelRoot.transform, false);
                    referencedRoot.transform.localPosition = Vector3.zero;
                    referencedRoot.transform.localRotation = Quaternion.identity;
                    referencedRoot.transform.localScale = Vector3.one;
                    built = true;
                }

                if (!built)
                {
                    UnityEngine.Object.Destroy(levelRoot);
                    Debug.LogWarning($"[W3D] LOD render object not found: {level.RenderObjName}");
                    continue;
                }

                var renderers = levelRoot.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                {
                    UnityEngine.Object.Destroy(levelRoot);
                    continue;
                }

                return true;
            }

            return false;
        }

        private bool TryLoadReferencedRenderObject(string renderObjName, string sourcePath, out GameObject root)
        {
            root = null;
            string path = ResolveRenderObjectPath(renderObjName);
            if (string.IsNullOrEmpty(path))
                return false;

            string normalizedPath = Path.GetFullPath(path);
            if (!string.IsNullOrWhiteSpace(sourcePath) &&
                string.Equals(normalizedPath, Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (_aggregateSubModelTemplateCache.TryGetValue(normalizedPath, out var template) && template != null)
            {
                root = UnityEngine.Object.Instantiate(template);
                return true;
            }

            var context = W3DFileParser.Parse(normalizedPath);
            root = new W3DSceneBuilder(_baseDirectory, _textureResolver, _aggregateSubModelTemplateCache).Build(context);
            _aggregateSubModelTemplateCache[normalizedPath] = root;
            return root != null;
        }

        private string ResolveRenderObjectPath(string renderObjName)
        {
            if (string.IsNullOrWhiteSpace(_baseDirectory) || !Directory.Exists(_baseDirectory))
                return null;

            foreach (var candidate in GetRenderObjectPathCandidates(renderObjName))
            {
                string path = Path.Combine(_baseDirectory, candidate + ".w3d");
                if (File.Exists(path))
                    return path;
            }

            return null;
        }

        private static IEnumerable<string> GetRenderObjectPathCandidates(string renderObjName)
        {
            string normalized = (renderObjName ?? string.Empty).Trim();
            if (normalized.Length == 0)
                yield break;

            yield return normalized.ToLowerInvariant();

            int dot = normalized.IndexOf('.');
            if (dot > 0)
                yield return normalized.Substring(0, dot).ToLowerInvariant();

            if (dot >= 0 && dot + 1 < normalized.Length)
                yield return normalized.Substring(dot + 1).ToLowerInvariant();
        }

        private static string GetRenderObjLeafName(string renderObjName)
        {
            if (string.IsNullOrWhiteSpace(renderObjName))
                return string.Empty;

            int dot = renderObjName.IndexOf('.');
            return dot >= 0 && dot + 1 < renderObjName.Length
                ? renderObjName.Substring(dot + 1)
                : renderObjName;
        }

        private static string[] GetW3dFiles(string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory) || !Directory.Exists(baseDirectory))
                return Array.Empty<string>();

            if (SharedW3dFileListCache.TryGetValue(baseDirectory, out var cached))
                return cached;

            var files = Directory.GetFiles(baseDirectory, "*.w3d");
            SharedW3dFileListCache[baseDirectory] = files;
            return files;
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


