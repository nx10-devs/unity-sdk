using System;
using System.Collections.Generic;
using UnityEngine;

namespace NX10
{ 
    public class PromptUiController : MonoBehaviour
    {
        [Serializable]
        public class PromptWithType
        {
            public PromptType Type;
            public PromptUi Prompt;
        }

        [Serializable]
        public class FeelingWithSprite
        {
            public FeelingType Type;
            public Sprite sprite;
        }

        private NX10PromptManager promptManager;

        [SerializeField] private List<PromptWithType> prompts;
        private Dictionary<PromptType, PromptUi> promptUiDict = new Dictionary<PromptType, PromptUi>();

        private PromptUi currentPrompt;

        public event Action<SAAQAnswer> onSAAQSubmitted;

        public void Initialise(NX10PromptManager promptManager)
        {
            this.promptManager = promptManager;

            foreach (PromptWithType promptWithType in prompts)
            {
                promptUiDict.Add(promptWithType.Type, promptWithType.Prompt);
                promptWithType.Prompt.Initialise(promptManager);
                promptWithType.Prompt.onSubmit += Prompt_onSubmit;
            }
        }

        private void Prompt_onSubmit(SAAQAnswer answer)
        {
            onSAAQSubmitted.Invoke(answer);
        }

        public void ShowPrompt(SAAQData data)
        {
            PromptType promptType = ParsePromptType(data.prompt.blockType);
            currentPrompt = promptUiDict[promptType];
            currentPrompt.OnOpen(data);
        }

        private PromptType ParsePromptType(string promptType)
        {
            if (Enum.TryParse(promptType, true, out PromptType result))
            {
                return result;
            }

            throw new Exception("cant find prompt type " + promptType);
        }
    }
}
