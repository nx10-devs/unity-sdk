using TMPro;
using UnityEngine;

namespace NX10
{
    public class PromptButton : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI feelingText;
        private SAAQOption option;

        public System.Action<SAAQOption> pressed;

        public void Initialise(SAAQOption option)
        {
            this.option = option;
            feelingText.text = option.feeling.displayName;
        }

        public void OnPressed()
        {
            pressed?.Invoke(option);
        }
    }
}
