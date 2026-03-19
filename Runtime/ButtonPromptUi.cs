using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace NX10
{
    public class ButtonPromptUi : PromptUi
    {
        [SerializeField] private PromptButton promptButtonBase;

        private List<PromptButton> promptButtons = new List<PromptButton>();

        public override void Initialise(NX10PromptManager promptManager)
        {
            base.Initialise(promptManager);
        }

        public override void OnOpen(SAAQPrompt prompt)
        {
            base.OnOpen(prompt);

            promptButtonBase.gameObject.SetActive(true);

            foreach (SAAQAnswer answer in prompt.answers)
            {
                PromptButton promptButton = Instantiate(promptButtonBase, promptButtonBase.transform.parent);
                promptButtons.Add(promptButton);
                promptButton.Initialise(answer);
                promptButton.pressed += ButtonPressed;
            }

            promptButtonBase.gameObject.SetActive(false);
        }

        public void ButtonPressed(SAAQAnswer answer)
        {
            foreach(PromptButton promptButton in promptButtons)
            {
                promptButton.pressed -= ButtonPressed;
            }

            promptButtons.Clear();
            Submit(answer);
        }
    }

}
