using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NX10
{
    public class NX10PromptThemeApplier : MonoBehaviour
    {
        [SerializeField] private Image backGroundImage;
        private TextMeshProUGUI[] texts;

        public void ApplyConfig(NX10PackageConfig config)
        {
            backGroundImage.sprite = config.promptBackgroundSprite;
            texts = GetComponentsInChildren<TextMeshProUGUI>();
            foreach(TextMeshProUGUI text in texts)
            {
                text.font = config.promptFont;
            }

        }
    }
}

