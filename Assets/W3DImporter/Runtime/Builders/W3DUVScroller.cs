using UnityEngine;

namespace W3DImporter.Builders
{
    [ExecuteAlways]
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

        private void OnEnable()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.update -= EditorUpdate;
                UnityEditor.EditorApplication.update += EditorUpdate;
            }
#endif
            if (_renderer == null)
                _renderer = GetComponent<Renderer>();
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
                Tick();
        }

        private void Tick()
        {
            if (_renderer == null || CycleTime <= 0.0001f)
                return;

            var materials = _renderer.materials;
            if (materials == null || materials.Length == 0)
                return;

            float range = EndV - StartV;
            float v = StartV + Mathf.Repeat(Time.realtimeSinceStartup / CycleTime, 1f) * range;
            var offset = new Vector2(0f, v);

            for (int i = 0; i < materials.Length; i++)
            {
                var mat = materials[i];
                if (mat == null)
                    continue;

                if (mat.HasProperty("_UnlitColorMap"))
                    mat.SetTextureOffset("_UnlitColorMap", offset);
                if (mat.HasProperty("_BaseColorMap"))
                    mat.SetTextureOffset("_BaseColorMap", offset);
                if (mat.HasProperty("_MainTex"))
                    mat.SetTextureOffset("_MainTex", offset);
                if (mat.HasProperty("_BaseMap"))
                    mat.SetTextureOffset("_BaseMap", offset);
            }
        }

#if UNITY_EDITOR
        private void EditorUpdate()
        {
            if (Application.isPlaying || this == null || !isActiveAndEnabled)
                return;

            if (_renderer == null)
                _renderer = GetComponent<Renderer>();

            Tick();
            UnityEditor.SceneView.RepaintAll();
        }
#endif
    }
}
