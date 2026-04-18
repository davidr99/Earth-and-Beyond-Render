using System;
using UnityEngine;

namespace W3DImporter.Builders
{
    [ExecuteAlways]
    public sealed class W3DAnimatedTexture : MonoBehaviour
    {
        [Serializable]
        public sealed class Slot
        {
            public int MaterialIndex;
            public string TexturePropertyName = "";
            public bool IsMainTextureProperty = true;
            public Texture[] Frames;
            public ushort AnimType;
            public float FrameRate;
        }

        public Slot[] Slots = Array.Empty<Slot>();

        private Renderer _renderer;
        private Material[] _materials;
        private int[] _lastFrames;
        private float _startTime;
        private string _objectName = "";

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnEnable()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.update -= EditorUpdate;
                UnityEditor.EditorApplication.update += EditorUpdate;
            }
#endif
            EnsureInitialized();
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorUpdate;
#endif
        }

        private void Update()
        {
            if (Application.isPlaying)
                TickAnimation();
        }

        private void TickAnimation()
        {
            if (!EnsureInitialized())
                return;

            ApplyFrames(GetAnimationTime() - _startTime, false);
        }

        private bool EnsureInitialized()
        {
            if (_renderer == null)
                _renderer = GetComponent<Renderer>();

            _objectName = gameObject != null ? gameObject.name : "<unknown>";
            if (_renderer == null || Slots == null || Slots.Length == 0)
                return false;

            if (_materials != null && _lastFrames != null && _lastFrames.Length == Slots.Length)
                return true;

            _materials = _renderer.materials;
            if (_materials == null || _materials.Length == 0)
                return false;

            _lastFrames = new int[Slots.Length];
            for (int i = 0; i < _lastFrames.Length; i++)
            {
                _lastFrames[i] = -1;
                var slot = Slots[i];
                int frameCount = slot?.Frames?.Length ?? 0;
                Debug.Log($"[W3D AnimTex] '{_objectName}': slot={i} mat={slot?.MaterialIndex ?? -1} animType={slot?.AnimType ?? 0} fps={slot?.FrameRate ?? 0f} frameCount={frameCount}");
            }

            _startTime = GetAnimationTime();
            Debug.Log($"[W3D AnimTex] '{_objectName}': starting at t={_startTime:F3}");
            ApplyFrames(0f, true);
            return true;
        }

#if UNITY_EDITOR
        private void EditorUpdate()
        {
            if (Application.isPlaying || this == null || !isActiveAndEnabled)
                return;

            TickAnimation();
            UnityEditor.SceneView.RepaintAll();
        }
#endif

        private void ApplyFrames(float timeSeconds, bool force)
        {
            for (int i = 0; i < Slots.Length; i++)
            {
                var slot = Slots[i];
                int frame = EvaluateFrame(slot, timeSeconds);
                if (frame < 0)
                    continue;

                if (!force && _lastFrames[i] == frame)
                    continue;

                _lastFrames[i] = frame;

                if (slot.MaterialIndex < 0 || slot.MaterialIndex >= _materials.Length)
                {
                    Debug.LogWarning($"[W3D AnimTex] '{_objectName}': slot={i} invalid material index {slot.MaterialIndex} for materialCount={_materials.Length}");
                    continue;
                }

                var material = _materials[slot.MaterialIndex];
                if (material == null)
                {
                    Debug.LogWarning($"[W3D AnimTex] '{_objectName}': slot={i} material is null");
                    continue;
                }

                var texture = slot.Frames[frame];
                if (texture == null)
                {
                    Debug.LogWarning($"[W3D AnimTex] '{_objectName}': slot={i} frame={frame} texture is null");
                    continue;
                }

                string texturePropertyName = GetTexturePropertyName(slot);
                if (!string.IsNullOrWhiteSpace(texturePropertyName) && material.HasProperty(texturePropertyName))
                    material.SetTexture(texturePropertyName, texture);

                if (slot.IsMainTextureProperty)
                    material.mainTexture = texture;
                Debug.Log($"[W3D AnimTex] '{_objectName}': slot={i} time={timeSeconds:F3} frame={frame} texture='{texture.name}' material='{material.name}'");
            }
        }

        private static string GetTexturePropertyName(Slot slot)
        {
            return slot != null && !string.IsNullOrWhiteSpace(slot.TexturePropertyName)
                ? slot.TexturePropertyName
                : "_MainTex";
        }

        private static int EvaluateFrame(Slot slot, float timeSeconds)
        {
            if (slot?.Frames == null || slot.Frames.Length == 0)
                return -1;

            int frameCount = slot.Frames.Length;
            if (frameCount == 1)
                return 0;

            float frameRate = slot.FrameRate;
            if (Mathf.Abs(frameRate) <= 0.001f)
                return 0;

            bool reverse = frameRate < 0f;
            int frame = Mathf.FloorToInt(timeSeconds * Mathf.Abs(frameRate));

            switch (slot.AnimType)
            {
                case 1: // W3DTEXTURE_ANIM_PINGPONG
                {
                    int span = (frameCount * 2) - 2;
                    if (span <= 0)
                        return 0;

                    frame %= span;
                    if (frame >= frameCount)
                        frame = span - frame;
                    frame = Mathf.Clamp(frame, 0, frameCount - 1);
                    return reverse ? (frameCount - 1) - frame : frame;
                }

                case 2: // W3DTEXTURE_ANIM_ONCE
                {
                    frame = Mathf.Clamp(frame, 0, frameCount - 1);
                    return reverse ? (frameCount - 1) - frame : frame;
                }

                case 3: // W3DTEXTURE_ANIM_MANUAL
                    return 0;

                default: // loop
                {
                    frame = Mathf.Abs(frame % frameCount);
                    return reverse ? (frameCount - 1) - frame : frame;
                }
            }
        }

        private static float GetAnimationTime()
        {
            return Time.realtimeSinceStartup;
        }
    }
}
