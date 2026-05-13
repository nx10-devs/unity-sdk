using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NX10
{
    public enum ScreenOrientation
    {
        Landscape,
        Portrait,
    }

    public enum Prompt2Type
    {
        MultipleSelect,
        SingleSelect
    }

    [Serializable]
    public class  Prompt2UiDefinition
    {
        public ScreenOrientation orientation;
        public Prompt2Type type;
        public Type2PromptPanelUi panelUi;
    }

    public class Type2PromptUi : PromptUi
    {
        [SerializeField] private List<Prompt2UiDefinition> uiDefinitions; 

        private Vector2 lastScreenSize;

        private List<PromptButton> promptButtons = new List<PromptButton>();

        private SAAQBlock promptData;
        private bool dismissable;

        private SAAQAnswer currentAnswer;
        private Type2PromptPanelUi currentPanel;

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

            currentAnswer = new SAAQAnswer();
            currentAnswer.data.selectedValue = null;

            currentPanel = GetCorrectPanel();
            currentPanel.OnOpen(this, promptData, dismissable);
        }

        public override void OnClose()
        {
            base.OnClose();

            currentPanel.OnClose();
        }

        public void SingleAnswerChosen(SAAQOption option, List<SAAQOption> selectedOptions)
        {
            currentAnswer.type = "answered";
            CheckFollowUp(option, selectedOptions);
        }

        private void CheckFollowUp(SAAQOption option, List<SAAQOption> selectedOptions)
        {
            gameObject.SetActive(false);
            if (option.followonQuestion.Count > 0)
            {
                NX10Manager.Instance.ShowPrompt(option.followonQuestion[0], dismissable, (answer, displayTimestamp, answeredTimestamp) =>
                {
                    SelectedFeeling selectedFeeling = new SelectedFeeling();
                    selectedFeeling.feelingType = option.feeling.feelingsType.ToString().ToLower();

                    if (answer.type == "answered")
                    {
                        selectedFeeling.followonAnswer = new FollowonAnswer()
                        {
                            selectedValue = answer.data.selectedValue.Value
                        };
                    }
                    else currentAnswer.type = "partial";

                    currentAnswer.data.selectedValues = new List<SelectedFeeling>()
                    {
                        selectedFeeling
                    };

                    Submit(currentAnswer);
                });
            }
            else
            {
                SubmitAnswer(selectedOptions);
            }
        } 

        public void DismissPressed()
        {
            OnDismiss(new SAAQAnswer());
        }

        public void SubmitAnswer(List<SAAQOption> selectedOptions)
        {
            currentAnswer.data.selectedValues = new List<SelectedFeeling>();
            foreach (SAAQOption option in selectedOptions)
            {
                SelectedFeeling selectedFeeling = new SelectedFeeling();
                selectedFeeling.feelingType = option.feeling.feelingsType.ToString().ToLower();
                selectedFeeling.followonAnswer = null;

                currentAnswer.data.selectedValues.Add(selectedFeeling);
            }

            currentAnswer.type = "answered";
            Submit(currentAnswer);
        }

        private Type2PromptPanelUi GetCorrectPanel()
        {
            bool isLandscape = Screen.width > Screen.height;
            ScreenOrientation screenOrientation = isLandscape ? ScreenOrientation.Landscape : ScreenOrientation.Portrait;
            Prompt2Type prompt2Type = isMultiSelect ? Prompt2Type.MultipleSelect : Prompt2Type.SingleSelect;

            return uiDefinitions.Find(item => item.type == prompt2Type && item.orientation == screenOrientation).panelUi;
        }
    }

}
