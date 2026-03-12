using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NX10
{
    public class SliderPromptUi : PromptUi
    {
        [SerializeField] private TextMeshProUGUI SliderEmotionText;
        [SerializeField] private Slider Slider; 

        private FeelingType currentFeelingType;

        public override void OnOpen()
        {
            base.OnOpen();

            Slider.minValue = 0;
            Slider.maxValue = _manager.currentFeelingTypesToShow.Length -1;

            Slider.value = (Slider.maxValue - Slider.minValue) / 2.0f;
        }

        public void OnSliderChange(Slider slider)
        {
            if(_manager.currentFeelingTypesToShow != null)
                currentFeelingType = _manager.currentFeelingTypesToShow[(int)slider.value];

            SliderEmotionText.text = currentFeelingType.ToString();
        }

        public void SubmitPressed()
        {
            Submit(currentFeelingType);
        }
    }
}

