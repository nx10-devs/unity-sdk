using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NX10
{
    public class SliderPromptUi : PromptUi
    {
        [SerializeField] private TextMeshProUGUI SliderEmotionText;
        [SerializeField] private Slider Slider; 

        private SAAQAnswer currentAnswer;

        public override void OnOpen()
        {
            base.OnOpen();

            Slider.minValue = 0;
            Slider.maxValue = _manager.currentSaaqAnswers.Length -1;

            Slider.value = (Slider.maxValue - Slider.minValue) / 2.0f;
        }

        public void OnSliderChange(Slider slider)
        {
            if(_manager.currentSaaqAnswers != null)
                currentAnswer = _manager.currentSaaqAnswers[(int)slider.value];

            SliderEmotionText.text = currentAnswer.displayName.ToString();
        }

        public void SubmitPressed()
        {
            Submit(currentAnswer);
        }
    }
}

