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
        private NX10NetworkingManager networkingManager;
        private NX10TelemetryManager telemetryManager;
        private NX10AnalyticsManager analyticsManager;
        private NX10AttributesManager attributesManager;
        private NX10DebugManager debugManager;

        private Queue<NX10AnalyticsManager.NX10AnalyticsEvent> unSentEvents = new Queue<NX10AnalyticsManager.NX10AnalyticsEvent>();

        public bool Initialised { get; private set; }

        public event Action<SAAQData> OnPromptRequested;

        protected void Awake()
        {
            promptManager = GetComponentInChildren<NX10PromptManager>();
            networkingManager = GetComponentInChildren<NX10NetworkingManager>();
            telemetryManager = GetComponentInChildren<NX10TelemetryManager>();
            analyticsManager = GetComponentInChildren<NX10AnalyticsManager>();
            attributesManager = GetComponentInChildren<NX10AttributesManager>();
            debugManager = GetComponentInChildren<NX10DebugManager>();

            telemetryManager.sendTelemetryDataRequest += SendTelemetryData;
            networkingManager.OnPromptRequested += PromptRequested;
            analyticsManager.analyticsFired += AnalyticsManager_analyticsFired;
            attributesManager.sendAttributesRequest += SendAttributeRequest;
        }

        private void SendAttributeRequest(Dictionary<string, object> attributes)
        {
            networkingManager.SendAttributes(attributes);
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
            networkingManager.SendAnalytics(analyticsEvent.eventName, analyticsEvent.sourceName, analyticsEvent.timeStamp, analyticsEvent.data);
        }

        public void StartSession(string email = null, string phoneNumber = null, Dictionary<string, object> metaData = null, Action<bool> startSuccess = null)
        {
            if (email == null)
                email = string.Empty;
            if (phoneNumber == null)
                phoneNumber = string.Empty;
            if (metaData == null)
                metaData = new Dictionary<string, object>();

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

            networkingManager.StartSession(sessionConfig, (sessionStartSuccess) =>
            {
                Initialised = sessionStartSuccess;

                NX10SDKSession session = networkingManager.CurrentSession;
                telemetryManager.SetTelemetryVariables(session.gyroFrequencyHz, session.accelFrequencyHz, session.touchFrequencyHz, session.acquisitionWindowSize, session.dpi);

                analyticsManager.FireEvent("session_started");
                SendUnsentAnalytics();

                debugManager.Initialise(telemetryManager);

                if(session.saaqPollingPeriod.HasValue)
                {
                    promptManager.Initalise(session.saaqPollingPeriod.Value, networkingManager);
                }

                startSuccess?.Invoke(sessionStartSuccess);
            });
        }

        public void SendEvent(string eventName, Dictionary<string, object> eventData = null)
        {
            string timeStamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            networkingManager.SendEvent(eventName, timeStamp, null, eventData);
        }


        public enum Outcome
        {
            Converted,
            UnConverted
        }

        public void SendOutcomeEvent(string eventName, Outcome outcome, Dictionary<string, object> eventData = null)
        {
            string timeStamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            networkingManager.SendEvent(eventName, timeStamp, outcome.ToString(), eventData);
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

        public void ShowPrompt(SAAQData promptData, Action<SAAQAnswer, string, string> promptAnsweredAction)
        {
            ShowPrompt(promptData.prompt, promptData.dismissable, promptAnsweredAction);
        }

        public void ShowPrompt(SAAQBlock promptData, bool dismissable, Action<SAAQAnswer, string, string> promptAnsweredAction)
        {
            string promptDisplayTimestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            promptManager.ShowPrompt(promptData, dismissable, (answer) =>
            {
                string promptAnswerTimestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                promptAnsweredAction.Invoke(answer, promptDisplayTimestamp, promptAnswerTimestamp);
            });

            analyticsManager.FireEvent("saaq_shown");
        }

        public void RequestActivity(Action<KineticState> activityAction)
        {
            networkingManager.RequestActivity((state) =>
            {
                activityAction(state);
            });
        }

        public void RequestAffect(Action<Affect, float> affectAndConfidenceAction)
        {
            networkingManager.RequestAffect((affect, confidence) =>
            {
                affectAndConfidenceAction(affect, confidence);
            });
        }

        public void SendPromptAnswer(SAAQAnswer answer, string displayTimestamp, string answerTimestamp, string triggerId)
        {
            networkingManager.SendTriggeredSAAQData(answer, displayTimestamp, answerTimestamp, triggerId);
        }

        private void SendTelemetryData(string windowStartTimestamp, double windowEndOffset, List<IInputEvent> inputEvents)
        {
            networkingManager.SendTelemetryData(windowStartTimestamp, windowEndOffset, inputEvents);
        }

        private void PromptRequested(SAAQData promptData)
        {
            OnPromptRequested?.Invoke(promptData);
        }
    }
}

