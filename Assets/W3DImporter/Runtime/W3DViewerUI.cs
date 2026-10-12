using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using W3DImporter.Parsers;
using W3DImporter.Builders;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace W3DImporter
{
    public class W3DViewerUI : MonoBehaviour
    {
        private const string FilePathControlName = "W3DViewerFilePath";
        private const string BaseDirectionalLightName = "W3D_DirectionalLight";
        private const float BuiltInViewerLightIntensity = 3.5f;
        private static readonly Rect UiRect = new Rect(10, 10, 560, 430);
        private static readonly Color ViewerBackgroundColor = new Color(0.025f, 0.07f, 0.19f, 1f);
        private static readonly Color ViewerAmbientLightColor = new Color(0.40f, 0.43f, 0.48f, 1f);
        private static readonly Vector3 FallbackViewerShaderLightDirection = new Vector3(0.35f, 0.85f, 0.35f).normalized;
        private const float FallbackViewerShaderLightIntensity = 0.9f;
        private static readonly int W3DViewerAmbientColorId = Shader.PropertyToID("_W3DViewerAmbientColor");
        private static readonly int W3DViewerLightDirectionId = Shader.PropertyToID("_W3DViewerLightDirection");
        private static readonly int W3DViewerLightColorId = Shader.PropertyToID("_W3DViewerLightColor");
        private static readonly int W3DViewerLightIntensityId = Shader.PropertyToID("_W3DViewerLightIntensity");
        private static readonly Type HdAdditionalCameraDataType = ResolveHdrpCameraDataType();
        private static readonly FieldInfo HdVolumeLayerMaskField = HdAdditionalCameraDataType?.GetField("volumeLayerMask", BindingFlags.Instance | BindingFlags.Public);
        private static readonly FieldInfo HdVolumeAnchorOverrideField = HdAdditionalCameraDataType?.GetField("volumeAnchorOverride", BindingFlags.Instance | BindingFlags.Public);
        private static readonly FieldInfo HdClearColorModeField = HdAdditionalCameraDataType?.GetField("clearColorMode", BindingFlags.Instance | BindingFlags.Public);
        private static readonly FieldInfo HdBackgroundColorHDRField = HdAdditionalCameraDataType?.GetField("backgroundColorHDR", BindingFlags.Instance | BindingFlags.Public);

        private enum ViewerCameraMode
        {
            Orbit,
            Fly
        }

        private string _filePath = "";
        private GameObject _loadedModel;
        private float _rotationY;
        private float _rotationX = 30f;
        private float _distance = 10f;
        private float _maxOrbitDistance = 200f;
        private Vector3 _orbitTarget;
        private ViewerCameraMode _cameraMode = ViewerCameraMode.Orbit;
        private Vector3 _flyPosition;
        private float _flyYaw;
        private float _flyPitch;
        private float _flyMoveSpeed = 8f;
        private Camera _viewerCamera;
        private Camera _presentationCamera;
        private CameraClearFlags _savedClearFlags;
        private Color _savedBackgroundColor;
        private Skybox _savedSkybox;
        private bool _savedSkyboxEnabled;
        private bool _savedRenderFog;
        private bool _savedRenderFogCaptured;
        private AmbientMode _savedAmbientMode;
        private Color _savedAmbientLight;
        private float _savedAmbientIntensity;
        private bool _savedAmbientLightingCaptured;
        private Component _savedHdCameraData;
        private LayerMask _savedHdVolumeLayerMask;
        private Transform _savedHdVolumeAnchorOverride;
        private bool _savedHdCameraVolumeCaptured;
        private bool _addedHdCameraData;
        private bool _hideTransparentMeshes;
        private bool _useSceneLightForPreview;
        private float _viewerLightAmbient = 0.18f;
        private float _viewerLightIntensity = 1.1f;
        private float _viewerLightYaw = -30f;
        private float _viewerLightPitch = 50f;
        private int _lastLightingMaterialCount;
        private MaterialPropertyBlock _viewerLightingProperties;
        private string _statusMessage = "Ready. Enter a .w3d file path to load.";

#if UNITY_EDITOR
        private SceneView _presentationSceneView;
        private bool _savedSceneViewFogEnabled;
        private bool _savedSceneViewSkyboxEnabled;
        private bool _savedSceneViewStateCaptured;
#endif

        // Runtime input state polled each frame so camera controls do not depend on IMGUI events.
        private Vector2 _lookDelta;
        private bool _rightMouseHeld;
        private float _scrollDelta;

        private static Type ResolveHdrpCameraDataType()
        {
            const string typeName = "UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData";
            var type = Type.GetType(typeName + ", Unity.RenderPipelines.HighDefinition.Runtime");
            if (type != null)
                return type;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName);
                if (type != null)
                    return type;
            }

            return null;
        }

        private void Update()
        {
            CaptureRuntimeInput();
            if (_loadedModel == null) return;

            var bounds = CalculateBounds(_loadedModel);
            _orbitTarget = bounds.center;

            var cam = ResolveViewerCamera();
            if (cam == null)
                return;

            ApplyViewerCameraOverrides(cam);
            ConfigureCameraClipPlanes(cam, bounds);

#if UNITY_EDITOR
            ApplyEditorSceneViewOverrides();
#endif

            if (_cameraMode == ViewerCameraMode.Fly)
            {
                UpdateFlyCamera(cam);
                return;
            }

            UpdateOrbitCamera(cam);
        }

        private void UpdateOrbitCamera(Camera cam)
        {
            if (_rightMouseHeld)
            {
                _rotationY += _lookDelta.x * 0.15f;
                _rotationX -= _lookDelta.y * 0.15f;
                _rotationX = Mathf.Clamp(_rotationX, -80f, 80f);
            }

            _distance -= _scrollDelta * Mathf.Max(_distance * 0.1f, 0.1f);
            _distance = Mathf.Clamp(_distance, 0.5f, _maxOrbitDistance);
            _scrollDelta = 0f;

            cam.transform.position = _orbitTarget + Quaternion.Euler(_rotationX, _rotationY, 0f) * new Vector3(0f, 0f, -_distance);
            cam.transform.LookAt(_orbitTarget);
        }

        private void UpdateFlyCamera(Camera cam)
        {
            if (_rightMouseHeld)
            {
                _flyYaw += _lookDelta.x * 0.12f;
                _flyPitch -= _lookDelta.y * 0.12f;
                _flyPitch = Mathf.Clamp(_flyPitch, -89f, 89f);
            }

            if (Mathf.Abs(_scrollDelta) > Mathf.Epsilon)
            {
                _flyMoveSpeed = Mathf.Clamp(_flyMoveSpeed + (_scrollDelta * 0.4f), 1f, 60f);
                _scrollDelta = 0f;
            }

            var lookRotation = Quaternion.Euler(_flyPitch, _flyYaw, 0f);
            var localMove = Vector3.zero;
            if (IsKeyHeld(KeyCode.W)) localMove += Vector3.forward;
            if (IsKeyHeld(KeyCode.S)) localMove += Vector3.back;
            if (IsKeyHeld(KeyCode.A)) localMove += Vector3.left;
            if (IsKeyHeld(KeyCode.D)) localMove += Vector3.right;
            if (IsKeyHeld(KeyCode.E)) localMove += Vector3.up;
            if (IsKeyHeld(KeyCode.Q)) localMove += Vector3.down;

            if (localMove.sqrMagnitude > 0f)
            {
                float speed = _flyMoveSpeed;
                if (IsKeyHeld(KeyCode.LeftShift) || IsKeyHeld(KeyCode.RightShift))
                    speed *= 3f;

                _flyPosition += lookRotation * localMove.normalized * speed * Time.deltaTime;
            }

            cam.transform.SetPositionAndRotation(_flyPosition, lookRotation);
        }

        private void OnGUI()
        {
            var e = Event.current;

            // UI
            GUILayout.BeginArea(UiRect);
            GUILayout.BeginVertical("box");

            GUILayout.Label("W3D Viewer", GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("File:", GUILayout.Width(30));
            GUI.SetNextControlName(FilePathControlName);
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

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Orbit"))
                SetCameraMode(ViewerCameraMode.Orbit);
            if (GUILayout.Button("Fly"))
                SetCameraMode(ViewerCameraMode.Fly);
            if (GUILayout.Button("Frame"))
                FrameLoadedModel();
            GUILayout.EndHorizontal();

            bool hideTransparentMeshes = GUILayout.Toggle(_hideTransparentMeshes, "Hide Transparent Meshes");
            if (hideTransparentMeshes != _hideTransparentMeshes)
            {
                _hideTransparentMeshes = hideTransparentMeshes;
                ApplyVisibilityFilters();
            }

            bool useUpscaled = GUILayout.Toggle(W3DTextureResolver.UseUpscaledPngs, "Use Upscaled Textures (PNG)");
            if (useUpscaled != W3DTextureResolver.UseUpscaledPngs)
            {
                W3DTextureResolver.UseUpscaledPngs = useUpscaled;
                if (_loadedModel != null && File.Exists(_filePath))
                    LoadModel(_filePath);   // textures are cached per load; rebuild with the new preference
            }

            DrawLightingControls();

            GUILayout.Label(_statusMessage);
            GUILayout.Label(_cameraMode == ViewerCameraMode.Orbit
                ? "Orbit: right-drag rotate, scroll zoom"
                : $"Fly: right-drag look, WASD move, Q/E rise, Shift boost, scroll speed ({_flyMoveSpeed:0.0})");

            GUILayout.EndVertical();
            GUILayout.EndArea();

            HandleHotkeys(e);
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
                var loadTimer = System.Diagnostics.Stopwatch.StartNew();
                string baseDir = Path.GetDirectoryName(path);
                long parseStartMs = loadTimer.ElapsedMilliseconds;
                var context = W3DFileParser.Parse(path);
                long parseElapsedMs = loadTimer.ElapsedMilliseconds - parseStartMs;
                long buildStartMs = loadTimer.ElapsedMilliseconds;
                var builder = new W3DSceneBuilder(baseDir);
                _loadedModel = builder.Build(context);
                long buildElapsedMs = loadTimer.ElapsedMilliseconds - buildStartMs;
                loadTimer.Stop();

                _statusMessage = $"Loaded: {Path.GetFileName(path)} ({context.Meshes.Count} meshes, {context.Emitters.Count} emitters) in {loadTimer.ElapsedMilliseconds} ms [parse {parseElapsedMs} ms, build {buildElapsedMs} ms]";
                FrameLoadedModel();
                ApplyVisibilityFilters();
                ApplyLoadedModelLightingFlags();
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
                _rightMouseHeld = false;
                _statusMessage = "Cleared.";
            }

            RestoreViewerCameraOverrides();
        }

        private void EnsureLight()
        {
            if (FindAnyObjectByType<Light>() == null)
                CreateBaseDirectionalLight();
        }

        private static void CreateBaseDirectionalLight()
        {
            var lightGo = new GameObject(BaseDirectionalLightName);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = BuiltInViewerLightIntensity;
            light.color = Color.white;
            light.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Debug.Log("[W3D] Created directional light");
        }

        private void FrameLoadedModel()
        {
            if (_loadedModel == null)
                return;

            var bounds = CalculateBounds(_loadedModel);
            _orbitTarget = bounds.center;
            float radius = Mathf.Max(bounds.extents.magnitude, 1f);
            _distance = radius * 3f;
            _maxOrbitDistance = Mathf.Max(_distance * 4f, 200f);

            var cam = ResolveViewerCamera();
            if (cam == null)
                return;

            ApplyViewerCameraOverrides(cam);
            cam.transform.position = _orbitTarget + Quaternion.Euler(_rotationX, _rotationY, 0f) * new Vector3(0f, 0f, -_distance);
            cam.transform.LookAt(_orbitTarget);
            SyncFlyFromCamera(cam);
        }

        private void OnDisable()
        {
            RestoreViewerCameraOverrides();
        }

        private void OnDestroy()
        {
            RestoreViewerCameraOverrides();
        }

        private void SetCameraMode(ViewerCameraMode cameraMode)
        {
            if (_cameraMode == cameraMode)
                return;

            var cam = ResolveViewerCamera();
            if (cam != null)
            {
                if (cameraMode == ViewerCameraMode.Fly)
                    SyncFlyFromCamera(cam);
                else
                    SyncOrbitFromCamera(cam);
            }

            _cameraMode = cameraMode;
            _statusMessage = cameraMode == ViewerCameraMode.Fly
                ? "Fly camera enabled."
                : "Orbit camera enabled.";
        }

        private void SyncFlyFromCamera(Camera cam)
        {
            _flyPosition = cam.transform.position;
            var euler = cam.transform.rotation.eulerAngles;
            _flyYaw = euler.y;
            _flyPitch = NormalizeSignedAngle(euler.x);
        }

        private void SyncOrbitFromCamera(Camera cam)
        {
            var offset = cam.transform.position - _orbitTarget;
            if (offset.sqrMagnitude < 0.0001f)
                return;

            _distance = Mathf.Max(offset.magnitude, 0.5f);
            var direction = offset / _distance;
            _rotationX = Mathf.Clamp(Mathf.Asin(direction.y) * Mathf.Rad2Deg, -80f, 80f);
            _rotationY = Mathf.Atan2(direction.x, -direction.z) * Mathf.Rad2Deg;
        }

        private void HandleHotkeys(Event e)
        {
            if (e.type != EventType.KeyDown || e.keyCode == KeyCode.None)
                return;

            if (GUI.GetNameOfFocusedControl() == FilePathControlName)
                return;

            switch (e.keyCode)
            {
                case KeyCode.F2:
                    SetCameraMode(_cameraMode == ViewerCameraMode.Orbit ? ViewerCameraMode.Fly : ViewerCameraMode.Orbit);
                    e.Use();
                    return;
                case KeyCode.F:
                    FrameLoadedModel();
                    e.Use();
                    return;
                case KeyCode.Escape:
                    _rightMouseHeld = false;
                    e.Use();
                    return;
            }
        }

        private bool IsKeyHeld(KeyCode keyCode)
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return false;

            return keyCode switch
            {
                KeyCode.W => keyboard.wKey.isPressed,
                KeyCode.A => keyboard.aKey.isPressed,
                KeyCode.S => keyboard.sKey.isPressed,
                KeyCode.D => keyboard.dKey.isPressed,
                KeyCode.Q => keyboard.qKey.isPressed,
                KeyCode.E => keyboard.eKey.isPressed,
                KeyCode.LeftShift => keyboard.leftShiftKey.isPressed,
                KeyCode.RightShift => keyboard.rightShiftKey.isPressed,
                _ => false
            };
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(keyCode);
#else
            return false;
