using System;
using System.Collections.Generic;
using System.Globalization;
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
        private NX10InferenceManager inferenceManager;
        //private NX10DebugManager debugManager;

        public bool Initialised { get; private set; }
        private bool SessionExpired { get; set; }

        public bool IsSessionValid
        {
            get
            {
                return Initialised && !SessionExpired;
            }
        }

        public event Action<SAAQData> OnPromptRequested;
        public event Action<CooldownData> OnCooldownRequested;

        protected void Awake()
        {
            promptManager = GetComponentInChildren<NX10PromptManager>();
            networkingManager = GetComponentInChildren<NX10NetworkingManager>();
            telemetryManager = GetComponentInChildren<NX10TelemetryManager>();
            analyticsManager = GetComponentInChildren<NX10AnalyticsManager>();
            attributesManager = GetComponentInChildren<NX10AttributesManager>();
            inferenceManager = GetComponentInChildren<NX10InferenceManager>();
            //debugManager = GetComponentInChildren<NX10DebugManager>();

            telemetryManager.sendTelemetryDataRequest += SendTelemetryData;
            networkingManager.OnPromptRequested += PromptRequested;
            networkingManager.OnCooldownRequested += CooldownRequested;
            analyticsManager.analyticsFired += AnalyticsManager_analyticsFired;
            attributesManager.sendAttributesRequest += SendAttributeRequest;

            inferenceManager.Initialise(telemetryManager);
        }
        public string GetCurrentTimestamp()
        {
            return DateTime.UtcNow
                .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        }

        private void SendAttributeRequest(Dictionary<string, object> attributes)
        {
            if (!IsSessionValid)
                return;

            networkingManager.SendAttributes(attributes);
        }

        private void AnalyticsManager_analyticsFired(NX10AnalyticsManager.NX10AnalyticsEvent analyticsEvent)
        {
            if (!IsSessionValid)
                return;

            SendAnalytics(analyticsEvent);
        }

        private void SendAnalytics(NX10AnalyticsManager.NX10AnalyticsEvent analyticsEvent)
        {
            if (!IsSessionValid)
                return;

            networkingManager.SendAnalytics(analyticsEvent.eventName, analyticsEvent.sourceName, analyticsEvent.timeStamp, analyticsEvent.data);
        }

        public void StartSession(Dictionary<string, object> metaData = null, Action<bool> startSuccess = null)
        {
            if (metaData == null)
                metaData = new Dictionary<string, object>();

            UserIdentifiers identifiers = new UserIdentifiers
            {
                deviceId = SystemInfo.deviceUniqueIdentifier,
                email = string.Empty,
                phoneNumber = string.Empty,
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
                SessionExpired = !sessionStartSuccess;

                if (sessionStartSuccess)
                {
                    NX10SDKSession session = networkingManager.CurrentSession;
                    telemetryManager.SetTelemetryVariables(session.gyroFrequencyHz, session.accelFrequencyHz,
                        session.touchFrequencyHz, session.magnetometerFrequencyHz, session.screenBrightnessDelta,
                        session.acquisitionWindowSize, session.telemetryCompression, session.dpi);

                    _ = inferenceManager.InitializeModelAsync(session.GetAdPropensityUrl("adPropensity-randomClassifier", "1"));
                }

                startSuccess?.Invoke(sessionStartSuccess);
            });
        }

        [Obsolete("Start Session now should only take metadata and a success action", false)]
        public void StartSession(string input1 = null, string input2 = null, Dictionary<string, object> metaData = null, Action<bool> startSuccess = null)
        {
            StartSession(metaData, startSuccess);
        }

        public void ShouldShowAd(Action<bool, float[]> shouldShowAd)
        {
            if (!IsSessionValid)
            {
                Debug.LogWarning("[NX10] Cannot run inference: Session is not valid.");
                shouldShowAd?.Invoke(false, Array.Empty<float>());
                return;
            }

            inferenceManager.RunInference((result =>
            {
                if (result != null)
                {
                    foreach (var item in result)
                    {
                        Debug.Log(item);
                    }
                }

                shouldShowAd?.Invoke(result != null, result); 
            }));
        }

        public void StopSession()
        {
            Initialised = false;
            SessionExpired = true;

            StopTelemetry();
            telemetryManager.ResetTelemetryVariables();
            //inferenceManager.StopSession();
            attributesManager.ClearSessionAttributes();
            _ = promptManager.StopPollingAsync();
            networkingManager.StopSession();
        }

        public void PersonalDataRequested(bool dryRun, System.Action<bool, string> completedAction)
        {
            string timeStamp = GetCurrentTimestamp();
            networkingManager.PersonalDataRequested(dryRun, timeStamp, completedAction);
        }

        public void PersonalDataDeletionRequested(bool dryRun, System.Action<bool> completedAction)
        {
            string timeStamp = GetCurrentTimestamp();
            networkingManager.PersonalDataDeletionRequested(dryRun, timeStamp, completedAction);
        }

        public void SendEvent(string eventName, Dictionary<string, object> eventData = null)
        {
            string timeStamp = GetCurrentTimestamp();
            networkingManager.SendEvent(eventName, timeStamp, null, eventData);

            if (IsSessionValid)
            {
                telemetryManager.RecordEvent(eventName, eventData);
            }
        }

        public enum Outcome
        {
            Converted,
            UnConverted
        }

        public void SendOutcomeEvent(string eventName, Outcome outcome, Dictionary<string, object> eventData = null)
        {
            string timeStamp = GetCurrentTimestamp();
            networkingManager.SendEvent(eventName, timeStamp, outcome.ToString(), eventData);

            if (IsSessionValid)
            {
                telemetryManager.RecordEvent(eventName, eventData);
            }
        }

        public void SetAttributes(Dictionary<string, object> attributes)
        {
            attributesManager.SetAttributes(attributes);

            if (IsSessionValid && attributes != null)
            {
                telemetryManager.RecordAttributes(attributes.Keys);
            }
        }

        public void SetAttribute(string attributeKey, object attributeValue)
        {
            attributesManager.SetAttribute(attributeKey, attributeValue, true);

            if (IsSessionValid)
            {
                telemetryManager.RecordAttribute(attributeKey);
            }
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
        }

        public void StopTelemetry()
        {
            telemetryManager.SetTelemetryCollection(false);
        }

        public void ShowPrompt(SAAQData promptData, Action<SAAQAnswer, string, string> promptAnsweredAction)
        {
            if (!IsSessionValid)
            {
                Debug.LogWarning("NX10 Session is not valid; prompt will not be shown.");
                return;
            }

            ShowPrompt(promptData.prompt, promptData.dismissable, promptAnsweredAction);
        }

        public void ShowPrompt(SAAQBlock promptData, bool dismissable, Action<SAAQAnswer, string, string> promptAnsweredAction)
        {
            if (!IsSessionValid)
            {
                Debug.LogWarning("NX10 Session is not valid; prompt will not be shown.");
                return;
            }

            string promptDisplayTimestamp = GetCurrentTimestamp();

            promptManager.ShowPrompt(promptData, dismissable, (answer) =>
            {
                string promptAnswerTimestamp = GetCurrentTimestamp();
                promptAnsweredAction.Invoke(answer, promptDisplayTimestamp, promptAnswerTimestamp);
            });
        }

        public void RequestActivity(Action<KineticState> activityAction)
        {
            if (!IsSessionValid)
            {
                Debug.LogWarning("NX10 Session is not valid; activity cannot be requested.");
                activityAction?.Invoke(KineticState.Unknown);
                return;
            }

            networkingManager.RequestActivity((state) =>
            {
                activityAction(state);
            });
        }

        public void RequestAffect(Action<Affect, float> affectAndConfidenceAction)
        {
            if (!IsSessionValid)
            {
                Debug.LogWarning("NX10 Session is not valid; affect cannot be requested.");
                affectAndConfidenceAction?.Invoke(Affect.Unknown, 0f);
                return;
            }

            networkingManager.RequestAffect((affect, confidence) =>
            {
                affectAndConfidenceAction(affect, confidence);
            });
        }

        public void SendPromptAnswer(SAAQAnswer answer, string displayTimestamp, string answerTimestamp, string triggerId)
        {
            if (!IsSessionValid)
            {
                Debug.LogWarning("NX10 Session is not valid; prompt answer will not be sent.");
                return;
            }

            networkingManager.SendTriggeredSAAQData(answer, displayTimestamp, answerTimestamp, triggerId);
        }

        private void SendTelemetryData(string windowStartTimestamp, double windowEndOffset, List<IInputEvent> inputEvents)
        {
            if (!IsSessionValid)
                return;

            networkingManager.SendTelemetryData(windowStartTimestamp, windowEndOffset, inputEvents);
        }

        private void PromptRequested(SAAQData promptData)
        {
            OnPromptRequested?.Invoke(promptData);
        }

        private void CooldownRequested(CooldownData cooldownData)
        {
            OnCooldownRequested?.Invoke(cooldownData);
        }
    }
}

