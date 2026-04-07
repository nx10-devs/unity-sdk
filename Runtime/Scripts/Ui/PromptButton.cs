using TMPro;
using UnityEngine;

namespace NX10
{
    public class PromptButton : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI feelingText;
        private SAAQAnswer answer;

        public System.Action<SAAQAnswer> pressed;

        public void Initialise(SAAQAnswer answer)
        {
            this.answer = answer;
            //feelingText.text = answer.displayName;
        }

        public void OnPressed()
        {
            pressed?.Invoke(answer);
        }
    }
}
