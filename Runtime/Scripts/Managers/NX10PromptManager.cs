using System;
using UnityEngine;

namespace NX10
{
    public enum PromptType
    {
        SaaqType1,
        SaaqType2,
        SaaqType3,
        SaaqType4,
        SaaqType5,
    }

    public enum FeelingType
    {
        VeryFrustrated = 0,
        Frustrated = 1,
        Neutral = 2,
        Enjoyment = 3,
        Ecstatic = 4,
        Bored = 5,
        Surprised = 6,
        Relaxed = 7,
        Fun = 8,
    }

    public class NX10PromptManager : MonoBehaviour
    {
        [SerializeField] private PromptUiController uiController;
        public PromptUiController PromptUiController => uiController;

       

        private void Start()
        {
            PromptUiController.Initialise(this);
        }

        public void ShowPrompt(SAAQBlock promptData, bool dismissable, Action<SAAQAnswer> promptAnsweredAction)
        {
            uiController.ShowPrompt(promptData, dismissable, (answer) =>
            {
                promptAnsweredAction(answer);
            });
        }
    }
}

