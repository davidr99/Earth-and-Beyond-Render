using UnityEngine;

namespace W3DImporter.Builders
{
    public sealed class W3DBillboard : MonoBehaviour
    {
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

        private void LateUpdate()
        {
            if (_camera == null)
                _camera = Camera.main;
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
                    var toCamera = _camera.transform.position - transform.position;
                    if (toCamera.sqrMagnitude <= 0.0001f)
                        return;
                    var right = toCamera.normalized;
                    var up = _camera.transform.up;
                    var forward = Vector3.Cross(right, up).normalized;
                    up = Vector3.Cross(forward, right);
                    targetWorldRotation = Quaternion.LookRotation(forward, up);
                    break;
                }
                default:
                    return;
            }

            // Preserve the authored local rotation offset under the current parent bone.
            transform.localRotation = Quaternion.Inverse(parentRotation) * targetWorldRotation * _initialLocalRotation;
        }
    }
}
