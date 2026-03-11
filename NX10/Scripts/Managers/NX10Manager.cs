using System;
using System.Collections.Generic;
using UnityEngine;

namespace NX10
{
    public class NX10Manager : NX10PersistentSingleton<NX10Manager>
    {
        private NX10PromptManager promptManager;
        private NX10BackendManager backendManager;
        private NX10TelemetryManager telemetryManager;

        public bool Initialised { get; private set; }

        protected override void Awake()
        {
            base.Awake();

            promptManager = GetComponentInChildren<NX10PromptManager>();
            backendManager = GetComponentInChildren<NX10BackendManager>();
            telemetryManager = GetComponentInChildren<NX10TelemetryManager>();

            telemetryManager.sendTelemetryDataRequest += SendTelemetryData;
            promptManager.sendSaaqDataRequest += SendSaaqData;
        }

        public void StartSession()
        {
            backendManager.StartSession((sessionStartSuccess) =>
            {
                Initialised = true;
            });
        }

        /// <summary>
        /// should be used carefully, will call the prompts completionAction
        /// </summary>
        public void ForceClosePrompt()
        {
            promptManager.ForceClosePrompt();
        }

        public void UpdateAttributes(Dictionary<string, object> attributes)
        {
            backendManager.UpdateAttributes(attributes);
        }

        public void SetTelemetryCollection(bool canCollect)
        {
            telemetryManager.SetTelemetryCollection(canCollect);
        }

        public void ShowPrompt(PromptType promptType, FeelingType[] typesToShow, string feelingContext, string feelingFor, Action<FeelingType> completeAction)
        {
            switch(promptType)
            {
                case PromptType.Slider:
                    ShowSlider(typesToShow, feelingContext, feelingFor, completeAction);
                    break;
                    case PromptType.Button:
                    ShowButton(typesToShow, feelingContext, feelingFor, completeAction);
                    break;
            }
        }

        private void ShowSlider(FeelingType[] typesToShow, string feelingContext, string feelingFor, Action<FeelingType> completeAction)
        {
            promptManager.ShowSlider(typesToShow, feelingContext, feelingFor, completeAction);
        }

        private void ShowButton(FeelingType[] typeToShow, string feelingContext, string feelingFor, Action<FeelingType> completeAction)
        {
            promptManager.ShowButton(typeToShow, feelingContext, feelingFor, completeAction);
        }

        private void SendSaaqData(string feeling, int ranking, string feelingModalType, string feelingContext, string feelingFor, string promptDisplayTimestamp, string promptAnswerTimestamp)
        {
            backendManager.SendSaaqData(feeling, ranking, feelingModalType, feelingContext, feelingFor, promptDisplayTimestamp, promptAnswerTimestamp);
        }

        private void SendTelemetryData(string windowStartTimestamp, double windowEndOffset, List<IInputEvent> inputEvents)
        {
            backendManager.SendTelemetryData(windowStartTimestamp, windowEndOffset, inputEvents);
        }

    }
}

