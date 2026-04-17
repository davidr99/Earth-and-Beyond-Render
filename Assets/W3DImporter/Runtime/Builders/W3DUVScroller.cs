using UnityEngine;

namespace W3DImporter.Builders
{
    public sealed class W3DUVScroller : MonoBehaviour
    {
        public float CycleTime = 1f;
        public float StartV;
        public float EndV = 1f;

        private Renderer _renderer;

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
        }

        private void Update()
        {
            if (_renderer == null || CycleTime <= 0.0001f)
                return;

            var materials = _renderer.materials;
            if (materials == null || materials.Length == 0)
                return;

            float range = EndV - StartV;
            float v = StartV + Mathf.Repeat(Time.time / CycleTime, 1f) * range;
            var offset = new Vector2(0f, v);

            for (int i = 0; i < materials.Length; i++)
            {
                var mat = materials[i];
                if (mat == null)
                    continue;

                if (mat.HasProperty("_MainTex"))
                    mat.SetTextureOffset("_MainTex", offset);
                if (mat.HasProperty("_BaseMap"))
                    mat.SetTextureOffset("_BaseMap", offset);
            }
        }
    }
}
