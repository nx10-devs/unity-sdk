using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NX10
{
    public class PromptUi : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI questionText, timerText;
        [SerializeField] private Button dismissButton;
        [SerializeField] private Image timerImage;

        protected NX10PromptManager _manager;

        public event Action<SAAQAnswer> onSubmit;

        private float displayTimeLeft;
        private float displayTimeTotal;
        private bool shouldCountdown;

        public virtual void Initialise(NX10PromptManager promptManager)
        {
            if (_manager == null)
            {
                _manager = promptManager;
            }
        }

        private void Update()
        {
            UpdateTimer();
        }

        private void UpdateTimer()
        {
            if(shouldCountdown)
            {
                UpdateTimerView();
                displayTimeLeft -= Time.deltaTime;
                if (displayTimeLeft <= 0)
                {

                }
            }
        }

        private void UpdateTimerView()
        {
            timerImage.fillAmount = displayTimeTotal / displayTimeLeft;
            timerText.text = FormatTime(displayTimeLeft);
        }

        public static string FormatTime(float seconds, bool includeMs = false)
        {
            bool isNegative = seconds < 0;
            float absoluteSeconds = Mathf.Abs(seconds);

            TimeSpan time = TimeSpan.FromSeconds(absoluteSeconds);

            string baseTime = string.Format("{0:00}:{1:02}:{2:02}",
                Mathf.FloorToInt((float)time.TotalHours),
                time.Minutes,
                time.Seconds);

            if (includeMs)
            {
                baseTime += string.Format(".{0:000}", time.Milliseconds);
            }

            return isNegative ? $"-{baseTime}" : baseTime;
        }

        public virtual void OnOpen(SAAQBlock promptData, float timer, bool dismissable, Action<SAAQAnswer> promptAnsweredAction)
        {
            onSubmit = promptAnsweredAction;

            if(timer > 0)
            {
                displayTimeTotal = timer;
                displayTimeLeft = timer;
                shouldCountdown = true;

            } else shouldCountdown = false;


            if (questionText)
                questionText.text = promptData.questionText;

            if(dismissButton)
                dismissButton.gameObject.SetActive(dismissable);

            gameObject.SetActive(true);
        }

        public virtual void OnClose()
        {
            gameObject.SetActive(false);
        }

        protected void OnDismiss(SAAQAnswer answer)
        {
            answer.type = "dismissed";

            onSubmit.Invoke(answer);
            OnClose();
        }


        protected void Submit(SAAQAnswer answer)
        {
            onSubmit.Invoke(answer);
            OnClose();
        }
    }

}
