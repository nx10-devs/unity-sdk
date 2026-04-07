using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NX10
{
    public class Type2PromptUi : PromptUi
    {
        [SerializeField] private PromptButton promptButtonBase;
        [SerializeField] private Button submitButton;

        private List<PromptButton> promptButtons = new List<PromptButton>();

        private SAAQBlock promptData;
        private bool dismissable;

        private List<SAAQOption> selectedOptions;

        private SAAQAnswer currentAnswer;

        private bool isMultiSelect
        {
            get
            {
                return promptData.multipleSelect;
            }
        }

        public override void Initialise(NX10PromptManager promptManager)
        {
            base.Initialise(promptManager);
        }

        public override void OnOpen(SAAQBlock promptData, bool dismissable, Action<SAAQAnswer> promptAnsweredAction)
        {
            base.OnOpen(promptData, dismissable, promptAnsweredAction);

            this.promptData = promptData;
            this.dismissable = dismissable;
            selectedOptions = new List<SAAQOption>();

            currentAnswer = new SAAQAnswer();
            currentAnswer.data.selectedValue = null;

            promptButtonBase.gameObject.SetActive(true);
            submitButton.gameObject.SetActive(isMultiSelect);
            submitButton.interactable = false;

            foreach(SAAQOption option in promptData.options)
            {
                PromptButton promptButton = Instantiate(promptButtonBase, promptButtonBase.transform.parent);
                promptButtons.Add(promptButton);
                promptButton.Initialise(option);
                promptButton.pressed += ButtonPressed;
            }

            promptButtonBase.gameObject.SetActive(false);
        }

        public override void OnClose()
        {
            base.OnClose(); 

            foreach(PromptButton promptButton in promptButtons)
            {
                promptButton.pressed -= ButtonPressed;
                Destroy(promptButton.gameObject);
            }

            promptButtons.Clear();
        }

        public void ButtonPressed(SAAQOption option)
        {
            SelectOption(option);
        }

        private void SelectOption(SAAQOption option)
        {
            if (selectedOptions.Contains(option))
            {
                selectedOptions.Remove(option);
            }
            else
            {
                selectedOptions.Add(option);
            }

            if(isMultiSelect)
            {
                submitButton.interactable = (selectedOptions.Count > 0);
            }
            else
            {
                CheckFollowUp(option);
            }
        }

        private void CheckFollowUp(SAAQOption option)
        {
            gameObject.SetActive(false);
            if (option.followonQuestion != null)
            {
                NX10Manager.Instance.ShowPrompt(option.followonQuestion[0], dismissable, (answer) =>
                {
                    SelectedFeeling selectedFeeling = new SelectedFeeling()
                    {
                        feelingType = option.feeling.feelingsType.ToString(),
                        followonAnswer = new FollowonAnswer()
                        {
                            selectedValue = answer.data.selectedValue.Value
                        }
                    };

                    currentAnswer.data.selectedValues = new List<SelectedFeeling>()
                    {
                        selectedFeeling
                    };

                    Submit(currentAnswer);
                });
            }
            else
            {
                SubmitAnswer();
            }
        } 

        public void DismissPressed()
        {
            OnDismiss(new SAAQAnswer());
        }

        public void SubmitAnswer()
        {
            currentAnswer.data.selectedValues = new List<SelectedFeeling>();
            foreach (SAAQOption option in selectedOptions)
            {
                SelectedFeeling selectedFeeling = new SelectedFeeling();
                selectedFeeling.feelingType = option.feeling.feelingsType.ToString();
                selectedFeeling.followonAnswer = null;

                currentAnswer.data.selectedValues.Add(selectedFeeling);
            }

            Submit(currentAnswer);
        }
    }

}
