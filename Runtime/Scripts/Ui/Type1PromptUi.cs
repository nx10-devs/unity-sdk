using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NX10
{
    public class Type1PromptUi : PromptUi
    {
        [SerializeField] private TextMeshProUGUI leftAnchorText, rightAnchorText;
        [SerializeField] private Slider Slider;
        [SerializeField] private Button submitButton;

        private SAAQAnswer currentAnswer;

        public override void Initialise(NX10PromptManager promptManager)
        {
            base.Initialise(promptManager);
        }

        public override void OnOpen(SAAQData promptData)
        {
            base.OnOpen(promptData);

            currentAnswer = new SAAQAnswer();

            Slider.minValue = 0;
            Slider.maxValue = promptData.prompt.rangeSize.Value;
            Slider.value = promptData.prompt.startingValue.Value;

            submitButton.interactable = promptData.prompt.confirmButtonEnabled.Value;

            currentAnswer.data.selectedValue = (int)Slider.value;
            currentAnswer.data.selectedValues = null;

            leftAnchorText.text = promptData.prompt.leftAnchorValue;
            rightAnchorText.text = promptData.prompt.rightAnchorValue;
        }

        public void OnSliderChange(Slider slider)
        {
            currentAnswer.data.selectedValue = (int)Slider.value;
            submitButton.interactable = true;
        }

        public void DismissPressed()
        {
            OnDismiss(currentAnswer);
        }

        public void SubmitPressed()
        {
            Submit(currentAnswer);
        }
    }
}

