using System;
using UnityEngine;

namespace NX10
{
    public class PromptUi : MonoBehaviour
    {
        protected NX10PromptManager _manager;
        public event Action<FeelingType> onSubmit;

        public virtual void Initialise(NX10PromptManager promptManager)
        {
            if (_manager == null)
            {
                _manager = promptManager;
            }
        }

        public virtual void OnOpen()
        {
            gameObject.SetActive(true);
        }

        public virtual void OnClose()
        {
            gameObject.SetActive(false);
        }

        protected void Submit(FeelingType feelingType)
        {
            onSubmit.Invoke(feelingType);
            OnClose();
        }
    }

}
