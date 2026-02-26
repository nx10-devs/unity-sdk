using System.Security.Cryptography;
using TMPro;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace NX10
{
    public class NX10PackageConfigWindow : EditorWindow
    {
        private NX10PackageConfig config;
        private NX10PackageConfig workingCopy;

        private TextField apiKeyField;
        private ObjectField backgroundSpriteField;
        private ObjectField fontField;

        private Label statusLabel;

        GameObject previewInstance;
        Camera previewCamera;
        RenderTexture previewTexture;

        TextMeshProUGUI titleText;
        TextMeshProUGUI questionText;
        TextMeshProUGUI feelingText;
        TextMeshProUGUI submitText;
        UnityEngine.UI.Image backgroundImage;

        [MenuItem("Window/NX10/Configuration")]
        public static void Open()
        {
            var wnd = GetWindow<NX10PackageConfigWindow>();
            wnd.titleContent = new GUIContent("Config");
        }

        static Texture2D LoadLogo()
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

            workingCopy = ScriptableObject.Instantiate(config);

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
            rootVisualElement.Add(CreateDiscardButton());
            rootVisualElement.Add(CreateFooter());
        }

        VisualElement CreateHeader()
        {
            var container = new VisualElement();
            container.AddToClassList("header");

            var row = new VisualElement();
            row.AddToClassList("header-row");

            var logo = new Image();
            logo.image = LoadLogo();
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

            apiKeyField.RegisterValueChangedCallback(evt =>
            {
                workingCopy.apiKey = config.apiKey;
            });

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
            var card = CreateCard("Prompt Appearance");

            backgroundSpriteField = new ObjectField("Prompt Background")
            {
                objectType = typeof(Sprite),
                value = config.promptBackgroundSprite
            };

            fontField = new ObjectField("Prompt Font")
            {
                objectType = typeof(TMPro.TMP_FontAsset),
                value = config.promptFont
            };

            card.Add(backgroundSpriteField);
            card.Add(fontField);

            CreatePreview();

            var previewBox = new VisualElement();
            previewBox.style.borderTopWidth = 1;
            previewBox.style.borderBottomWidth = 1;
            previewBox.style.borderLeftWidth = 1;
            previewBox.style.borderRightWidth = 1;
            previewBox.style.borderTopColor = Color.gray;
            previewBox.style.borderBottomColor = Color.gray;
            previewBox.style.borderLeftColor = Color.gray;
            previewBox.style.borderRightColor = Color.gray;
            previewBox.style.paddingLeft = 4;
            previewBox.style.paddingRight = 4;
            previewBox.style.paddingTop = 12; 
            previewBox.style.paddingBottom = 4;
            previewBox.style.marginTop = 8;
            previewBox.style.marginBottom = 8;
            previewBox.style.flexDirection = FlexDirection.Column;
            previewBox.style.position = Position.Relative;

            var image = new Image();
            image.style.width = 350;
            image.style.height = 450;
            image.image = previewTexture;

            previewBox.Add(image);

            var previewLabel = new Label("Preview");
            previewLabel.style.position = Position.Absolute;
            previewLabel.style.top = -8; 
            previewLabel.style.left = 8;
            previewLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            previewLabel.style.fontSize = 11;
            previewLabel.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.1f, 1f));
            previewLabel.style.paddingLeft = 4;
            previewLabel.style.paddingRight = 4;

            previewBox.Add(previewLabel);

            card.Add(previewBox);

            backgroundSpriteField.RegisterValueChangedCallback(evt =>
            {
                backgroundImage.sprite = workingCopy.promptBackgroundSprite = evt.newValue as Sprite;
                RefreshPreview();
            });

            fontField.RegisterValueChangedCallback(evt =>
            {
                TMP_FontAsset fontAsset = workingCopy.promptFont = evt.newValue as TMP_FontAsset;
                titleText.font = fontAsset;
                questionText.font = fontAsset;
                feelingText.font = fontAsset;
                submitText.font = fontAsset;

                RefreshPreview();
            });

            return card;
        }

        void ApplyConfigToPreview()
        {
            backgroundImage.sprite = workingCopy.promptBackgroundSprite;

            titleText.font = workingCopy.promptFont;
            questionText.font = workingCopy.promptFont;
            feelingText.font = workingCopy.promptFont;
            submitText.font = workingCopy.promptFont;
        }

        void RefreshPreview()
        {
            ApplyConfigToPreview();
            previewCamera.Render();
        }

        void OnDisable()
        {
            if (previewInstance != null)
                DestroyImmediate(previewInstance);

            if (previewTexture != null)
                previewTexture.Release();
        }

        void CreatePreview()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Packages/com.nx10.sdk/NX10/Prefabs/NX10PromptPreview.prefab");

            previewInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            previewInstance.hideFlags = HideFlags.HideAndDontSave;

            previewCamera = previewInstance.GetComponentInChildren<Camera>();

            previewTexture = new RenderTexture(350, 450, 16);
            previewCamera.targetTexture = previewTexture;
            string sliderPathPrefix = "Canvas/prefab_SliderPrompt/Image";
            titleText = previewInstance.transform.Find(sliderPathPrefix + "/Title")
                .GetComponent<TextMeshProUGUI>();

            questionText = previewInstance.transform.Find(sliderPathPrefix + "/PromptText")
                .GetComponent<TextMeshProUGUI>();

            feelingText = previewInstance.transform.Find(sliderPathPrefix + "/GameObject/EmotionText")
                .GetComponent<TextMeshProUGUI>();

            submitText = previewInstance.transform.Find(sliderPathPrefix + "/Submit/TextMeshPro Text")
                .GetComponent<TextMeshProUGUI>();

            backgroundImage = previewInstance.transform.Find(sliderPathPrefix)
                .GetComponent<UnityEngine.UI.Image>();
        }


        VisualElement CreateApplyButton()
        {
            var button = new Button(() =>
            {
                Undo.RecordObject(config, "NX10 Config Changes");

                EditorUtility.CopySerialized(workingCopy, config);
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();

                Apply();
                statusLabel.text = "✔ Configuration Applied";
            })
            {
                text = "Apply Changes"
            };

            button.AddToClassList("primary-button");

            return button;
        }

        VisualElement CreateDiscardButton()
        {
            var discardButton = new Button(() =>
            {
                DiscardChanges();
            })
            {
                text = "Discard"
            };

            return discardButton;
        }

        public override void DiscardChanges()
        {
            base.DiscardChanges();

            EditorUtility.CopySerialized(config, workingCopy);
            RebuildUIFromWorkingCopy();
            RefreshPreview();
        }

        void RebuildUIFromWorkingCopy()
        {
            fontField.SetValueWithoutNotify(workingCopy.promptFont);
            backgroundSpriteField.SetValueWithoutNotify(workingCopy.promptBackgroundSprite);
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