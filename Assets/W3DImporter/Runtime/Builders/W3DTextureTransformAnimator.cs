using System;
using UnityEngine;

namespace W3DImporter.Builders
{
    [ExecuteAlways]
    public sealed class W3DTextureTransformAnimator : MonoBehaviour
    {
        [Serializable]
        public sealed class Slot
        {
            public int MaterialIndex;
            public string TexturePropertyName = "";
            public string Label = "";
            public W3DTextureAnimationKind Kind;

            public int GridWidth = 1;
            public int FrameCount = 1;
            public float FramesPerSecond;
            public int FrameOffset;

            public float UScale = 1f;
            public float VScale = 1f;
            public float UPerSec;
            public float VPerSec;

            public float UAmplitude;
            public float UFrequency = 1f;
            public float UPhase;
            public float VAmplitude;
            public float VFrequency = 1f;
            public float VPhase;

            public float StepU;
            public float StepV;
            public float StepsPerSecond;

            public float PeriodSeconds;
        }

        public Slot[] Slots = Array.Empty<Slot>();

        private Renderer _renderer;
        private Material[] _materials;
        private Vector2[] _baseOffsets;
        private Vector2[] _baseScales;
        private int[] _lastFrames;
        private float _startTime;

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

            Apply(Time.realtimeSinceStartup - _startTime, false);
        }

        private bool EnsureInitialized()
        {
            if (_renderer == null)
                _renderer = GetComponent<Renderer>();
            if (_renderer == null || Slots == null || Slots.Length == 0)
                return false;

            if (_materials != null &&
                _baseOffsets != null &&
                _baseScales != null &&
                _lastFrames != null &&
                _lastFrames.Length == Slots.Length)
            {
                return true;
            }

            _materials = _renderer.materials;
            if (_materials == null || _materials.Length == 0)
                return false;

            _baseOffsets = new Vector2[Slots.Length];
            _baseScales = new Vector2[Slots.Length];
            _lastFrames = new int[Slots.Length];

            for (int i = 0; i < Slots.Length; i++)
            {
                _lastFrames[i] = int.MinValue;
                CaptureBaseTransform(i);
            }

            _startTime = Time.realtimeSinceStartup;
            Apply(0f, true);
            return true;
        }

        private void Apply(float timeSeconds, bool force)
        {
            for (int i = 0; i < Slots.Length; i++)
            {
                var slot = Slots[i];
                if (slot == null)
                    continue;

                if (slot.MaterialIndex < 0 || slot.MaterialIndex >= _materials.Length)
                    continue;

                var material = _materials[slot.MaterialIndex];
                if (material == null)
                    continue;

                Vector2 offset;
                Vector2 scale;
                int frameKey;

                if (!Evaluate(slot, timeSeconds, _baseOffsets[i], _baseScales[i], out offset, out scale, out frameKey))
                    continue;

                if (!force && slot.Kind == W3DTextureAnimationKind.GridAtlas && _lastFrames[i] == frameKey)
                    continue;

                _lastFrames[i] = frameKey;
                ApplyTextureTransform(material, slot.TexturePropertyName, offset, scale);
            }
        }

        private bool Evaluate(
            Slot slot,
            float timeSeconds,
            Vector2 baseOffset,
            Vector2 baseScale,
            out Vector2 offset,
            out Vector2 scale,
            out int frameKey)
        {
            offset = baseOffset;
            scale = new Vector2(baseScale.x * slot.UScale, baseScale.y * slot.VScale);
            frameKey = 0;

            switch (slot.Kind)
            {
                case W3DTextureAnimationKind.GridAtlas:
                    return EvaluateGrid(slot, timeSeconds, baseOffset, baseScale, out offset, out scale, out frameKey);

                case W3DTextureAnimationKind.LinearOffset:
                    offset = new Vector2(
                        Wrap01(baseOffset.x - (slot.UPerSec * timeSeconds)),
                        Wrap01(baseOffset.y - (slot.VPerSec * timeSeconds)));
                    return true;

                case W3DTextureAnimationKind.SineLinearOffset:
                {
                    float angle = timeSeconds * Mathf.PI * 2f;
                    offset = new Vector2(
                        Wrap01(baseOffset.x + slot.UAmplitude * Mathf.Sin(slot.UFrequency * angle + slot.UPhase * Mathf.PI)),
                        Wrap01(baseOffset.y + slot.VAmplitude * Mathf.Sin(slot.VFrequency * angle + slot.VPhase * Mathf.PI)));
                    return true;
                }

                case W3DTextureAnimationKind.StepLinearOffset:
                {
                    int stepCount = slot.StepsPerSecond <= 0f
                        ? 0
                        : Mathf.FloorToInt(timeSeconds * slot.StepsPerSecond);

                    offset = new Vector2(
                        Wrap01(baseOffset.x + slot.StepU * stepCount),
                        Wrap01(baseOffset.y + slot.StepV * stepCount));
                    frameKey = stepCount;
                    return true;
                }

                case W3DTextureAnimationKind.ZigZagLinearOffset:
                {
                    float time = 0f;
                    if (slot.PeriodSeconds > 0.0001f)
                    {
                        float remainder = Mathf.Repeat(timeSeconds, slot.PeriodSeconds);
                        float half = slot.PeriodSeconds * 0.5f;
                        time = remainder > half ? slot.PeriodSeconds - remainder : remainder;
                    }

                    offset = new Vector2(
                        Wrap01(baseOffset.x + slot.UPerSec * time),
                        Wrap01(baseOffset.y + slot.VPerSec * time));
                    return true;
                }
            }

            return false;
        }

