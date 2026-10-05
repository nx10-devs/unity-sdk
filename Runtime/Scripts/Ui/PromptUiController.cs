using System;
using System.Collections.Generic;
using UnityEngine;

namespace NX10
{ 
    public class PromptUiController : MonoBehaviour
    {
        private const string type1key = "prefab_prompt_type_1";
        private const string type2key = "prefab_prompt_type_2";

        private NX10PromptManager promptManager;

        private Dictionary<PromptType, PromptUi> promptUiDict = new Dictionary<PromptType, PromptUi>();

        private PromptUi currentPrompt;

        public event Action<SAAQAnswer> onSAAQSubmitted;

        public void Initialise(NX10PromptManager promptManager)
        {
            this.promptManager = promptManager;

            GameObject type1PromptObject = Resources.Load<GameObject>(type1key);
            PromptUi type1PromptUi = Instantiate(type1PromptObject, transform).GetComponent<PromptUi>();

            type1PromptUi.Initialise(promptManager);
            type1PromptUi.onSubmit += Prompt_onSubmit;
            promptUiDict.Add(PromptType.SaaqType1, type1PromptUi);

            GameObject type2PromptObject = Resources.Load<GameObject>(type2key);
            PromptUi type2PromptUi = Instantiate(type2PromptObject, transform).GetComponent<PromptUi>();

            type2PromptUi.Initialise(promptManager);
            type2PromptUi.onSubmit += Prompt_onSubmit;
            promptUiDict.Add(PromptType.SaaqType2, type2PromptUi); 
        }

        private void Prompt_onSubmit(SAAQAnswer answer)
        {
            onSAAQSubmitted.Invoke(answer);
        }

        public void ShowPrompt(SAAQBlock blockData, bool dismissable, System.Action<SAAQAnswer> promptAnsweredAction)
        {
            PromptType promptType = ParsePromptType(blockData.blockType);
            currentPrompt = promptUiDict[promptType];
            currentPrompt.OnOpen(blockData, dismissable, promptAnsweredAction);
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
