using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NX10
{
    public class SliderPromptUi : PromptUi
    {
        [SerializeField] private Image SliderEmotion;
        [SerializeField] private TextMeshProUGUI SliderEmotionText;

        private FeelingType currentFeelingType;

        public override void OnOpen()
        {
            base.OnOpen();


        }

        public void OnSliderChange(Slider slider)
        {
            switch (slider.value)
            {
                case 1:
                    currentFeelingType = FeelingType.VeryFrustrated;

                    break;
                case 2:
                    currentFeelingType = FeelingType.Frustated;
                    break;
                case 3:
                    currentFeelingType = FeelingType.Neutral;
                    break;
                case 4:
                    currentFeelingType = FeelingType.Enjoyment;
                    break;
                case 5:
                    currentFeelingType = FeelingType.Ecstatic;
                    break;
            }

            SliderEmotion.sprite = NX10Manager.Instance.GetSprite(currentFeelingType);
            SliderEmotionText.text = currentFeelingType.ToString();
        }

        public void SubmitPressed()
        {
            Submit(currentFeelingType);
        }
    }
}

