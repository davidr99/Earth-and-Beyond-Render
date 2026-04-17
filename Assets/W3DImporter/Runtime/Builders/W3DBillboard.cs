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

            Quaternion targetWorldRotation;
            switch (Mode)
            {
                case BillboardMode.CameraAligned:
                {
                    var toCamera = _camera.transform.position - transform.position;
                    if (toCamera.sqrMagnitude <= 0.0001f)
                        return;
                    targetWorldRotation = Quaternion.LookRotation(toCamera.normalized, _camera.transform.up);
                    break;
                }
                case BillboardMode.CameraOriented:
                    targetWorldRotation = _camera.transform.rotation;
                    break;
                default:
                    return;
            }

            // Preserve the authored local rotation offset under the current parent bone.
            transform.localRotation = Quaternion.Inverse(parentRotation) * targetWorldRotation * _initialLocalRotation;
        }
    }
}
