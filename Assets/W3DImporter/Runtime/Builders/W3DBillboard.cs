using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace W3DImporter.Builders
{
    [ExecuteAlways]
    public sealed class W3DBillboard : MonoBehaviour
    {
        // Imported ENB/CnC billboard quads present their visible face on local -X, not +X.
        // Without this correction the billboard logic points the quad's back side at the camera,
        // which makes asymmetric textures such as posters/signs read mirrored.
        private static readonly Quaternion FacingCorrection = Quaternion.Euler(0f, 180f, 0f);

        public enum BillboardMode
        {
            CameraAligned,
            CameraOriented
        }

        public BillboardMode Mode;

        private Camera _camera;
        private Quaternion _initialLocalRotation;

        private void Awake()
        {
            _initialLocalRotation = transform.localRotation;
        }

        private void OnEnable()
        {
            _camera = null;
        }

        private void LateUpdate()
        {
            if (_camera == null)
                _camera = ResolveCamera();
            if (_camera == null)
                return;

            var parentRotation = transform.parent != null
                ? transform.parent.rotation
                : Quaternion.identity;

            // W3D convention:
            //   CAMERA_ORIENTED "points its +ve x-axis at the camera" with free rotation
            //     only about the world-up (W3D +Z, Unity +Y) axis. Quad stays level; only yaw tracks camera.
            //   CAMERA_ALIGNED is fully screen-aligned — the mesh rotates freely in all axes to face the camera plane.
            //
            // After the importer basis rotation (-90 deg X), W3D +X maps to Unity +X.
            // So in Unity local space we still want the mesh's local +X to point at the camera.
            Quaternion targetWorldRotation;
            switch (Mode)
            {
                case BillboardMode.CameraOriented:
                {
                    var toCamera = _camera.transform.position - transform.position;
                    var toCameraFlat = new Vector3(toCamera.x, 0f, toCamera.z);
                    if (toCameraFlat.sqrMagnitude <= 0.0001f)
                        return;
                    toCameraFlat.Normalize();
                    var forward = Vector3.Cross(toCameraFlat, Vector3.up);
                    targetWorldRotation = Quaternion.LookRotation(forward, Vector3.up);
                    break;
                }
                case BillboardMode.CameraAligned:
                {
                    // Renegade uses the camera's Z axis here, not the vector from mesh to camera:
                    //   tmp_world.Obj_Look_At(mesh_position, mesh_position + camera_z_vector, 0.0f)
                    // Matrix3D::Look_At makes camera +Z point backward, so Unity's equivalent is
                    // "-camera.forward". Using the mesh-to-camera vector makes nearby large quads
                    // pivot around the eye position instead of staying screen-plane aligned.
                    var cameraZ = -_camera.transform.forward;
                    if (cameraZ.sqrMagnitude <= 0.0001f)
                        return;

                    var right = cameraZ.normalized;
                    var up = _camera.transform.up;
                    var forward = Vector3.Cross(right, up).normalized;
                    if (forward.sqrMagnitude <= 0.0001f)
                        return;
                    up = Vector3.Cross(forward, right);
                    targetWorldRotation = Quaternion.LookRotation(forward, up);
                    break;
                }
                default:
                    return;
            }

            targetWorldRotation *= FacingCorrection;

            // Preserve the authored local rotation offset under the current parent bone.
            transform.localRotation = Quaternion.Inverse(parentRotation) * targetWorldRotation * _initialLocalRotation;
        }

        private static Camera ResolveCamera()
        {
            if (Camera.main != null)
                return Camera.main;

#if UNITY_EDITOR
            if (SceneView.lastActiveSceneView != null && SceneView.lastActiveSceneView.camera != null)
                return SceneView.lastActiveSceneView.camera;
#endif

            return Camera.current != null
                ? Camera.current
                : Object.FindAnyObjectByType<Camera>();
        }
    }
}
