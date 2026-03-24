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
        private NX10AnalyticsManager analyticsManager;

        public bool Initialised { get; private set; }

        public event Action<SAAQPrompt> OnPromptRequested;

        protected void Awake()
        {
            promptManager = GetComponentInChildren<NX10PromptManager>();
            backendManager = GetComponentInChildren<NX10BackendManager>();
            telemetryManager = GetComponentInChildren<NX10TelemetryManager>();
            analyticsManager = GetComponentInChildren<NX10AnalyticsManager>();

            telemetryManager.sendTelemetryDataRequest += SendTelemetryData;
            promptManager.sendSaaqDataRequest += SendSaaqData;
            backendManager.OnPromptRequested += PromptRequested;
            analyticsManager.analyticsFired += AnalyticsManager_analyticsFired;
        }

        private void AnalyticsManager_analyticsFired(string eventName, string sourceName)
        {
            backendManager.SendAnalytics(eventName, sourceName);
        }

        public void StartSession(SessionConfig sessionConfig, System.Action<bool> startSuccess)
        {
            backendManager.StartSession(sessionConfig, (sessionStartSuccess) =>
            {
                Initialised = sessionStartSuccess;
                startSuccess?.Invoke(sessionStartSuccess);

                analyticsManager.FireEvent("session_started");
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
            analyticsManager.FireEvent("client_started_telemetry");
        }

        public void StopTelemetry()
        {
            telemetryManager.SetTelemetryCollection(false);
            analyticsManager.FireEvent("client_stopped_telemetry");
        }

        public void ShowPrompt(SAAQPrompt prompt, Action<SAAQAnswer> promptAnsweredAction)
        {
            promptManager.ShowPrompt(prompt, promptAnsweredAction);
            analyticsManager.FireEvent("saaq_shown");
        }

        public void SendPromptAnswer(SAAQAnswer answer, string displayTimestamp, string answerTimestamp, string triggerId)
        {
            backendManager.SendTriggeredSAAQData(answer, displayTimestamp, answerTimestamp, triggerId);
        }

        private void SendSaaqData(SAAQAnswer answer, string promptType, string feelingContext, string feelingFor, string promptDisplayTimestamp, string promptAnswerTimestamp)
        {
            backendManager.SendSaaqData(answer.feelingsType, 0, promptType, feelingContext, feelingFor, promptDisplayTimestamp, promptAnswerTimestamp);
        }

        private void SendTelemetryData(string windowStartTimestamp, double windowEndOffset, List<IInputEvent> inputEvents)
        {
            backendManager.SendTelemetryData(windowStartTimestamp, windowEndOffset, inputEvents);
        }

        private void PromptRequested(SAAQPrompt prompt)
        {
            OnPromptRequested?.Invoke(prompt);
        }
    }
}

