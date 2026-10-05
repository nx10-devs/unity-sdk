#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

namespace NX10
{
    [InitializeOnLoad]
    public static class NX10ConfigInitialiser
    {
        const string CONFIG_FOLDER_PARENT = "Assets/NX10Config";
        const string CONFIG_FOLDER = CONFIG_FOLDER_PARENT + "/Resources";
        const string CONFIG_PATH = CONFIG_FOLDER + "/NX10Package_Config.asset";

        static NX10ConfigInitialiser()
        {
            EditorApplication.delayCall += TryCreateConfig;
        }

        static void TryCreateConfig()
        { 
            if (!AssetDatabase.IsValidFolder(CONFIG_FOLDER_PARENT))
            {
                AssetDatabase.CreateFolder("Assets", "NX10Config");
                AssetDatabase.CreateFolder(CONFIG_FOLDER_PARENT, "Resources");
            }

            MigratePackageAssets();

            var existingConfig = AssetDatabase.LoadAssetAtPath<NX10Config>(CONFIG_PATH);
            if (existingConfig != null)
                return;

            var config = ScriptableObject.CreateInstance<NX10Config>();
            AssetDatabase.CreateAsset(config, CONFIG_PATH);
            AssetDatabase.SaveAssets(); 

            Debug.Log("MyPackage: Default config created at " + CONFIG_PATH);
        }

        private static void MigratePackageAssets()
        {
            string sourcePath = "Packages/com.nx10.sdk/Runtime/Prefabs";
            string destinationPath = "Assets/NX10Config/Resources";

            if (!Directory.Exists(sourcePath)) return;

            // Ensure the destination folder exists
            if (!AssetDatabase.IsValidFolder(destinationPath))
            {
                // It's better to use AssetDatabase to create folders within Assets
                // to keep the .meta files healthy.
                Directory.CreateDirectory(destinationPath);
                AssetDatabase.Refresh();
            }

            string[] sourceFiles = Directory.GetFiles(sourcePath, "*.prefab");
            bool copiedAnything = false;

            foreach (string file in sourceFiles)
            {
                string fileName = Path.GetFileName(file);
                string destFile = $"{destinationPath}/{fileName}";

                // IMPORTANT: Check if the file already exists so you don't 
                // overwrite the user's custom changes every time this runs.
                if (File.Exists(destFile))
                {
                    continue;
                }

                // Use CopyAsset instead of MoveAsset
                if (AssetDatabase.CopyAsset(file, destFile))
                {
                    Debug.Log($"[NX10] Successfully copied: {fileName} to {destinationPath}");
                    copiedAnything = true;
                }
                else
                {
                    Debug.LogError($"[NX10] Failed to copy {fileName}.");
                }
            }

            if (copiedAnything)
            {
                AssetDatabase.Refresh();
            }
        }
    }
}

#endif