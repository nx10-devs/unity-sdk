#if UNITY_EDITOR

using TMPro;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace NX10
{
    public class NX10ConfigWindow : EditorWindow
    {
        private NX10Config config;
        private NX10Config workingCopy;

        private Label statusLabel;


        [MenuItem("Window/NX10/Configuration")]
        public static void Open()
        {
            var wnd = GetWindow<NX10ConfigWindow>();
            wnd.titleContent = new GUIContent("Config");
        }

        static Texture2D LoadLogo()
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Packages/com.nx10.sdk/NX10/Scripts/Config/Editor/nx10_logo.png");
        }

        private void OnDestroy()
        {
            if (workingCopy != null)
            {
                DestroyImmediate(workingCopy);
            }
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();

            config = Resources.Load<NX10Config>("NX10Package_Config");

            if (!config)
            {
                rootVisualElement.Add(new Label("Config not found in Resources folder."));
                return;
            }

            if (workingCopy == null)
            {
                workingCopy = ScriptableObject.Instantiate(config);
                workingCopy.name = config.name;
                workingCopy.hideFlags = HideFlags.HideAndDontSave; 
            }

            var script = MonoScript.FromScriptableObject(this);
            var scriptPath = AssetDatabase.GetAssetPath(script);
            var folder = System.IO.Path.GetDirectoryName(scriptPath);
            var ussPath = System.IO.Path.Combine(folder, "NX10ConfigWindow.uss");

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);

            if (styleSheet != null)
                rootVisualElement.styleSheets.Add(styleSheet);
            else
                Debug.LogWarning("NX10 USS file not found at: " + ussPath);

            if (styleSheet != null)
                rootVisualElement.styleSheets.Add(styleSheet);

            rootVisualElement.Add(CreateHeader());
            rootVisualElement.Add(CreateApiSection());
            rootVisualElement.Add(CreateApplyButton());
            rootVisualElement.Add(CreateDiscardButton());
            rootVisualElement.Add(CreateFooter());

            RefreshUIState();
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
            var root = new VisualElement();

            var keysCard = CreateCard("API KEYS");

            var stagingField = new TextField("Staging Key") { value = config.stagingApiKey, name = "staging-field" };
            stagingField.RegisterValueChangedCallback(evt => {

                workingCopy.stagingApiKey = evt.newValue;
                RefreshUIState();
            });

            var prodField = new TextField("Production Key") { value = config.productionApiKey, name = "prod-field" };
            prodField.RegisterValueChangedCallback(evt => {

                workingCopy.productionApiKey = evt.newValue;
                RefreshUIState();
            });

            keysCard.Add(stagingField);
            keysCard.Add(prodField);
            root.Add(keysCard);

            var routingCard = CreateCard("ENVIRONMENT ROUTING");
            routingCard.Add(new Label("Choose which key to use for each build type:"));

            routingCard.Add(CreateRouteField("Unity Editor", config.editorTarget,
                val => workingCopy.editorTarget = (KeyType)val));

            routingCard.Add(CreateRouteField("Development Builds", config.devBuildTarget,
                val => workingCopy.devBuildTarget = (KeyType)val));

            routingCard.Add(CreateRouteField("Release Builds", config.releaseBuildTarget,
                val => workingCopy.releaseBuildTarget = (KeyType)val));

            root.Add(routingCard);

            statusLabel = new Label();
            statusLabel.AddToClassList("status-label");
            root.Add(statusLabel);

            return root;
        }

        VisualElement CreateRouteField(string label, KeyType current, System.Action<object> onChange)
        {
            var row = new VisualElement();
            row.AddToClassList("route-row");

            var nameLabel = new Label(label);
            nameLabel.AddToClassList("route-label");

            var enumField = new EnumField(current) { name = label };
            enumField.AddToClassList("route-enum");

            enumField.RegisterValueChangedCallback(evt => {
                onChange(evt.newValue);
                RefreshUIState(); 
            });

            row.Add(nameLabel);
            row.Add(enumField);
            return row;
        }

        VisualElement CreateApplyButton()
        {
            var button = new Button(() =>
            {
                Undo.RecordObject(config, "NX10 Config Changes");

                EditorUtility.CopySerialized(workingCopy, config);
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();

                statusLabel.text = "✔ Configuration Saved";
                RefreshUIState();
            })
            {
                text = "Save Changes"
            };

            button.AddToClassList("primary-button");

            return button;
        }

        VisualElement CreateDiscardButton()
        {
            var discardButton = new Button(() =>
            {
                DiscardChanges();
                RefreshUIState();
            })
            {
                text = "Discard"
            };

            discardButton.AddToClassList("discard-button");
            return discardButton;
        }

        public override void DiscardChanges()
        {
            base.DiscardChanges();

            EditorUtility.CopySerialized(config, workingCopy);

            var stagingField = rootVisualElement.Q<TextField>("staging-field");
            var prodField = rootVisualElement.Q<TextField>("prod-field");

            if (stagingField != null) stagingField.SetValueWithoutNotify(workingCopy.stagingApiKey);
            if (prodField != null) prodField.SetValueWithoutNotify(workingCopy.productionApiKey);

            rootVisualElement.Q<EnumField>("Unity Editor")?.SetValueWithoutNotify(workingCopy.editorTarget);
            rootVisualElement.Q<EnumField>("Development Builds")?.SetValueWithoutNotify(workingCopy.devBuildTarget);
            rootVisualElement.Q<EnumField>("Release Builds")?.SetValueWithoutNotify(workingCopy.releaseBuildTarget);

            RefreshUIState();
        }

        VisualElement CreateFooter()
        {
            var footer = new Label($"NX10 SDK v{GetSdkVersion()}"); footer.AddToClassList("footer");
            return footer;

            string GetSdkVersion()
            {
                var assembly = typeof(NX10ConfigWindow).Assembly;
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

        private bool HasUnsavedChanges()
        {
            return workingCopy.stagingApiKey != config.stagingApiKey ||
                   workingCopy.productionApiKey != config.productionApiKey ||
                   workingCopy.editorTarget != config.editorTarget ||
                   workingCopy.devBuildTarget != config.devBuildTarget ||
                   workingCopy.releaseBuildTarget != config.releaseBuildTarget;
        }

        private void RefreshUIState()
        {
            bool changed = HasUnsavedChanges();

            var wnd = GetWindow<NX10ConfigWindow>();
            string title = changed ? "Config*" : "Config";
            wnd.titleContent = new GUIContent(title);

            var applyBtn = rootVisualElement.Q<Button>(className: "primary-button");
            var discardBtn = rootVisualElement.Q<Button>(className: "discard-button");

            if(changed)
                statusLabel.text = string.Empty;

            applyBtn.SetEnabled(changed);
            discardBtn.SetEnabled(changed);
        }
    }
}
#endif