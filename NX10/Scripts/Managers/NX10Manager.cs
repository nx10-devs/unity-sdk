using System;
using System.Collections.Generic;
using UnityEngine;
using static NX10.PromptUiController;

namespace NX10
{
    public class NX10Manager : NX10PersistentSingleton<NX10Manager>
    {
        private NX10PromptManager promptManager;
        private NX10BackendManager backendManager;

        [SerializeField] private List<FeelingWithSprite> feelingWithSprites;
        public Dictionary<FeelingType, Sprite> feelingSpriteDict = new Dictionary<FeelingType, Sprite>();

        protected override void Awake()
        {
            base.Awake();

            promptManager = GetComponentInChildren<NX10PromptManager>();
            backendManager = GetComponentInChildren<NX10BackendManager>();

            foreach (FeelingWithSprite feelingWithSprite in feelingWithSprites)
            {
                feelingSpriteDict.Add(feelingWithSprite.Type, feelingWithSprite.sprite);
            }
        }

        public Sprite GetSprite(FeelingType feelingType)
        {
            return feelingSpriteDict[feelingType];
        }

        /// <summary>
        /// should be used carefully, will call the prompts completionAction
        /// </summary>
        public void ForceClosePrompt()
        {
            promptManager.ForceClosePrompt();
        }

        public void UpdateNX10MetaData(string metaDataKey, string metaDataValue)
        {
            backendManager.UpdateNX10MetaData(metaDataKey, metaDataValue);
        }

        public void SetTelemetryCollection(bool canCollect)
        {
            backendManager.SetTelemetryCollection(canCollect);
        }

        public void SetDeviceIdOverride(string overrideDeviceId)
        {
            backendManager.SetOverrideDeviceId(overrideDeviceId);
        }

        public void ShowSlider(string feelingContext, string feelingFor, Action<FeelingType> completeAction)
        {
            promptManager.ShowSlider(feelingContext, feelingFor, completeAction);
        }

        public void ShowButton(FeelingType[] typeToShow, string feelingContext, string feelingFor, Action<FeelingType> completeAction)
        {
            promptManager.ShowButton(typeToShow, feelingContext, feelingFor, completeAction);
        }

        public bool ShowSliderTimerDependant(string timerKey, int duration, string feelingContext, string feelingFor, Action<FeelingType> completeAction)
        {
            return promptManager.ShowSliderTimerDependant(timerKey, duration, feelingContext, feelingFor, completeAction);
        }

        public float GetRemainingCooldownTimeMinutes(string timerKey)
        {
            return promptManager.GetRemainingCooldownTimeMinutes(timerKey);
        }

        public void SendSessionDataToApi(string sessionDataJson, string reason)
        {
            backendManager.SendSessionDataToAPI(sessionDataJson, reason);
        }

        public void OnGameStart()
        {
            backendManager.OnGameStart();
        }

        public void OnGameEnd(bool win)
        {
            backendManager.OnGameEnd(win);
        }

        public void SendSaaqPromptData(string feeling, int ranking, string feelingModalType, string feelingContext, string feelingFor)
        {
            backendManager.SendSaaqPrompt(feeling, ranking, feelingModalType, feelingContext, feelingFor);
        }
    }
}

