using UnityEngine;

namespace W3DImporter.Builders
{
    /// <summary>
    /// Ensures a usable shader is always available at runtime.
    /// Attach this to any GameObject in your scene to guarantee shader inclusion.
    /// </summary>
    public class W3DShaderHelper : MonoBehaviour
    {
        [Tooltip("Reference material to ensure its shader is included in builds")]
        public Material ReferenceMaterial;

        /// <summary>
        /// Creates a basic unlit textured material that works everywhere.
        /// </summary>
        public static Material CreateBasicMaterial(string name, Texture2D texture)
        {
            // Try to find any working shader
            Shader shader = FindAnyShader();
            if (shader == null)
            {
                Debug.LogError("[W3D] Cannot find any shader at all!");
                return new Material(Shader.Find("Hidden/InternalErrorShader"));
            }

            var mat = new Material(shader);
            mat.name = name;
            if (texture != null)
                mat.mainTexture = texture;
            return mat;
        }

        public static Shader FindAnyShader()
        {
            // Comprehensive list covering all Unity configurations
            string[] candidates = {
                "Universal Render Pipeline/Lit",
                "Universal Render Pipeline/Simple Lit",
                "Universal Render Pipeline/Unlit",
                "Standard",
                "Standard (Specular setup)",
                "Legacy Shaders/Diffuse",
                "Legacy Shaders/Transparent/Diffuse",
                "Unlit/Texture",
                "Unlit/Color",
                "Sprites/Default",
                "UI/Default"
            };

            foreach (var name in candidates)
            {
                var s = Shader.Find(name);
                if (s != null) return s;
            }

            return null;
        }
    }
}
