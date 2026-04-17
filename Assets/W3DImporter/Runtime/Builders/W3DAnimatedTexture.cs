using System;
using UnityEngine;

namespace W3DImporter.Builders
{
    public sealed class W3DAnimatedTexture : MonoBehaviour
    {
        [Serializable]
        public sealed class Slot
        {
            public int MaterialIndex;
            public Texture[] Frames;
            public ushort AnimType;
            public float FrameRate;
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
            _lastFrames = new int[Slots.Length];
            for (int i = 0; i < _lastFrames.Length; i++)
                _lastFrames[i] = -1;

            _startTime = GetAnimationTime();
            ApplyFrames(0f, true);
        }

        private void Update()
        {
            if (_materials == null || Slots == null || Slots.Length == 0)
                return;

            ApplyFrames(GetAnimationTime() - _startTime, false);
        }

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
                    continue;

                var material = _materials[slot.MaterialIndex];
                if (material == null)
                    continue;

                var texture = slot.Frames[frame];
                if (texture == null)
                    continue;

                if (material.HasProperty("_MainTex"))
                    material.SetTexture("_MainTex", texture);
                if (material.HasProperty("_BaseMap"))
                    material.SetTexture("_BaseMap", texture);
                if (material.HasProperty("_UnlitColorMap"))
                    material.SetTexture("_UnlitColorMap", texture);

                material.mainTexture = texture;
            }
        }

        private static int EvaluateFrame(Slot slot, float timeSeconds)
        {
            if (slot?.Frames == null || slot.Frames.Length == 0)
                return -1;

            int frameCount = slot.Frames.Length;
            if (frameCount == 1)
                return 0;

            float frameRate = slot.FrameRate > 0.001f ? slot.FrameRate : 8f;
            int frame = Mathf.FloorToInt(timeSeconds * frameRate);

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
                    return Mathf.Clamp(frame, 0, frameCount - 1);
                }

                case 2: // W3DTEXTURE_ANIM_ONCE
                    return Mathf.Clamp(frame, 0, frameCount - 1);

                case 3: // W3DTEXTURE_ANIM_MANUAL
                    return 0;

                default: // loop
                    return Mathf.Abs(frame % frameCount);
            }
        }

        private static float GetAnimationTime()
        {
            return Time.realtimeSinceStartup;
        }
    }
}
