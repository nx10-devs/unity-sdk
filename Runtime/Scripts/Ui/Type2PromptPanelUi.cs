using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NX10
{
    public class Type2PromptPanelUi : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI questionText;
        [SerializeField] private PromptButton promptButtonBase;
        [SerializeField] private Button submitButton, dismissButton;

        private List<PromptButton> promptButtons = new List<PromptButton>();
        private List<SAAQOption> selectedOptions;

        private bool isMultiSelect;
        Type2PromptUi promptUi;

        public void OnOpen(Type2PromptUi promptUi, SAAQBlock promptData, bool dismissable)
        {
            gameObject.SetActive(true);

            selectedOptions = new List<SAAQOption>();
            this.isMultiSelect = promptData.multipleSelect;
            this.promptUi = promptUi;

            promptButtonBase.gameObject.SetActive(true);

            if(submitButton)
            {
                submitButton.gameObject.SetActive(isMultiSelect);
                submitButton.interactable = false;
            }
                
            foreach (SAAQOption option in promptData.options)
            {
                PromptButton promptButton = Instantiate(promptButtonBase, promptButtonBase.transform.parent);
                promptButtons.Add(promptButton);
                promptButton.Initialise(option);
                promptButton.pressed += ButtonPressed;
            }

            promptButtonBase.gameObject.SetActive(false);

            questionText.text = promptData.questionText;
            dismissButton.gameObject.SetActive(dismissable);
        }

        public void OnClose()
        {
            foreach (PromptButton promptButton in promptButtons)
            {
                promptButton.pressed -= ButtonPressed;
                Destroy(promptButton.gameObject);
            }

            promptButtons.Clear();

            gameObject.SetActive(false);
        }

        private void ButtonPressed(SAAQOption option)
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

            if (isMultiSelect)
            {
                submitButton.interactable = (selectedOptions.Count > 0);
            }
            else
            {
                promptUi.SingleAnswerChosen(option, selectedOptions);
            }
        }

        public void SubmitPressed()
        {
            promptUi.SubmitAnswer(selectedOptions);
        }
    }
}
