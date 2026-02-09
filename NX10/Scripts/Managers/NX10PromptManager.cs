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
        Ecstatic = 4
    }

    public class NX10PromptManager : MonoBehaviour
    {
        [SerializeField] private PromptUiController uiController;
        public PromptUiController PromptUiController => uiController;

        private Action<FeelingType> promptCompleteAction;

        private string feelingModalType = "modal2";
        private string feelingContext = "menu";
        private string feelingFor = "coins";

        private const string TOTAL_SAAQ_DONE = "TOTAL_SAAQ_DONE";

        public FeelingType[] currentFeelingTypeToShow { get; private set; }

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
            NX10Manager.Instance.SendSaaqPromptData(feelingType.ToString(), (int)feelingType, feelingModalType, feelingContext, feelingFor);

            AddSaaqToTotalCount();
            promptCompleteAction.Invoke(feelingType);
        }

        public void ForceClosePrompt()
        {
            uiController.ForceClosePrompt();
        }

        public void ShowPrompt(PromptType type, string feelingModalType, string feelingContext, string feelingFor, Action<FeelingType> promptCompleteAction)
        {
            if(ExperimentManager.Instance.experiment == ExperimentManager.Experiment.ExperimentA)
            {
                promptCompleteAction.Invoke(FeelingType.Neutral);
                return;
            }


            this.feelingModalType = feelingModalType;
            this.feelingContext = feelingContext;
            this.feelingFor = feelingFor;
            this.promptCompleteAction = promptCompleteAction;

            uiController.ShowPrompt(type);
        }

        public void ShowSlider(string feelingContext, string feelingFor, Action<FeelingType> completeAction)
        {
            ShowPrompt(PromptType.Slider, "modal2", feelingContext, feelingFor, completeAction);
        }

        public void ShowButton(FeelingType[] typeToShow, string feelingContext, string feelingFor, Action<FeelingType> completeAction)
        {
            currentFeelingTypeToShow = typeToShow;
            ShowPrompt(PromptType.Button, "modal1", feelingContext, feelingFor, completeAction);
        }

        public bool ShowSliderTimerDependant(string timerKey, int duration, string feelingContext, string feelingFor, Action<FeelingType> completeAction)
        {
            bool Show()
            {
                ShowSlider(feelingContext, feelingFor, completeAction);
                PlayerPrefs.SetString(timerKey, DateTime.Now.ToString());
                PlayerPrefs.SetInt(timerKey + "_duration", duration);
                return true;
            }

            string timeStamp = PlayerPrefs.GetString(timerKey, string.Empty);

            if (timeStamp == string.Empty)
            {
                return Show();
            }

            DateTime lastClaimTime = DateTime.Parse(timeStamp);
            TimeSpan timeElapsed = DateTime.Now - lastClaimTime;
            if (timeElapsed.TotalSeconds >= duration)
            {
                return Show();
            }

            return false;
        }

        public float GetRemainingCooldownTimeMinutes(string timerKey)
        {
            string timeStamp = PlayerPrefs.GetString(timerKey, string.Empty);
            DateTime lastClaimTime = DateTime.Parse(timeStamp);
            TimeSpan timeElapsed = DateTime.Now - lastClaimTime;
            int duration = PlayerPrefs.GetInt(timerKey + "_duration");

            int timeRemaining = duration - (int)timeElapsed.TotalSeconds;

            float remainingMinutes = Mathf.Ceil(((float)timeRemaining) / 60f);
            return remainingMinutes;
        }

        public int GetTotalSaaqDone()
        {
            return PlayerPrefs.GetInt(TOTAL_SAAQ_DONE, 0);
        }

        public void AddSaaqToTotalCount()
        {
            int totalSaaqDone = PlayerPrefs.GetInt(TOTAL_SAAQ_DONE, 0);
            int finalTotalSaaqDone = totalSaaqDone + 1;
            PlayerPrefs.SetInt(TOTAL_SAAQ_DONE, finalTotalSaaqDone);
            PlayerPrefs.Save();
        }
    }
}

