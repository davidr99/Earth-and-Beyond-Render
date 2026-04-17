using UnityEngine;

namespace W3DImporter.Builders
{
    /// <summary>
    /// Converts W3D/Blender-style coordinates to Unity coordinates.
    /// W3D data is treated as Z-up and maps to Unity via a -90 degree X-axis rotation:
    /// (x, y, z) -> (x, z, -y)
    /// This is a basis rotation, not a mirror, so triangle winding is preserved.
    /// </summary>
    public static class W3DCoordinateConverter
    {
        private static readonly Quaternion BasisRotation = Quaternion.Euler(-90f, 0f, 0f);
        private static readonly Quaternion InverseBasisRotation = Quaternion.Inverse(BasisRotation);

        public static Vector3 ConvertPosition(Vector3 w3d)
        {
            return BasisRotation * w3d;
        }

        public static Quaternion ConvertRotation(Quaternion w3d)
        {
            return BasisRotation * w3d * InverseBasisRotation;
        }

        public static Vector3 ConvertNormal(Vector3 w3d)
        {
            return BasisRotation * w3d;
        }
    }
}
