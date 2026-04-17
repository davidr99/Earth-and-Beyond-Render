using UnityEngine;
using System.IO;
using W3DImporter.Parsers;
using W3DImporter.Builders;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace W3DImporter
{
    public class W3DViewerUI : MonoBehaviour
    {
        private string _filePath = "";
        private GameObject _loadedModel;
        private float _rotationY;
        private float _rotationX = 30f;
        private float _distance = 10f;
        private string _statusMessage = "Ready. Enter a .w3d file path to load.";

        // Input state captured from OnGUI events (works with any input backend)
        private Vector2 _mousePos;
        private Vector2 _prevMousePos;
        private bool _rightMouseHeld;
        private float _scrollDelta;
        private bool _initialized;

        private void Update()
        {
            if (_loadedModel == null) return;

            if (_rightMouseHeld && _initialized)
            {
                var delta = _mousePos - _prevMousePos;
                _rotationY += delta.x * 0.3f;
                _rotationX -= delta.y * 0.3f;
                _rotationX = Mathf.Clamp(_rotationX, -80f, 80f);
            }

            _distance -= _scrollDelta * 2f;
            _distance = Mathf.Clamp(_distance, 0.5f, 200f);
            _scrollDelta = 0f;

            var bounds = CalculateBounds(_loadedModel);
            var center = bounds.center;
            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.position = center + Quaternion.Euler(_rotationX, _rotationY, 0) * new Vector3(0, 0, -_distance);
                cam.transform.LookAt(center);
            }
        }

        private void OnGUI()
        {
            // Capture all input from GUI events (works with both old and new Input System)
            var e = Event.current;
            _prevMousePos = _mousePos;
            _mousePos = e.mousePosition;
            _initialized = true;

            if (e.type == EventType.MouseDown && e.button == 1)
                _rightMouseHeld = true;
            if (e.type == EventType.MouseUp && e.button == 1)
                _rightMouseHeld = false;
            if (e.type == EventType.ScrollWheel)
                _scrollDelta = -e.delta.y;

            // UI
            GUILayout.BeginArea(new Rect(10, 10, 500, 220));
            GUILayout.BeginVertical("box");

            GUILayout.Label("W3D Viewer", GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("File:", GUILayout.Width(30));
            _filePath = GUILayout.TextField(_filePath);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Browse"))
                BrowseForFile();
            if (GUILayout.Button("Load"))
                LoadModel(_filePath);
            if (GUILayout.Button("Clear"))
                ClearModel();
            GUILayout.EndHorizontal();

            GUILayout.Label(_statusMessage);
            GUILayout.Label("Right-click drag to orbit, scroll to zoom");

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private void BrowseForFile()
        {
#if UNITY_EDITOR
            var initialDirectory = string.IsNullOrEmpty(_filePath)
                ? Application.dataPath
                : Path.GetDirectoryName(_filePath);

            if (string.IsNullOrEmpty(initialDirectory) || !Directory.Exists(initialDirectory))
                initialDirectory = Application.dataPath;

            var selected = EditorUtility.OpenFilePanel("Open W3D File", initialDirectory, "w3d");
            if (!string.IsNullOrEmpty(selected))
            {
                _filePath = selected;
                _statusMessage = $"Selected: {Path.GetFileName(selected)}";
            }
#else
            _statusMessage = "File picker is only available in the Unity Editor. Enter the full path manually in builds.";
#endif
        }

        private void LoadModel(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                _statusMessage = $"File not found: {path}";
                return;
            }

            ClearModel();
            EnsureLight();

            try
            {
                string baseDir = Path.GetDirectoryName(path);
                var context = W3DFileParser.Parse(path);
                var builder = new W3DSceneBuilder(baseDir);
                _loadedModel = builder.Build(context);

                _statusMessage = $"Loaded: {Path.GetFileName(path)} ({context.Meshes.Count} meshes, {context.Emitters.Count} emitters)";

                var bounds = CalculateBounds(_loadedModel);
                _distance = Mathf.Max(bounds.size.magnitude * 1.5f, 1f);
            }
            catch (System.Exception e)
            {
                _statusMessage = $"Error: {e.Message}";
                Debug.LogException(e);
            }
        }

        private void ClearModel()
        {
            if (_loadedModel != null)
            {
                Destroy(_loadedModel);
                _loadedModel = null;
                _statusMessage = "Cleared.";
            }
        }

        private void EnsureLight()
        {
            // Make sure the scene has a directional light
            if (FindAnyObjectByType<Light>() == null)
            {
                var lightGo = new GameObject("W3D_DirectionalLight");
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.0f;
                light.color = Color.white;
                lightGo.transform.rotation = Quaternion.Euler(50, -30, 0);
                Debug.Log("[W3D] Created directional light");
            }
        }

        private Bounds CalculateBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(go.transform.position, Vector3.one);

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }
    }
}
