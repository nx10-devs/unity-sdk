using System;
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

        public override void OnOpen(SAAQBlock promptData, bool dismissable, Action<SAAQAnswer> promptAnsweredAction)
        {
            base.OnOpen(promptData, dismissable, promptAnsweredAction);

            currentAnswer = new SAAQAnswer();

            Slider.minValue = 0;
            Slider.maxValue = promptData.rangeSize;
            Slider.value = promptData.startingValue;

            submitButton.interactable = promptData.confirmButtonEnabled;

            currentAnswer.data.selectedValue = (int)Slider.value;
            currentAnswer.data.selectedValues = null;

            leftAnchorText.text = promptData.leftAnchorValue;
            rightAnchorText.text = promptData.rightAnchorValue;
        }

        public void OnSliderChange(Slider slider)
        {
            if(currentAnswer != null) 
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

