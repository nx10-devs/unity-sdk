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
                    OnClose();
                }
            }
        }

        private void UpdateTimerView()
        {
            timerImage.fillAmount = displayTimeLeft / displayTimeTotal;
            timerText.text = FormatTime(displayTimeLeft);
        }

        public static string FormatTime(float seconds)
        {
            int wholeSeconds = Mathf.FloorToInt(seconds);

            return wholeSeconds.ToString("0") + "s";
        }

        public virtual void OnOpen(SAAQBlock promptData, float timer, bool dismissable, Action<SAAQAnswer> promptAnsweredAction)
        {
            onSubmit = promptAnsweredAction;

            if (timer > 0)
            {
                displayTimeTotal = timer;
                displayTimeLeft = timer;

                timerImage.gameObject.SetActive(true);
                timerText.gameObject.SetActive(true);

                shouldCountdown = true;

            }
            else
            {
                shouldCountdown = false;
                timerImage.gameObject.SetActive(false);
                timerText.gameObject.SetActive(false);
            }

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
            //OnClose();
        }


        protected void Submit(SAAQAnswer answer)
        {
            onSubmit.Invoke(answer);
            OnClose();
        }
    }

}
