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

            //MigratePackageAssets();

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

            if (!Directory.Exists(sourcePath) || Directory.GetFiles(sourcePath, "*.prefab").Length == 0)
            {
                return;
            }

            if (!AssetDatabase.IsValidFolder(destinationPath))
            {
                Directory.CreateDirectory(destinationPath);
                AssetDatabase.Refresh();
            }

            string[] sourceFiles = Directory.GetFiles(sourcePath, "*.prefab");
            bool movedAnything = false;

            foreach (string file in sourceFiles)
            {
                string fileName = Path.GetFileName(file);
                string destFile = $"{destinationPath}/{fileName}";

                string error = AssetDatabase.MoveAsset(file, destFile);

                if (string.IsNullOrEmpty(error))
                {
                    Debug.Log($"[NX10] Successfully migrated: {fileName}");
                    movedAnything = true;
                }
                else
                {
                    Debug.LogWarning($"[NX10] Could not move {fileName}: {error}. " +
                                    "The package might be read-only.");
                }
            }

            if (movedAnything)
            {
                AssetDatabase.Refresh();
            }
        }
    }
}

#endif