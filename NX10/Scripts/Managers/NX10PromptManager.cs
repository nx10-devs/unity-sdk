using System;
using UnityEngine;

namespace NX10
{
    public enum PromptType
    {
        Slider,
        Button
    }

    public enum FeelingType
    {
        VeryFrustrated = 0,
        Frustated = 1,
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

        private Action<FeelingType> promptCompleteAction;

        private string feelingModalType = "modal2";
        private string feelingContext = "menu";
        private string feelingFor = "coins";
        private string promptDisplayTimestamp = "";
        private string promptAnswerTimestamp = "";

        public FeelingType[] currentFeelingTypesToShow { get; private set; }

        public Action<string, int, string, string, string, string, string> sendSaaqDataRequest;


        private void Start()
        {
            PromptUiController.Initialise(this);
            PromptUiController.onSAAQSubmitted += PromptUiController_onSAAQSubmitted;
        }

        private void OnDestroy()
        {
            PromptUiController.onSAAQSubmitted -= PromptUiController_onSAAQSubmitted;
        }

        private void PromptUiController_onSAAQSubmitted(FeelingType feelingType)
        {
            promptAnswerTimestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            sendSaaqDataRequest?.Invoke(feelingType.ToString(), (int)feelingType, feelingModalType, feelingContext, feelingFor, promptDisplayTimestamp, promptAnswerTimestamp);
            promptCompleteAction.Invoke(feelingType);
        }

        public void ForceClosePrompt()
        {
            uiController.ForceClosePrompt();
        }

        public void ShowPrompt(PromptType type, string feelingModalType, string feelingContext, string feelingFor, Action<FeelingType> promptCompleteAction)
        {
            promptDisplayTimestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            this.feelingModalType = feelingModalType;
            this.feelingContext = feelingContext;
            this.feelingFor = feelingFor;
            this.promptCompleteAction = promptCompleteAction;

            uiController.ShowPrompt(type);
        }

        public void ShowSlider(FeelingType[] typesToShow, string feelingContext, string feelingFor, Action<FeelingType> completeAction)
        {
            currentFeelingTypesToShow = typesToShow;
            ShowPrompt(PromptType.Slider, "modal2", feelingContext, feelingFor, completeAction);
        }

        public void ShowButton(FeelingType[] typesToShow, string feelingContext, string feelingFor, Action<FeelingType> completeAction)
        {
            currentFeelingTypesToShow = typesToShow;
            ShowPrompt(PromptType.Button, "modal1", feelingContext, feelingFor, completeAction);
        }
    }
}

