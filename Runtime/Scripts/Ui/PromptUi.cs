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

        public virtual void OnOpen(SAAQData promptData)
        {
            questionText.text = promptData.prompt.questionText;
            dismissButton.gameObject.SetActive(promptData.dismissable);

            gameObject.SetActive(true);
        }

        public virtual void OnClose()
        {
            gameObject.SetActive(false);
        }

        protected void OnDismiss(SAAQAnswer answer)
        {
            answer.type = "dismissed";
            answer.data = null;

            onSubmit.Invoke(answer);
            OnClose();
        }


        protected void Submit(SAAQAnswer answer)
        {
            answer.type = "answered";

            onSubmit.Invoke(answer);
            OnClose();
        }
    }

}
