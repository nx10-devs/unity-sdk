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

        private string promptType = "modal2";
        private string feelingContext = "menu";
        private string feelingFor = "coins";
        private string promptDisplayTimestamp = "";
        private string promptAnswerTimestamp = "";

        public SAAQAnswer[] currentSaaqAnswers { get; private set; }

        public Action<SAAQAnswer, string, string, string, string, string> sendSaaqDataRequest;


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

            sendSaaqDataRequest?.Invoke(answer, promptType, feelingContext, feelingFor, promptDisplayTimestamp, promptAnswerTimestamp);
            promptCompleteAction.Invoke(ParseFeeling(answer.feelingsType));
        }

        public void ForceClosePrompt()
        {
            uiController.ForceClosePrompt();
        }

        public void ShowPrompt(SAAQPrompt prompt)
        {
            promptDisplayTimestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            this.promptType = prompt.type;
            this.currentSaaqAnswers = prompt.answers.ToArray();

            this.feelingContext = string.Empty;
            this.feelingFor = string.Empty;

            PromptType promptType = ParsePromptType(prompt.type);
            uiController.ShowPrompt(promptType);
        }

        private PromptType ParsePromptType(string promptType)
        {
            if (Enum.TryParse(promptType, true, out PromptType result))
            {
                return result;
            }

            throw new Exception("cant find prompt type " + promptType);
        }

        private FeelingType ParseFeeling(string feeling)
        {
            if (Enum.TryParse(feeling, true, out FeelingType result))
            {
                return result;
            }

            throw new Exception("cant find feeling type " +  feeling);
        }
    }
}

