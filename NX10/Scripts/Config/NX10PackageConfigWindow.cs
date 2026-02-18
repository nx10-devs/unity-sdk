using TMPro;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR
namespace NX10
{
    public class NX10PackageConfigWindow : EditorWindow
    {
        private NX10PackageConfig config;

        [MenuItem("Window/NX10/Configuration")]
        static void Open()
        {
            GetWindow<NX10PackageConfigWindow>("NX10 Config");
        }

        void OnEnable()
        {
            config = Resources.Load<NX10PackageConfig>("NX10Package_Config");
        }

        void OnGUI()
        {
            if (!config)
            {
                EditorGUILayout.HelpBox("Config not found.", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("Appearance", EditorStyles.boldLabel);
            config.promptBackgroundSprite =
                (Sprite)EditorGUILayout.ObjectField(
                    "Prompt Background Sprite",
                    config.promptBackgroundSprite,
                    typeof(Sprite),
                    false
            );

            config.promptFont =
                (TMP_FontAsset)EditorGUILayout.ObjectField(
                    "Prompt Font",
                    config.promptFont,
                    typeof(TMP_FontAsset),
                    false
            );

            GUILayout.Space(10);

            if (GUILayout.Button("Apply"))
            {
                Apply();
            }

            EditorUtility.SetDirty(config);
        }
        void Apply()
        {
            ApplyToPrefab("Packages/com.nx10.sdk/NX10/Prefabs/prefab_SliderPrompt.prefab");
            ApplyToPrefab("Packages/com.nx10.sdk/NX10/Prefabs/prefab_TwoButtonPrompt.prefab");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        void ApplyToPrefab(string prefabPath)
        {
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);

            try
            {
                var prompt = prefabRoot.GetComponentInChildren<NX10PromptThemeApplier>();
                if (prompt == null)
                {
                    Debug.LogWarning($"NX10Prompt not found in {prefabPath}");
                    return;
                }

                prompt.ApplyConfig(config);

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }
    }
}
#endif
