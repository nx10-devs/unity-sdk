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
            var existingConfig = AssetDatabase.LoadAssetAtPath<NX10Config>(CONFIG_PATH);
            if (existingConfig != null)
                return;

            if (!AssetDatabase.IsValidFolder(CONFIG_FOLDER_PARENT))
            {
                AssetDatabase.CreateFolder("Assets", "NX10Config");
                AssetDatabase.CreateFolder(CONFIG_FOLDER_PARENT, "Resources");
            }

            var config = ScriptableObject.CreateInstance<NX10Config>();
            AssetDatabase.CreateAsset(config, CONFIG_PATH);
            AssetDatabase.SaveAssets();

            Debug.Log("MyPackage: Default config created at " + CONFIG_PATH);
        }
    }
}

#endif