#endif
        }

        private void CaptureRuntimeInput()
        {
            _lookDelta = Vector2.zero;
            _scrollDelta = 0f;

#if ENABLE_INPUT_SYSTEM
            CaptureInputSystemState();
#elif ENABLE_LEGACY_INPUT_MANAGER
            CaptureLegacyInputState();
#else
            _rightMouseHeld = false;
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private void CaptureInputSystemState()
        {
            var mouse = Mouse.current;
            if (mouse == null)
            {
                _rightMouseHeld = false;
                return;
            }

            _scrollDelta = mouse.scroll.ReadValue().y / 120f;
            _rightMouseHeld = mouse.rightButton.isPressed && !IsPointerOverUi(mouse.position.ReadValue());
            if (_rightMouseHeld)
                _lookDelta = mouse.delta.ReadValue();
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        private void CaptureLegacyInputState()
        {
            _scrollDelta = Input.mouseScrollDelta.y;
            _rightMouseHeld = Input.GetMouseButton(1) && !IsPointerOverUi(Input.mousePosition);
            if (_rightMouseHeld)
                _lookDelta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 12f;
        }
#endif

        private static bool IsPointerOverUi(Vector2 screenPosition)
        {
            var guiPoint = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            return UiRect.Contains(guiPoint);
        }

        private static float NormalizeSignedAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f)
                angle -= 360f;
            return angle;
        }

        private void ApplyVisibilityFilters()
        {
            if (_loadedModel == null)
                return;

            var renderers = _loadedModel.GetComponentsInChildren<Renderer>(true);
            if (!_hideTransparentMeshes)
            {
                foreach (var renderer in renderers)
                    renderer.enabled = true;
                return;
            }

            int transparentCount = 0;
            foreach (var renderer in renderers)
            {
                if (IsTransparentRenderer(renderer))
                    transparentCount++;
            }

            bool wouldHideEverything = renderers.Length > 0 && transparentCount == renderers.Length;
            foreach (var renderer in renderers)
            {
                bool isTransparent = IsTransparentRenderer(renderer);
                renderer.enabled = wouldHideEverything || !isTransparent;
            }
        }

        private static bool IsTransparentRenderer(Renderer renderer)
        {
            var materials = renderer.sharedMaterials;
            bool sawMaterial = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var mat = materials[i];
                if (mat == null)
                    continue;

                sawMaterial = true;
                if (mat.renderQueue < 3000)
                    return false;

                var renderType = mat.GetTag("RenderType", false, "");
                if (!string.Equals(renderType, "Transparent", StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return sawMaterial;
        }

        private void DrawLightingControls()
        {
            GUILayout.Space(4);
            _useSceneLightForPreview = GUILayout.Toggle(_useSceneLightForPreview, "Use Scene Light For W3D Shading");

            if (_useSceneLightForPreview)
            {
                GUILayout.Label("W3D light follows the strongest active Unity light.");
                DrawLightingStatus();
                return;
            }

            _viewerLightAmbient = DrawSlider("Ambient", _viewerLightAmbient, 0f, 1f);
            _viewerLightIntensity = DrawSlider("Light", _viewerLightIntensity, 0f, 2f);
            _viewerLightYaw = DrawSlider("Yaw", _viewerLightYaw, -180f, 180f);
            _viewerLightPitch = DrawSlider("Pitch", _viewerLightPitch, -90f, 90f);
            DrawLightingStatus();
        }

        private static float DrawSlider(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {value:0.00}", GUILayout.Width(105));
            value = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.EndHorizontal();
            return value;
        }

        private void DrawLightingStatus()
        {
            string status = _loadedModel != null && _lastLightingMaterialCount == 0
                ? "W3D lit materials: 0 (W3D shader not active)"
                : $"W3D lit materials: {_lastLightingMaterialCount}";
            GUILayout.Label(status);
        }

        private Camera ResolveViewerCamera()
        {
            if (_viewerCamera != null)
                return _viewerCamera;

            _viewerCamera = Camera.main;
            if (_viewerCamera != null)
                return _viewerCamera;

            _viewerCamera = GetComponent<Camera>();
            if (_viewerCamera != null)
            {
                Debug.LogWarning("[W3D] Camera.main was not found; using the camera attached to W3DViewerUI.");
                return _viewerCamera;
            }

            _viewerCamera = FindAnyObjectByType<Camera>();
            if (_viewerCamera != null)
            {
                Debug.LogWarning($"[W3D] Camera.main was not found; using fallback camera '{_viewerCamera.name}'.");
                return _viewerCamera;
            }

            var cameraGo = new GameObject("W3D_ViewerCamera");
            _viewerCamera = cameraGo.AddComponent<Camera>();
            _viewerCamera.transform.position = new Vector3(0f, 0f, -10f);
            _viewerCamera.transform.rotation = Quaternion.identity;
            Debug.LogWarning("[W3D] Camera.main was not found; created fallback camera 'W3D_ViewerCamera'.");
            return _viewerCamera;
        }

        private static void ConfigureCameraClipPlanes(Camera cam, Bounds bounds)
        {
            float radius = Mathf.Max(bounds.extents.magnitude, 1f);
            cam.nearClipPlane = Mathf.Max(radius * 0.0025f, 0.02f);
            cam.farClipPlane = Mathf.Max(radius * 100f, 500f);
        }

        private void ApplyViewerCameraOverrides(Camera cam)
        {
            if (cam == null)
                return;

            if (_presentationCamera != cam)
            {
                RestoreViewerCameraOverrides();

                _presentationCamera = cam;
                _savedClearFlags = cam.clearFlags;
                _savedBackgroundColor = cam.backgroundColor;
                _savedSkybox = cam.GetComponent<Skybox>();
                _savedSkyboxEnabled = _savedSkybox != null && _savedSkybox.enabled;
                _savedRenderFog = RenderSettings.fog;
                _savedRenderFogCaptured = true;
                _savedAmbientMode = RenderSettings.ambientMode;
                _savedAmbientLight = RenderSettings.ambientLight;
                _savedAmbientIntensity = RenderSettings.ambientIntensity;
                _savedAmbientLightingCaptured = true;
                CaptureHdrpCameraVolumeState(cam);
            }

            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = ViewerBackgroundColor;
            ApplyHdrpCameraVolumeOverride();

            if (_savedSkybox != null)
                _savedSkybox.enabled = false;

            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ViewerAmbientLightColor;
            RenderSettings.ambientIntensity = Mathf.Max(RenderSettings.ambientIntensity, 1f);
            ApplyViewerShaderLighting();
        }

        private void RestoreViewerCameraOverrides()
        {
            if (_presentationCamera != null)
            {
                _presentationCamera.clearFlags = _savedClearFlags;
                _presentationCamera.backgroundColor = _savedBackgroundColor;
            }

            if (_savedSkybox != null)
                _savedSkybox.enabled = _savedSkyboxEnabled;

            if (_savedRenderFogCaptured)
                RenderSettings.fog = _savedRenderFog;

            if (_savedAmbientLightingCaptured)
            {
                RenderSettings.ambientMode = _savedAmbientMode;
                RenderSettings.ambientLight = _savedAmbientLight;
                RenderSettings.ambientIntensity = _savedAmbientIntensity;
            }

            RestoreHdrpCameraVolumeState();

            _presentationCamera = null;
            _savedSkybox = null;
            _savedRenderFogCaptured = false;
            _savedAmbientLightingCaptured = false;

#if UNITY_EDITOR
            RestoreEditorSceneViewOverrides();
#endif
        }

#if UNITY_EDITOR
        private void ApplyEditorSceneViewOverrides()
        {
            if (!_savedSceneViewStateCaptured)
            {
                _presentationSceneView = SceneView.lastActiveSceneView;
                if (_presentationSceneView == null && SceneView.sceneViews.Count > 0)
                    _presentationSceneView = SceneView.sceneViews[0] as SceneView;

                if (_presentationSceneView == null)
                    return;

                _savedSceneViewFogEnabled = _presentationSceneView.sceneViewState.showFog;
                _savedSceneViewSkyboxEnabled = _presentationSceneView.sceneViewState.showSkybox;
                _savedSceneViewStateCaptured = true;
            }

            _presentationSceneView.sceneViewState.showFog = false;
            _presentationSceneView.sceneViewState.showSkybox = false;
            SceneView.RepaintAll();
        }

        private void RestoreEditorSceneViewOverrides()
        {
            if (_presentationSceneView == null || !_savedSceneViewStateCaptured)
                return;

            _presentationSceneView.sceneViewState.showFog = _savedSceneViewFogEnabled;
            _presentationSceneView.sceneViewState.showSkybox = _savedSceneViewSkyboxEnabled;
            _presentationSceneView = null;
            _savedSceneViewStateCaptured = false;
            SceneView.RepaintAll();
        }
#endif

        private void CaptureHdrpCameraVolumeState(Camera cam)
        {
            if (HdAdditionalCameraDataType == null || HdVolumeLayerMaskField == null)
                return;

            _savedHdCameraData = cam.GetComponent(HdAdditionalCameraDataType);
            if (_savedHdCameraData == null)
            {
                // Without HDAdditionalCameraData the volume-mask override below is a
                // no-op and the scene's fog/exposure volume still hits the viewer
                // camera in play mode. Add one so the override actually applies; it is
                // removed again in RestoreHdrpCameraVolumeState.
                _savedHdCameraData = cam.gameObject.AddComponent(HdAdditionalCameraDataType) as Component;
                _addedHdCameraData = _savedHdCameraData != null;
                if (_savedHdCameraData == null)
                    return;
            }

            _savedHdVolumeLayerMask = (LayerMask)HdVolumeLayerMaskField.GetValue(_savedHdCameraData);
            if (HdVolumeAnchorOverrideField != null)
                _savedHdVolumeAnchorOverride = HdVolumeAnchorOverrideField.GetValue(_savedHdCameraData) as Transform;
            _savedHdCameraVolumeCaptured = true;
        }

        private void ApplyHdrpCameraVolumeOverride()
        {
            if (_savedHdCameraData == null)
                return;

            LayerMask emptyVolumeMask = 0;
            HdVolumeLayerMaskField.SetValue(_savedHdCameraData, emptyVolumeMask);
            HdVolumeAnchorOverrideField?.SetValue(_savedHdCameraData, null);

            // HDRP ignores Camera.clearFlags; the background is driven by
            // HDAdditionalCameraData. 1 = ClearColorMode.BackgroundColor.
            if (HdClearColorModeField != null)
                HdClearColorModeField.SetValue(_savedHdCameraData, Enum.ToObject(HdClearColorModeField.FieldType, 1));
            HdBackgroundColorHDRField?.SetValue(_savedHdCameraData, ViewerBackgroundColor);
        }

        private void RestoreHdrpCameraVolumeState()
        {
            if (!_savedHdCameraVolumeCaptured || _savedHdCameraData == null)
            {
                _savedHdCameraData = null;
                _savedHdCameraVolumeCaptured = false;
                _addedHdCameraData = false;
                return;
            }

            if (_addedHdCameraData)
            {
                if (Application.isPlaying)
                    Destroy(_savedHdCameraData);
                else
                    DestroyImmediate(_savedHdCameraData);
            }
            else
            {
                HdVolumeLayerMaskField.SetValue(_savedHdCameraData, _savedHdVolumeLayerMask);
                HdVolumeAnchorOverrideField?.SetValue(_savedHdCameraData, _savedHdVolumeAnchorOverride);
            }

            _savedHdCameraData = null;
            _savedHdVolumeAnchorOverride = null;
            _savedHdCameraVolumeCaptured = false;
            _addedHdCameraData = false;
        }

        private void ApplyViewerShaderLighting()
        {
            var direction = Quaternion.Euler(_viewerLightPitch, _viewerLightYaw, 0f) * Vector3.forward;
            var color = Color.white;
            var intensity = _viewerLightIntensity;

            if (_useSceneLightForPreview &&
                TryResolvePreviewLight(_orbitTarget, out var resolvedDirection, out var resolvedColor, out var resolvedIntensity))
            {
                direction = resolvedDirection;
                color = resolvedColor;
                intensity = resolvedIntensity;
            }

            var ambient = new Color(_viewerLightAmbient, _viewerLightAmbient, _viewerLightAmbient, 1f);
            Shader.SetGlobalColor(W3DViewerAmbientColorId, ambient);
            Shader.SetGlobalVector(
                W3DViewerLightDirectionId,
                new Vector4(direction.x, direction.y, direction.z, 0f));
            Shader.SetGlobalColor(W3DViewerLightColorId, color);
            Shader.SetGlobalFloat(W3DViewerLightIntensityId, intensity);
            ApplyLoadedModelLightingValues(ambient, direction, color, intensity);
        }

        private void ApplyLoadedModelLightingFlags()
        {
            ApplyLoadedModelLightingValues(
                new Color(_viewerLightAmbient, _viewerLightAmbient, _viewerLightAmbient, 1f),
                Quaternion.Euler(_viewerLightPitch, _viewerLightYaw, 0f) * Vector3.forward,
                Color.white,
                _viewerLightIntensity);
        }

        private void ApplyLoadedModelLightingValues(Color ambient, Vector3 direction, Color color, float intensity)
        {
            if (_loadedModel == null)
            {
                _lastLightingMaterialCount = 0;
                return;
            }

            int litMaterialCount = 0;
            var directionVector = new Vector4(direction.x, direction.y, direction.z, 0f);
            _viewerLightingProperties ??= new MaterialPropertyBlock();
            var renderers = _loadedModel.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                var materials = renderer.sharedMaterials;
                bool rendererHasViewerLighting = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    var mat = materials[i];
                    if (mat == null)
                        continue;

                    // _ApplyLighting is left as built: surfaces whose W3D lighting does not depend
                    // on the scene lights (emissive driven, see W3DMaterialBuilder) stay unlit
                    bool hasViewerLighting = mat.HasProperty("_ApplyLighting");

                    if (mat.HasProperty(W3DViewerAmbientColorId))
                    {
                        mat.SetColor(W3DViewerAmbientColorId, ambient);
                        hasViewerLighting = true;
                    }

                    if (mat.HasProperty(W3DViewerLightDirectionId))
                    {
                        mat.SetVector(W3DViewerLightDirectionId, directionVector);
                        hasViewerLighting = true;
                    }

                    if (mat.HasProperty(W3DViewerLightColorId))
                    {
                        mat.SetColor(W3DViewerLightColorId, color);
                        hasViewerLighting = true;
                    }

                    if (mat.HasProperty(W3DViewerLightIntensityId))
                    {
                        mat.SetFloat(W3DViewerLightIntensityId, intensity);
                        hasViewerLighting = true;
                    }

                    if (hasViewerLighting)
                    {
                        litMaterialCount++;
                        rendererHasViewerLighting = true;
                    }
                }

                if (!rendererHasViewerLighting)
                    continue;

                renderer.GetPropertyBlock(_viewerLightingProperties);
                _viewerLightingProperties.SetColor(W3DViewerAmbientColorId, ambient);
                _viewerLightingProperties.SetVector(W3DViewerLightDirectionId, directionVector);
                _viewerLightingProperties.SetColor(W3DViewerLightColorId, color);
                _viewerLightingProperties.SetFloat(W3DViewerLightIntensityId, intensity);
                renderer.SetPropertyBlock(_viewerLightingProperties);
            }

            _lastLightingMaterialCount = litMaterialCount;
        }

        private static bool TryResolvePreviewLight(
            Vector3 target,
            out Vector3 direction,
            out Color color,
            out float intensity)
        {
            direction = FallbackViewerShaderLightDirection;
            color = Color.white;
            intensity = 0f;

            Light bestLight = null;
            float bestScore = -1f;
            var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; i++)
            {
                var light = lights[i];
                if (light == null || !light.enabled || !light.gameObject.activeInHierarchy)
                    continue;

                float score = light.intensity;
                if (light.type == LightType.Directional)
                    score *= 4f;
                if (score <= bestScore)
                    continue;

                bestLight = light;
                bestScore = score;
            }

            if (bestLight == null)
                return false;

            direction = bestLight.type == LightType.Directional
                ? -bestLight.transform.forward
                : (bestLight.transform.position - target);

            if (direction.sqrMagnitude < 0.0001f)
                direction = FallbackViewerShaderLightDirection;
            else
                direction.Normalize();

            color = bestLight.color;
            intensity = NormalizePreviewLightIntensity(bestLight.intensity);
            return true;
        }

        private static float NormalizePreviewLightIntensity(float lightIntensity)
        {
            if (lightIntensity <= 0f)
                return 0f;

            if (lightIntensity > 1000f)
                return Mathf.Clamp(lightIntensity / 130000f, 0f, 1.5f);

            return Mathf.Clamp(lightIntensity / BuiltInViewerLightIntensity, 0f, 1.5f);
        }

        private Bounds CalculateBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return new Bounds(go.transform.position, Vector3.one);

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }
    }
}
