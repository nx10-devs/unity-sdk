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

        private Action<SAAQAnswer> promptAnsweredAction;

        private string triggerId;
        private string promptDisplayTimestamp = "";
        private string promptAnswerTimestamp = "";

        private void Start()
        {
            PromptUiController.Initialise(this);
            PromptUiController.onSAAQSubmitted += PromptUiController_onSAAQSubmitted;
        }

        private void OnDestroy()
        {
            PromptUiController.onSAAQSubmitted -= PromptUiController_onSAAQSubmitted;
        }

        private void PromptUiController_onSAAQSubmitted(SAAQAnswer answer)
        {
            promptAnswerTimestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            promptAnsweredAction.Invoke(answer);
        }

        public void ShowPrompt(SAAQData promptData, Action<SAAQAnswer> promptAnsweredAction)
        {
            this.promptDisplayTimestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            this.triggerId = promptData.triggerID;
            this.promptAnsweredAction = promptAnsweredAction;

            uiController.ShowPrompt(promptData);
        }
    }
}

