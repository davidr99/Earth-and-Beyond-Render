using System.Collections.Generic;
using UnityEngine;
using W3DImporter.Structs.CollisionBox;

namespace W3DImporter.Builders
{
    public static class W3DCollisionBoxBuilder
    {
        public static GameObject Build(W3DCollisionBox box, Transform root, Dictionary<int, Transform> boneMap)
        {
            var go = new GameObject(box.Name);
            go.transform.SetParent(root, false);

            var collider = go.AddComponent<BoxCollider>();
            collider.center = W3DCoordinateConverter.ConvertPosition(box.Center);
            collider.size = new Vector3(
                Mathf.Abs(box.Extent.x) * 2f,
                Mathf.Abs(box.Extent.z) * 2f,
                Mathf.Abs(box.Extent.y) * 2f
            );

            return go;
        }
    }
}
