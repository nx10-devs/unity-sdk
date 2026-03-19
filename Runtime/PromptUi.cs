using System;
using TMPro;
using UnityEngine;

namespace NX10
{
    public class PromptUi : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI questionText;
        protected NX10PromptManager _manager;
        public event Action<SAAQAnswer> onSubmit;

        public virtual void Initialise(NX10PromptManager promptManager)
        {
            if (_manager == null)
            {
                _manager = promptManager;
            }
        }

        public virtual void OnOpen(SAAQPrompt prompt)
        {
            questionText.text = prompt.questionText;
            gameObject.SetActive(true);
        }

        public virtual void OnClose()
        {
            gameObject.SetActive(false);
        }

        protected void Submit(SAAQAnswer answer)
        {
            onSubmit.Invoke(answer);
            OnClose();
        }
    }

}
