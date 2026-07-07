using UnityEngine;

namespace W3DImporter.Builders
{
    /// <summary>
    /// Converts W3D coordinates to Unity coordinates.
    /// W3D is Z-up and needs a handedness change for Unity: (x, y, z) -> (-x, z, -y).
    /// Mesh triangle winding is reversed separately in W3DMeshBuilder to compensate.
    /// </summary>
    public static class W3DCoordinateConverter
    {
        private static readonly Matrix4x4 BasisMatrix = CreateBasisMatrix();
        private static readonly Matrix4x4 InverseBasisMatrix = CreateInverseBasisMatrix();

        public static Vector3 ConvertPosition(Vector3 w3d)
        {
            return new Vector3(-w3d.x, w3d.z, -w3d.y);
        }

        public static Quaternion ConvertRotation(Quaternion w3d)
        {
            var converted = BasisMatrix * Matrix4x4.Rotate(w3d) * InverseBasisMatrix;
            return Quaternion.LookRotation(
                new Vector3(converted.m02, converted.m12, converted.m22),
                new Vector3(converted.m01, converted.m11, converted.m21));
        }

        public static Vector3 ConvertNormal(Vector3 w3d)
        {
            return new Vector3(-w3d.x, w3d.z, -w3d.y);
        }

        private static Matrix4x4 CreateBasisMatrix()
        {
            var matrix = Matrix4x4.identity;
            matrix.SetColumn(0, new Vector4(-1f, 0f, 0f, 0f));
            matrix.SetColumn(1, new Vector4(0f, 0f, -1f, 0f));
            matrix.SetColumn(2, new Vector4(0f, 1f, 0f, 0f));
            return matrix;
        }

        private static Matrix4x4 CreateInverseBasisMatrix()
        {
            var matrix = Matrix4x4.identity;
            matrix.SetColumn(0, new Vector4(-1f, 0f, 0f, 0f));
            matrix.SetColumn(1, new Vector4(0f, 0f, 1f, 0f));
            matrix.SetColumn(2, new Vector4(0f, -1f, 0f, 0f));
            return matrix;
        }
    }
}
