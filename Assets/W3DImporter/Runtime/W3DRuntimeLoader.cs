using System.IO;
using UnityEngine;
using W3DImporter.Parsers;
using W3DImporter.Builders;

namespace W3DImporter
{
    public class W3DRuntimeLoader : MonoBehaviour
    {
        [Header("W3D File Settings")]
        [Tooltip("Path to a .w3d file to load")]
        public string FilePath;

        [Tooltip("Base directory for resolving textures and external files")]
        public string TextureDirectory;

        [Header("Controls")]
        [Tooltip("Load the file on Start")]
        public bool LoadOnStart;

        private GameObject _loadedModel;

        private void Start()
        {
            if (LoadOnStart && !string.IsNullOrEmpty(FilePath))
                LoadFile(FilePath);
        }

        public GameObject LoadFile(string path)
        {
            if (!File.Exists(path))
            {
                Debug.LogError($"[W3D] File not found: {path}");
                return null;
            }

            // Clean up previous model
            if (_loadedModel != null)
                Destroy(_loadedModel);

            string baseDir = string.IsNullOrEmpty(TextureDirectory)
                ? Path.GetDirectoryName(path)
                : TextureDirectory;

            var context = W3DFileParser.Parse(path);
            var builder = new W3DSceneBuilder(baseDir);
            _loadedModel = builder.Build(context);

            Debug.Log($"[W3D] Loaded model from: {path}");
            return _loadedModel;
        }

        public void Clear()
        {
            if (_loadedModel != null)
            {
                Destroy(_loadedModel);
                _loadedModel = null;
            }
        }
    }
}
