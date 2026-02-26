using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace NX10
{
    public class NX10PackageConfigWindow : EditorWindow
    {
        private NX10PackageConfig config;
        private TextField apiKeyField;
        private Label statusLabel;

        [MenuItem("Window/NX10/Configuration")]
        public static void Open()
        {
            var wnd = GetWindow<NX10PackageConfigWindow>();
            wnd.titleContent = new GUIContent("Config");
        }

        static Texture2D LoadStaticIcon()
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Packages/com.nx10.sdk/NX10/Scripts/Config/Editor/nx10_logo.png");
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();

            config = Resources.Load<NX10PackageConfig>("NX10Package_Config");

            if (!config)
            {
                rootVisualElement.Add(new Label("Config not found in Resources folder."));
                return;
            }

            // Load USS
            var script = MonoScript.FromScriptableObject(this);
            var scriptPath = AssetDatabase.GetAssetPath(script);
            var folder = System.IO.Path.GetDirectoryName(scriptPath);
            var ussPath = System.IO.Path.Combine(folder, "NX10PackageConfigWindow.uss");

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);

            if (styleSheet != null)
                rootVisualElement.styleSheets.Add(styleSheet);
            else
                Debug.LogWarning("NX10 USS file not found at: " + ussPath);

            if (styleSheet != null)
                rootVisualElement.styleSheets.Add(styleSheet);

            rootVisualElement.Add(CreateHeader());
            rootVisualElement.Add(CreateApiSection());
            rootVisualElement.Add(CreateAppearanceSection());
            rootVisualElement.Add(CreateApplyButton());
            rootVisualElement.Add(CreateFooter());
        }

        VisualElement CreateHeader()
        {
            var container = new VisualElement();
            container.AddToClassList("header");

            var row = new VisualElement();
            row.AddToClassList("header-row");

            var logo = new Image();
            logo.image = LoadStaticIcon();
            logo.AddToClassList("header-logo");

            var textContainer = new VisualElement();
            textContainer.style.flexDirection = FlexDirection.Column;

            var title = new Label("NX10 SDK");
            title.AddToClassList("header-title");

            var subtitle = new Label("Deployment Configuration");
            subtitle.AddToClassList("header-subtitle");

            textContainer.Add(title);
            textContainer.Add(subtitle);

            row.Add(logo);
            row.Add(textContainer);

            container.Add(row);

            return container;
        }

        VisualElement CreateApiSection()
        {
            var card = CreateCard("API Settings");

            apiKeyField = new TextField("API Key");
            apiKeyField.isPasswordField = true;
            apiKeyField.value = config.apiKey;

            var toggle = new Button(() =>
            {
                apiKeyField.isPasswordField = !apiKeyField.isPasswordField;
            })
            { text = "Show / Hide" };

            var row = new VisualElement();
            row.AddToClassList("row");
            row.Add(apiKeyField);
            row.Add(toggle);

            statusLabel = new Label();
            statusLabel.AddToClassList("status-label");

            card.Add(row);
            card.Add(statusLabel);

            return card;
        }

        VisualElement CreateAppearanceSection()
        {
            var card = CreateCard("Appearance");

            var spriteField = new ObjectField("Prompt Background")
            {
                objectType = typeof(Sprite),
                value = config.promptBackgroundSprite
            };

            spriteField.RegisterValueChangedCallback(evt =>
            {
                config.promptBackgroundSprite = (Sprite)evt.newValue;
                EditorUtility.SetDirty(config);
            });

            var fontField = new ObjectField("Prompt Font")
            {
                objectType = typeof(TMPro.TMP_FontAsset),
                value = config.promptFont
            };

            fontField.RegisterValueChangedCallback(evt =>
            {
                config.promptFont = (TMPro.TMP_FontAsset)evt.newValue;
                EditorUtility.SetDirty(config);
            });

            card.Add(spriteField);
            card.Add(fontField);

            return card;
        }

        VisualElement CreateApplyButton()
        {
            var button = new Button(() =>
            {
                config.apiKey = apiKeyField.value;
                EditorUtility.SetDirty(config);
                Apply();
                statusLabel.text = "✔ Configuration Applied";
            })
            {
                text = "Apply Changes"
            };

            button.AddToClassList("primary-button");

            return button;
        }

        VisualElement CreateFooter()
        {
            var footer = new Label($"NX10 SDK v{GetSdkVersion()}"); footer.AddToClassList("footer");
            return footer;

            string GetSdkVersion()
            {
                var assembly = typeof(NX10PackageConfigWindow).Assembly;
                var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(assembly);

                if (packageInfo != null)
                    return packageInfo.version;

                return "Unknown";
            }
        }

        VisualElement CreateCard(string title)
        {
            var card = new VisualElement();
            card.AddToClassList("card");

            var label = new Label(title);
            label.AddToClassList("card-title");

            card.Add(label);

            return card;
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
            var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);

            try
            {
                var prompt = prefabRoot.GetComponentInChildren<NX10PromptThemeApplier>();
                if (prompt == null) return;

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