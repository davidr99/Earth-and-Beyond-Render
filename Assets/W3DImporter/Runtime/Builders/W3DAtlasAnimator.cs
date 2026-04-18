using System;
using UnityEngine;

namespace W3DImporter.Builders
{
    public sealed class W3DAtlasAnimator : MonoBehaviour
    {
        [Serializable]
        public sealed class Slot
        {
            public int MaterialIndex;
            public int GridWidth;
            public int FrameCount;
            public float FramesPerSecond;
            public int FrameOffset;
        }

        public Slot[] Slots = Array.Empty<Slot>();

        private Renderer _renderer;
        private Material[] _materials;
        private int[] _lastFrames;
        private float _startTime;
        private Vector2[] _baseOffsets;
        private Vector2[] _baseScales;
        private string _objectName = "";

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _objectName = gameObject != null ? gameObject.name : "<unknown>";
            if (_renderer == null || Slots == null || Slots.Length == 0)
            {
                Debug.LogWarning($"[W3D Atlas] '{_objectName}': animator awake without renderer/slots");
                return;
            }

            _materials = _renderer.materials;
            if (_materials == null || _materials.Length == 0)
            {
                Debug.LogWarning($"[W3D Atlas] '{_objectName}': renderer has no materials");
                return;
            }

            _lastFrames = new int[Slots.Length];
            _baseOffsets = new Vector2[Slots.Length];
            _baseScales = new Vector2[Slots.Length];
            for (int i = 0; i < _lastFrames.Length; i++)
            {
                _lastFrames[i] = -1;
                CaptureBaseTransform(i);
                var slot = Slots[i];
                Debug.Log($"[W3D Atlas] '{_objectName}': slot={i} mat={slot?.MaterialIndex ?? -1} grid={slot?.GridWidth ?? 0} frames={slot?.FrameCount ?? 0} fps={slot?.FramesPerSecond ?? 0f} frameOffset={slot?.FrameOffset ?? 0} baseOffset={_baseOffsets[i]} baseScale={_baseScales[i]}");
            }

            _startTime = GetAnimationTime();
            Debug.Log($"[W3D Atlas] '{_objectName}': starting at t={_startTime:F3}");
            Apply(0f, true);
        }

        private void Update()
        {
            if (_materials == null || Slots == null || Slots.Length == 0)
                return;

            Apply(GetAnimationTime() - _startTime, false);
        }

        private void Apply(float timeSeconds, bool force)
        {
            for (int i = 0; i < Slots.Length; i++)
            {
                var slot = Slots[i];
                if (slot == null || slot.GridWidth <= 0)
                    continue;

                int frameCount = Mathf.Max(1, slot.FrameCount);
                float fps = Mathf.Abs(slot.FramesPerSecond) > 0.001f ? Mathf.Abs(slot.FramesPerSecond) : 1f;
                int frame = (Mathf.FloorToInt(timeSeconds * fps) + Mathf.Max(0, slot.FrameOffset)) % frameCount;
                if (slot.FramesPerSecond < 0f && frameCount > 1)
                    frame = (frameCount - 1) - frame;

                if (!force && _lastFrames[i] == frame)
                    continue;

                _lastFrames[i] = frame;
                ApplyFrame(i, slot, frame);
            }
        }

        private void ApplyFrame(int slotIndex, Slot slot, int frame)
        {
            if (_materials == null || slot.MaterialIndex < 0 || slot.MaterialIndex >= _materials.Length)
            {
                Debug.LogWarning($"[W3D Atlas] '{_objectName}': slot={slotIndex} invalid material index {slot?.MaterialIndex ?? -1} for materialCount={_materials?.Length ?? 0}");
                return;
            }

            var material = _materials[slot.MaterialIndex];
            if (material == null)
            {
                Debug.LogWarning($"[W3D Atlas] '{_objectName}': slot={slotIndex} material is null");
                return;
            }

            int gridWidth = Mathf.Max(1, slot.GridWidth);
            float inv = 1f / gridWidth;
            int col = frame % gridWidth;
            int row = frame / gridWidth;

            Vector2 baseScale = _baseScales != null && _baseScales.Length > slotIndex
                ? _baseScales[slotIndex]
                : Vector2.one;
            Vector2 baseOffset = _baseOffsets != null && _baseOffsets.Length > slotIndex
                ? _baseOffsets[slotIndex]
                : Vector2.zero;

            float scaleX = Mathf.Abs(baseScale.x) * inv;
            float scaleY = Mathf.Abs(baseScale.y) * inv;

            var scale = new Vector2(
                Mathf.Sign(baseScale.x == 0f ? 1f : baseScale.x) * scaleX,
                Mathf.Sign(baseScale.y == 0f ? 1f : baseScale.y) * scaleY);

            float uOffset = baseOffset.x + (baseScale.x < 0f ? -col * scaleX : col * scaleX);
            float vOffset = baseOffset.y - row * scaleY;
            var offset = new Vector2(uOffset, vOffset);

            ApplyTextureTransform(material, offset, scale);
            Debug.Log($"[W3D Atlas] '{_objectName}': slot={slotIndex} frame={frame} row={row} col={col} offset={offset} scale={scale} material='{material.name}'");
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

            _baseOffsets[slotIndex] = GetTextureOffset(material);
            _baseScales[slotIndex] = GetTextureScale(material);
        }

        private static Vector2 GetTextureOffset(Material material)
        {
            if (material.HasProperty("_UnlitColorMap"))
                return material.GetTextureOffset("_UnlitColorMap");
            if (material.HasProperty("_BaseColorMap"))
                return material.GetTextureOffset("_BaseColorMap");
            if (material.HasProperty("_BaseMap"))
                return material.GetTextureOffset("_BaseMap");
            if (material.HasProperty("_MainTex"))
                return material.GetTextureOffset("_MainTex");
            return Vector2.zero;
        }

        private static Vector2 GetTextureScale(Material material)
        {
            if (material.HasProperty("_UnlitColorMap"))
                return material.GetTextureScale("_UnlitColorMap");
            if (material.HasProperty("_BaseColorMap"))
                return material.GetTextureScale("_BaseColorMap");
            if (material.HasProperty("_BaseMap"))
                return material.GetTextureScale("_BaseMap");
            if (material.HasProperty("_MainTex"))
                return material.GetTextureScale("_MainTex");
            return Vector2.one;
        }

        private static void ApplyTextureTransform(Material material, Vector2 offset, Vector2 scale)
        {
            if (material.HasProperty("_UnlitColorMap"))
            {
                material.SetTextureOffset("_UnlitColorMap", offset);
                material.SetTextureScale("_UnlitColorMap", scale);
            }
            if (material.HasProperty("_BaseColorMap"))
            {
                material.SetTextureOffset("_BaseColorMap", offset);
                material.SetTextureScale("_BaseColorMap", scale);
            }
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTextureOffset("_BaseMap", offset);
                material.SetTextureScale("_BaseMap", scale);
            }
            if (material.HasProperty("_MainTex"))
            {
                material.SetTextureOffset("_MainTex", offset);
                material.SetTextureScale("_MainTex", scale);
            }
        }

        private static float GetAnimationTime()
        {
            return Time.realtimeSinceStartup;
        }
    }
}
