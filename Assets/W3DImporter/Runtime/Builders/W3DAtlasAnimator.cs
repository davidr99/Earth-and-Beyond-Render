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
        }

        public Slot[] Slots = Array.Empty<Slot>();

        private Renderer _renderer;
        private Material[] _materials;
        private int[] _lastFrames;
        private float _startTime;

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            if (_renderer == null || Slots == null || Slots.Length == 0)
                return;

            _materials = _renderer.materials;
            if (_materials == null || _materials.Length == 0)
                return;

            _lastFrames = new int[Slots.Length];
            for (int i = 0; i < _lastFrames.Length; i++)
                _lastFrames[i] = -1;

            _startTime = GetAnimationTime();
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
                int frame = Mathf.FloorToInt(timeSeconds * fps) % frameCount;
                if (slot.FramesPerSecond < 0f && frameCount > 1)
                    frame = (frameCount - 1) - frame;

                if (!force && _lastFrames[i] == frame)
                    continue;

                _lastFrames[i] = frame;
                ApplyFrame(slot, frame);
            }
        }

        private void ApplyFrame(Slot slot, int frame)
        {
            if (_materials == null || slot.MaterialIndex < 0 || slot.MaterialIndex >= _materials.Length)
                return;

            var material = _materials[slot.MaterialIndex];
            if (material == null)
                return;

            int gridWidth = Mathf.Max(1, slot.GridWidth);
            float inv = 1f / gridWidth;
            int col = frame % gridWidth;
            int row = frame / gridWidth;

            float uOffset = col * inv;
            float vOffset = -row * inv;
            var offset = new Vector2(uOffset, vOffset);

            if (material.HasProperty("_UnlitColorMap"))
                material.SetTextureOffset("_UnlitColorMap", offset);
            if (material.HasProperty("_BaseColorMap"))
                material.SetTextureOffset("_BaseColorMap", offset);
            if (material.HasProperty("_BaseMap"))
                material.SetTextureOffset("_BaseMap", offset);
            if (material.HasProperty("_MainTex"))
                material.SetTextureOffset("_MainTex", offset);
        }

        private static float GetAnimationTime()
        {
            return Time.realtimeSinceStartup;
        }
    }
}
