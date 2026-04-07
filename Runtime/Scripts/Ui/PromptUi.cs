using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NX10
{
    public class PromptUi : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI questionText;
        [SerializeField] private Button dismissButton;

        protected NX10PromptManager _manager;

        public event Action<SAAQAnswer> onSubmit;

        public virtual void Initialise(NX10PromptManager promptManager)
        {
            if (_manager == null)
            {
                _manager = promptManager;
            }
        }

        public virtual void OnOpen(SAAQBlock promptData, bool dismissable, Action<SAAQAnswer> promptAnsweredAction)
        {
            onSubmit = promptAnsweredAction;

            questionText.text = promptData.questionText;
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
