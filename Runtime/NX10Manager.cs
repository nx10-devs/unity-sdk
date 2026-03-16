using System;
using System.Collections.Generic;
using UnityEngine;

namespace NX10
{
    public class NX10Manager : MonoBehaviour
    {
        private static NX10Manager _instance;

        public static NX10Manager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = UnityEngine.Object.FindAnyObjectByType<NX10Manager>();

                    if (_instance == null)
                    {
                        GameObject sdkObject = Resources.Load<GameObject>("NX10_Manager");
                        GameObject instanceObject = UnityEngine.Object.Instantiate(sdkObject);

                        instanceObject.hideFlags = HideFlags.HideInHierarchy;
                        DontDestroyOnLoad(instanceObject);

                        _instance = instanceObject.GetComponent<NX10Manager>();
                    }
                }
                return _instance;
            }
        }

        private NX10PromptManager promptManager;
        private NX10BackendManager backendManager;
        private NX10TelemetryManager telemetryManager;

        public bool Initialised { get; private set; }

        protected void Awake()
        {
            promptManager = GetComponentInChildren<NX10PromptManager>();
            backendManager = GetComponentInChildren<NX10BackendManager>();
            telemetryManager = GetComponentInChildren<NX10TelemetryManager>();

            telemetryManager.sendTelemetryDataRequest += SendTelemetryData;
            promptManager.sendSaaqDataRequest += SendSaaqData;
        }

        public void StartSession(SessionConfig sessionConfig, System.Action<bool> startSuccess)
        {
            backendManager.StartSession(sessionConfig, (sessionStartSuccess) =>
            {
                Initialised = sessionStartSuccess;
                startSuccess?.Invoke(sessionStartSuccess);
            });
        }

        public void SetAttributes(Dictionary<string, object> attributes)
        {
            backendManager.SetAttributes(attributes);
        }

        public void SetAttribute(string attributeKey, object attributeValue)
        {
            backendManager.SetAttribute(attributeKey, attributeValue, true);
        }

        public void RemoveAttribute(string attributeKey)
        {
            backendManager.RemoveAttribute(attributeKey);
        }

        public void ClearAttributes()
        {
            backendManager.ClearAttributes();
        }

        public void StartTelemetry()
        {
            telemetryManager.SetTelemetryCollection(true);
        }

        public void StopTelemetry()
        {
            telemetryManager.SetTelemetryCollection(false);
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

