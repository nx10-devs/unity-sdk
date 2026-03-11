using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace NX10
{
    public class ButtonPromptUi : PromptUi
    {
        [Serializable]
        public class FeelingButton
        {
            public FeelingType FeelingType;
            public Button button;
        }

        [SerializeField] private List<FeelingButton> feelingButtons;
        private Dictionary<FeelingType, Button> feelingButtonsDict = new Dictionary<FeelingType, Button>();

        public override void Initialise(NX10PromptManager promptManager)
        {
            base.Initialise(promptManager);

            foreach (FeelingButton feelingButton in feelingButtons)
            {
                feelingButtonsDict.Add(feelingButton.FeelingType, feelingButton.button);
                //feelingButton.button.image.sprite = NX10Manager.Instance.GetSprite(feelingButton.FeelingType);
            }
        }

        public override void OnOpen()
        {
            base.OnOpen();

            foreach (FeelingButton feelingButton in feelingButtons)
            {
                feelingButton.button.gameObject.SetActive(_manager.currentFeelingTypesToShow.Contains(feelingButton.FeelingType));
            }
        }

        public void ButtonPressed(int feelingTypeIndex)
        {
            FeelingType feelingType = (FeelingType) feelingTypeIndex;   
            Submit(feelingType);
        }
    }

}
