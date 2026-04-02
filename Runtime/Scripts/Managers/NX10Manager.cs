using CodiceApp.EventTracking.Plastic;
using System;
using System.Collections;
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
        private NX10NetworkingManager backendManager;
        private NX10TelemetryManager telemetryManager;
        private NX10AnalyticsManager analyticsManager;
        private NX10AttributesManager attributesManager;

        private Queue<NX10AnalyticsManager.NX10AnalyticsEvent> unSentEvents = new Queue<NX10AnalyticsManager.NX10AnalyticsEvent>();

        public bool Initialised { get; private set; }

        public event Action<SAAQPrompt> OnPromptRequested;

        protected void Awake()
        {
            promptManager = GetComponentInChildren<NX10PromptManager>();
            backendManager = GetComponentInChildren<NX10NetworkingManager>();
            telemetryManager = GetComponentInChildren<NX10TelemetryManager>();
            analyticsManager = GetComponentInChildren<NX10AnalyticsManager>();
            attributesManager = GetComponentInChildren<NX10AttributesManager>();

            telemetryManager.sendTelemetryDataRequest += SendTelemetryData;
            promptManager.sendSaaqDataRequest += SendSaaqData;
            backendManager.OnPromptRequested += PromptRequested;
            analyticsManager.analyticsFired += AnalyticsManager_analyticsFired;
            attributesManager.sendAttributesRequest += SendAttributeRequest;
        }

        private void SendAttributeRequest(Dictionary<string, object> attributes)
        {
            backendManager.SendAttributes(attributes);
        }

        private void AnalyticsManager_analyticsFired(NX10AnalyticsManager.NX10AnalyticsEvent analyticsEvent)
        {
            if(!Initialised)
            {
                unSentEvents.Enqueue(analyticsEvent);
            }
            else
            {
                SendAnalytics(analyticsEvent);
            }
        }

        private void SendUnsentAnalytics()
        {
            foreach(var e in unSentEvents)
            {
                SendAnalytics(e);
            }
        }

        private void SendAnalytics(NX10AnalyticsManager.NX10AnalyticsEvent analyticsEvent)
        {
            backendManager.SendAnalytics(analyticsEvent.eventName, analyticsEvent.sourceName, analyticsEvent.timeStamp, analyticsEvent.data);
        }

        public void StartSession(string email = null, string phoneNumber = null, Dictionary<string, object> metaData = null, Action<bool> startSuccess = null)
        {
            UserIdentifiers identifiers = new UserIdentifiers
            {
                deviceId = SystemInfo.deviceUniqueIdentifier,
                email = email,
                phoneNumber = phoneNumber,
            };

            AppProvidedData appProvided = new AppProvidedData
            {
                metaData = metaData,
                applicationVersion = Application.version,
                buildNumber = Application.buildGUID
            };

            SessionConfig sessionConfig = new SessionConfig
            {
                Identifiers = identifiers,
                AppProvidedData = appProvided
            };

            backendManager.StartSession(sessionConfig, (sessionStartSuccess) =>
            {
                Initialised = sessionStartSuccess;
                startSuccess?.Invoke(sessionStartSuccess);

                analyticsManager.FireEvent("session_started");
                SendUnsentAnalytics();
            });
        }

        public void SetAttributes(Dictionary<string, object> attributes)
        {
            attributesManager.SetAttributes(attributes);
        }

        public void SetAttribute(string attributeKey, object attributeValue)
        {
            attributesManager.SetAttribute(attributeKey, attributeValue, true);
        }

        public void RemoveAttribute(string attributeKey)
        {
            attributesManager.RemoveAttribute(attributeKey);
        }

        public void ClearAttributes()
        {
            attributesManager.ClearAttributes();
        }

        public void StartTelemetry()
        {
            telemetryManager.SetTelemetryCollection(true);
            analyticsManager.FireEvent("telemetry_started");
        }

        public void StopTelemetry()
        {
            telemetryManager.SetTelemetryCollection(false);
            analyticsManager.FireEvent("telemetry_ended");
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

