using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace NX10
{
    public class NX10Manager : MonoBehaviour
    {
        private const string ConsentDecisionPrefKey = "NX10_ConsentDecisionRecorded";
        private const string ConsentValuePrefKey = "NX10_ConsentGranted";

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

        private readonly Queue<NX10AnalyticsManager.NX10AnalyticsEvent> unSentEvents = new Queue<NX10AnalyticsManager.NX10AnalyticsEvent>();

        private int _sessionGeneration;
        private bool _isStartingSession;
        private Action<bool> _inFlightStartCallbacks;

        private bool _hasDeferredStartRequest;
        private Dictionary<string, object> _deferredSessionMetaData;
        private Action<bool> _deferredStartCallbacks;

        /// <summary>
        /// True when the user has granted consent for NX10 data collection and processing.
        /// </summary>
        public bool HasConsent { get; private set; }

        /// <summary>
        /// True once SetConsent(true) or SetConsent(false) has been called (persisted across launches when PersistConsent is enabled).
        /// </summary>
        public bool HasConsentDecision { get; private set; }

        public bool Initialised { get; private set; }
        private bool SessionExpired { get; set; }

        public bool IsSessionValid => HasConsent && Initialised && !SessionExpired;

        /// <summary>
        /// True only when consent is granted and an active, non-expired session is running.
        /// </summary>
        public bool CanCollect => IsSessionValid;

        /// <summary>
        /// Current session bearer token when a consented session is active; otherwise null.
        /// </summary>
        public string SessionToken => IsSessionValid ? networkingManager?.CurrentSession?.Token : null;

        public event Action<bool> OnConsentChanged;
        public event Action OnSessionStarted;
        public event Action OnSessionStopped;
        public event Action<SAAQData> OnPromptRequested;
        public event Action<CooldownData> OnCooldownRequested;

        protected void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;

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

            LoadPersistedConsentState();
        }

        private void LoadPersistedConsentState()
        {
            if (NX10RuntimeConfig.PersistConsent && PlayerPrefs.GetInt(ConsentDecisionPrefKey, 0) == 1)
            {
                HasConsentDecision = true;
                HasConsent = PlayerPrefs.GetInt(ConsentValuePrefKey, 0) == 1;
            }
            else
            {
                HasConsentDecision = false;
                HasConsent = false;
            }
        }

        private void SavePersistedConsentState()
        {
            if (!NX10RuntimeConfig.PersistConsent)
                return;

            PlayerPrefs.SetInt(ConsentDecisionPrefKey, HasConsentDecision ? 1 : 0);
            PlayerPrefs.SetInt(ConsentValuePrefKey, HasConsent ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Primary single-call consent flow for partners.
        /// Granting consent starts the NX10 session and (when AutoStartTelemetry is enabled) starts telemetry automatically.
        /// Withdrawing consent immediately stops any active or in-flight session, disables sensors, and discards buffered data.
        /// </summary>
        public void SetConsent(bool hasConsent, Dictionary<string, object> metaData = null, Action<bool> onSessionStarted = null)
        {
            bool previousConsent = HasConsent;
            bool previousDecision = HasConsentDecision;

            HasConsentDecision = true;
            HasConsent = hasConsent;
            SavePersistedConsentState();

            if (!previousDecision || previousConsent != hasConsent)
            {
                OnConsentChanged?.Invoke(hasConsent);
            }

            if (hasConsent)
            {
                Dictionary<string, object> mergedMetaData = MergeMetaData(_deferredSessionMetaData, metaData);
                Action<bool> combinedCallback = _deferredStartCallbacks + onSessionStarted;
                ClearDeferredStartRequest(invokeFalse: false);

                StartSessionInternal(mergedMetaData, combinedCallback);
            }
            else
            {
                ClearDeferredStartRequest(invokeFalse: true);
                TeardownSession(clearComplianceToken: false, clearDiskArtifacts: false);
                onSessionStarted?.Invoke(false);
            }
        }

        /// <summary>
        /// Convenience method for app startup when PersistConsent is enabled.
        /// Starts the session + telemetry if consent was previously granted; otherwise safely invokes onSessionStarted(false) with zero data capture.
        /// </summary>
        public void ResumeSessionIfConsented(Dictionary<string, object> metaData = null, Action<bool> onSessionStarted = null)
        {
            if (!HasConsent)
            {
                onSessionStarted?.Invoke(false);
                return;
            }

            StartSessionInternal(metaData, onSessionStarted);
        }

        /// <summary>
        /// Clears any stored consent decision and stops any active session.
        /// </summary>
        public void ResetConsent(bool clearDiskArtifacts = true)
        {
            bool hadConsent = HasConsent;
            HasConsentDecision = false;
            HasConsent = false;

            if (PlayerPrefs.HasKey(ConsentDecisionPrefKey))
                PlayerPrefs.DeleteKey(ConsentDecisionPrefKey);
            if (PlayerPrefs.HasKey(ConsentValuePrefKey))
                PlayerPrefs.DeleteKey(ConsentValuePrefKey);
            PlayerPrefs.Save();

            ClearDeferredStartRequest(invokeFalse: true);
            TeardownSession(clearComplianceToken: clearDiskArtifacts, clearDiskArtifacts: clearDiskArtifacts);

            if (hadConsent)
            {
                OnConsentChanged?.Invoke(false);
            }
        }

        public string GetCurrentTimestamp()
        {
            return DateTime.UtcNow
                .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        }

        private void SendAttributeRequest(Dictionary<string, object> attributes)
        {
            if (!CanCollect)
                return;

            networkingManager.SendAttributes(attributes);
        }

        private void AnalyticsManager_analyticsFired(NX10AnalyticsManager.NX10AnalyticsEvent analyticsEvent)
        {
            // Never capture or queue analytics when consent has not been granted.
            if (!HasConsent)
                return;

            if (!IsSessionValid)
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
            if (!CanCollect)
            {
                unSentEvents.Clear();
                return;
            }

            while (unSentEvents.Count > 0)
            {
                var e = unSentEvents.Dequeue();
                SendAnalytics(e);
            }
        }

        private void SendAnalytics(NX10AnalyticsManager.NX10AnalyticsEvent analyticsEvent)
        {
            if (!CanCollect)
                return;

            networkingManager.SendAnalytics(analyticsEvent.eventName, analyticsEvent.sourceName, analyticsEvent.timeStamp, analyticsEvent.data);
        }

        /// <summary>
        /// Starts an NX10 session. When RequireExplicitConsent is enabled (default), calling StartSession before SetConsent(true)
        /// logs a warning and defers session creation (with zero data capture) until SetConsent(true) is called.
        /// </summary>
        public void StartSession(Dictionary<string, object> metaData = null, Action<bool> startSuccess = null)
        {
            if (!HasConsent)
            {
                if (HasConsentDecision)
                {
                    Debug.LogWarning("[NX10] StartSession() ignored because user consent has been declined or withdrawn.");
                    startSuccess?.Invoke(false);
                    return;
                }

                if (NX10RuntimeConfig.RequireExplicitConsent)
                {
                    Debug.LogWarning("[NX10] StartSession() called before consent was granted. Deferring session start until SetConsent(true) is called.");
                    _hasDeferredStartRequest = true;
                    _deferredSessionMetaData = MergeMetaData(_deferredSessionMetaData, metaData);
                    _deferredStartCallbacks += startSuccess;
                    return;
                }

                // Legacy non-strict mode explicitly configured by partner in NX10Config
                SetConsent(true, metaData, startSuccess);
                return;
            }

            StartSessionInternal(metaData, startSuccess);
        }

        [Obsolete("Prefer NX10Manager.Instance.SetConsent(true, metaData, startSuccess) or StartSession(metaData, startSuccess).", false)]
        public void StartSession(string input1 = null, string input2 = null, Dictionary<string, object> metaData = null, Action<bool> startSuccess = null)
        {
            StartSession(metaData, startSuccess);
        }

        private void StartSessionInternal(Dictionary<string, object> metaData, Action<bool> startSuccess)
        {
            if (!HasConsent)
            {
                startSuccess?.Invoke(false);
                return;
            }

            if (IsSessionValid)
            {
                if (NX10RuntimeConfig.AutoStartTelemetry)
                {
                    StartTelemetry();
                }

                startSuccess?.Invoke(true);
                return;
            }

            _inFlightStartCallbacks += startSuccess;
            if (_isStartingSession)
            {
                return;
            }

            _isStartingSession = true;
            int requestGeneration = ++_sessionGeneration;

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
                if (requestGeneration != _sessionGeneration || !HasConsent)
                {
                    return;
                }

                _isStartingSession = false;
                Action<bool> callbacks = _inFlightStartCallbacks;
                _inFlightStartCallbacks = null;

                NX10SDKSession session = networkingManager.CurrentSession;
                if (!sessionStartSuccess || session == null)
                {
                    Initialised = false;
                    SessionExpired = false;
                    callbacks?.Invoke(false);
                    return;
                }

                Initialised = true;
                SessionExpired = false;

                inferenceManager.ResetSessionStartTime();
                telemetryManager.SetTelemetryVariables(session.gyroFrequencyHz, session.accelFrequencyHz,
                    session.touchFrequencyHz, session.magnetometerFrequencyHz, session.screenBrightnessDelta,
                    session.acquisitionWindowSize, session.telemetryCompression, session.dpi);

                if (NX10RuntimeConfig.AutoStartTelemetry)
                {
                    StartTelemetry();
                }

                string modelUrl = session.GetAdPropensityUrl("adPropensity-randomClassifier", "1");
                if (!string.IsNullOrWhiteSpace(modelUrl))
                {
                    _ = inferenceManager.InitializeModelAsync(modelUrl);
                }

                attributesManager.FlushPendingAttributes();
                SendUnsentAnalytics();

                OnSessionStarted?.Invoke();
                callbacks?.Invoke(true);
            });
        }

        private static Dictionary<string, object> MergeMetaData(Dictionary<string, object> first, Dictionary<string, object> second)
        {
            if (first == null && second == null)
                return null;

            var merged = new Dictionary<string, object>();
            if (first != null)
            {
                foreach (var kvp in first)
                    merged[kvp.Key] = kvp.Value;
            }
            if (second != null)
            {
                foreach (var kvp in second)
                    merged[kvp.Key] = kvp.Value;
            }
            return merged;
        }

        private void ClearDeferredStartRequest(bool invokeFalse)
        {
            if (!_hasDeferredStartRequest && _deferredStartCallbacks == null && _deferredSessionMetaData == null)
                return;

            var callbacks = _deferredStartCallbacks;
            _hasDeferredStartRequest = false;
            _deferredSessionMetaData = null;
            _deferredStartCallbacks = null;

            if (invokeFalse)
            {
                callbacks?.Invoke(false);
            }
        }

        public void ShouldShowAd(Action<bool, float[]> shouldShowAd)
        {
            if (!CanCollect)
            {
                shouldShowAd?.Invoke(false, Array.Empty<float>());
                return;
            }

            inferenceManager.RunInference((result =>
            {
                if (!CanCollect || result == null || result.Length == 0)
                {
                    shouldShowAd?.Invoke(false, Array.Empty<float>());
                    return;
                }

                foreach (var item in result)
                {
                    Debug.Log(item);
                }

                shouldShowAd?.Invoke(true, result);
            }));
        }

        /// <summary>
        /// Stops the current session, disables sensors, and discards any buffered telemetry/inference data.
        /// Prefer SetConsent(false) when stopping due to user consent withdrawal.
        /// </summary>
        public void StopSession()
        {
            TeardownSession(clearComplianceToken: false, clearDiskArtifacts: false);
        }

        private void TeardownSession(bool clearComplianceToken, bool clearDiskArtifacts)
        {
            bool wasActive = IsSessionValid;

            _sessionGeneration++;
            _isStartingSession = false;

            var inFlightCallbacks = _inFlightStartCallbacks;
            _inFlightStartCallbacks = null;

            Initialised = false;
            SessionExpired = true;

            telemetryManager?.StopTelemetry(flushRemainingWindow: false, clearConfig: true);
            promptManager?.StopPolling();
            inferenceManager?.StopAndReset(clearDiskArtifacts: clearDiskArtifacts);
            attributesManager?.ResetLocalState();
            unSentEvents.Clear();
            networkingManager?.ClearSession(clearComplianceToken: clearComplianceToken);

            inFlightCallbacks?.Invoke(false);

            if (wasActive)
            {
                OnSessionStopped?.Invoke();
            }
        }

        public void PersonalDataRequested(bool dryRun, System.Action<bool, string> completedAction)
        {
            string timeStamp = GetCurrentTimestamp();
            networkingManager.PersonalDataRequested(dryRun, timeStamp, completedAction);
        }

        public void PersonalDataDeletionRequested(bool dryRun, System.Action<bool> completedAction)
        {
            string timeStamp = GetCurrentTimestamp();
            networkingManager.PersonalDataDeletionRequested(dryRun, timeStamp, (success) =>
            {
                if (success && !dryRun)
                {
                    SetConsent(false);
                    inferenceManager?.StopAndReset(clearDiskArtifacts: true);
                    networkingManager?.ClearSession(clearComplianceToken: true);
                }

                completedAction?.Invoke(success);
            });
        }

        public void SendEvent(string eventName, Dictionary<string, object> eventData = null)
        {
            if (!CanCollect || string.IsNullOrEmpty(eventName))
                return;

            string timeStamp = GetCurrentTimestamp();
            networkingManager.SendEvent(eventName, timeStamp, null, eventData);
            telemetryManager.RecordEvent(eventName, eventData);
        }

        public enum Outcome
        {
            Converted,
            UnConverted
        }

        public void SendOutcomeEvent(string eventName, Outcome outcome, Dictionary<string, object> eventData = null)
        {
            if (!CanCollect || string.IsNullOrEmpty(eventName))
                return;

            string timeStamp = GetCurrentTimestamp();
            networkingManager.SendEvent(eventName, timeStamp, outcome.ToString(), eventData);
            telemetryManager.RecordEvent(eventName, eventData);
        }

        public void SetAttributes(Dictionary<string, object> attributes)
        {
            if (!HasConsent || attributes == null)
                return;

            attributesManager.SetAttributes(attributes);

            if (CanCollect)
            {
                telemetryManager.RecordAttributes(attributes.Keys);
            }
        }

        public void SetAttribute(string attributeKey, object attributeValue)
        {
            if (!HasConsent || string.IsNullOrEmpty(attributeKey))
                return;

            attributesManager.SetAttribute(attributeKey, attributeValue, true);

            if (CanCollect)
            {
                telemetryManager.RecordAttribute(attributeKey);
            }
        }

        public void RemoveAttribute(string attributeKey)
        {
            if (!HasConsent || string.IsNullOrEmpty(attributeKey))
                return;

            attributesManager.RemoveAttribute(attributeKey);
        }

        public void ClearAttributes()
        {
            if (!HasConsent)
                return;

            attributesManager.ClearAttributes();
        }

        public void StartTelemetry()
        {
            if (!CanCollect)
                return;

            telemetryManager.SetTelemetryCollection(true);
        }

        public void StopTelemetry()
        {
            telemetryManager.SetTelemetryCollection(false);
        }

        public void ShowPrompt(SAAQData promptData, Action<SAAQAnswer, string, string> promptAnsweredAction)
        {
            if (!CanCollect || promptData == null)
                return;

            ShowPrompt(promptData.prompt, promptData.dismissable, promptAnsweredAction);
        }

        public void ShowPrompt(SAAQBlock promptData, bool dismissable, Action<SAAQAnswer, string, string> promptAnsweredAction)
        {
            if (!CanCollect || promptData == null)
                return;

            string promptDisplayTimestamp = GetCurrentTimestamp();

            promptManager.ShowPrompt(promptData, dismissable, (answer) =>
            {
                string promptAnswerTimestamp = GetCurrentTimestamp();
                promptAnsweredAction?.Invoke(answer, promptDisplayTimestamp, promptAnswerTimestamp);
            });
        }

        public void RequestActivity(Action<KineticState> activityAction)
        {
            if (!CanCollect)
            {
                activityAction?.Invoke(KineticState.Unknown);
                return;
            }

            networkingManager.RequestActivity((state) =>
            {
                activityAction?.Invoke(state);
            });
        }

        public void RequestAffect(Action<Affect, float> affectAndConfidenceAction)
        {
            if (!CanCollect)
            {
                affectAndConfidenceAction?.Invoke(Affect.Unknown, 0f);
                return;
            }

            networkingManager.RequestAffect((affect, confidence) =>
            {
                affectAndConfidenceAction?.Invoke(affect, confidence);
            });
        }

        public void SendPromptAnswer(SAAQAnswer answer, string displayTimestamp, string answerTimestamp, string triggerId)
        {
            if (!CanCollect || answer == null)
                return;

            networkingManager.SendTriggeredSAAQData(answer, displayTimestamp, answerTimestamp, triggerId);
        }

        private void SendTelemetryData(string windowStartTimestamp, double windowEndOffset, List<IInputEvent> inputEvents)
        {
            if (!CanCollect)
                return;

            networkingManager.SendTelemetryData(windowStartTimestamp, windowEndOffset, inputEvents);
        }

        private void PromptRequested(SAAQData promptData)
        {
            if (!CanCollect)
                return;

            OnPromptRequested?.Invoke(promptData);
        }

        private void CooldownRequested(CooldownData cooldownData)
        {
            if (!CanCollect)
                return;

            OnCooldownRequested?.Invoke(cooldownData);
        }
    }
}