        private static bool EvaluateGrid(
            Slot slot,
            float timeSeconds,
            Vector2 baseOffset,
            Vector2 baseScale,
            out Vector2 offset,
            out Vector2 scale,
            out int frameKey)
        {
            offset = baseOffset;
            scale = baseScale;
            frameKey = 0;

            int gridWidth = Mathf.Max(1, slot.GridWidth);
            int frameCount = Mathf.Max(1, slot.FrameCount);

            int frame = Mathf.Max(0, slot.FrameOffset);
            if (Mathf.Abs(slot.FramesPerSecond) > 0.0001f && frameCount > 1)
            {
                int relativeFrame = Mathf.FloorToInt(timeSeconds * Mathf.Abs(slot.FramesPerSecond));
                frame = (relativeFrame + Mathf.Max(0, slot.FrameOffset)) % frameCount;
                if (slot.FramesPerSecond < 0f)
                    frame = (frameCount - 1) - frame;
            }

            frameKey = frame;

            float inv = 1f / gridWidth;
            int column = frame % gridWidth;
            int row = frame / gridWidth;

            // Match WW3D GridTextureMapperClass: preserve the authored UV scale and only
            // apply a per-frame atlas offset. The original runtime does not inject an
            // additional 1/grid scale in the texture matrix. The client's matrix adds
            // (column, row) / grid with rows counted from the texture's first row, which is
            // the direction of Unity's v here (raw DDS rows, flipped mesh v).
            scale = baseScale;
            offset = new Vector2(
                baseOffset.x + (baseScale.x < 0f ? -column * inv : column * inv),
                baseOffset.y + (baseScale.y < 0f ? -row * inv : row * inv));

            return true;
        }

        private void CaptureBaseTransform(int slotIndex)
        {
            if (_materials == null || slotIndex < 0 || slotIndex >= Slots.Length)
                return;

            var slot = Slots[slotIndex];
            if (slot == null || slot.MaterialIndex < 0 || slot.MaterialIndex >= _materials.Length)
                return;

            var material = _materials[slot.MaterialIndex];
            if (material == null)
                return;

            string texturePropertyName = GetTexturePropertyName(slot);
            _baseOffsets[slotIndex] = GetTextureOffset(material, texturePropertyName);
            _baseScales[slotIndex] = GetTextureScale(material, texturePropertyName);
        }

        private static float Wrap01(float value)
        {
            return value - Mathf.Floor(value);
        }

        private static string GetTexturePropertyName(Slot slot)
        {
            return slot != null && !string.IsNullOrWhiteSpace(slot.TexturePropertyName)
                ? slot.TexturePropertyName
                : "_MainTex";
        }

        private static Vector2 GetTextureOffset(Material material, string texturePropertyName)
        {
            if (!string.IsNullOrWhiteSpace(texturePropertyName) && material.HasProperty(texturePropertyName))
                return material.GetTextureOffset(texturePropertyName);
            return Vector2.zero;
        }

        private static Vector2 GetTextureScale(Material material, string texturePropertyName)
        {
            if (!string.IsNullOrWhiteSpace(texturePropertyName) && material.HasProperty(texturePropertyName))
                return material.GetTextureScale(texturePropertyName);
            return Vector2.one;
        }

        private static void ApplyTextureTransform(Material material, string texturePropertyName, Vector2 offset, Vector2 scale)
        {
            if (!string.IsNullOrWhiteSpace(texturePropertyName) && material.HasProperty(texturePropertyName))
            {
                material.SetTextureOffset(texturePropertyName, offset);
                material.SetTextureScale(texturePropertyName, scale);
            }
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
    }
}
