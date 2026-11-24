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

        private NX10NetworkingManager networkingManager;
        private NX10TelemetryManager telemetryManager;
        private NX10AttributesManager attributesManager;
        private NX10InferenceManager inferenceManager;

        private NX10EventsApi _eventsApi;
        private NX10AttributesApi _attributesApi;
        private bool _isAppBackgrounded;
        private bool _hasSentAppQuit;

        private NX10EventsApi EventsInternal => _eventsApi ?? (_eventsApi = new NX10EventsApi(this));
        private NX10AttributesApi AttributesInternal => _attributesApi ?? (_attributesApi = new NX10AttributesApi(this));

        /// <summary>
        /// Controls whether app_quit, app_backgrounded, and app_foregrounded are fired automatically.
        /// Initialized from <see cref="NX10RuntimeConfig.AutoTrackAppLifecycle"/>.
        /// </summary>
        public bool AutoTrackAppLifecycle { get; set; } = true;

        public bool Initialised { get; private set; }
        private bool SessionExpired { get; set; }

        public bool IsSessionValid
        {
            get
            {
                return Initialised && !SessionExpired;
            }
        }

        protected void Awake()
        {
            networkingManager = GetComponentInChildren<NX10NetworkingManager>();
            telemetryManager = GetComponentInChildren<NX10TelemetryManager>();
            attributesManager = GetComponentInChildren<NX10AttributesManager>();
            inferenceManager = GetComponentInChildren<NX10InferenceManager>();

            telemetryManager.sendTelemetryDataRequest += SendTelemetryData;
            attributesManager.sendAttributesRequest += SendAttributeRequest;

            AutoTrackAppLifecycle = NX10RuntimeConfig.AutoTrackAppLifecycle;

            inferenceManager.Initialise(telemetryManager, networkingManager);
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                if (!_isAppBackgrounded)
                {
                    _isAppBackgrounded = true;
                    if (AutoTrackAppLifecycle && IsSessionValid)
                    {
                        EventsInternal.AppBackgrounded();
                    }
                }
            }
            else
            {
                if (_isAppBackgrounded)
                {
                    _isAppBackgrounded = false;
                    if (AutoTrackAppLifecycle && IsSessionValid)
                    {
                        EventsInternal.AppForegrounded();
                    }
                }
            }
        }

        private void OnApplicationQuit()
        {
            if (_hasSentAppQuit)
                return;

            _hasSentAppQuit = true;

            if (AutoTrackAppLifecycle && IsSessionValid)
            {
                EventsInternal.AppQuit();
            }
        }

        public string GetCurrentTimestamp()
        {
            return DateTime.UtcNow
                .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        }

        private void SendAttributeRequest(Dictionary<string, object> attributes)
        {
            networkingManager.SendAttributes(attributes);
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
                SessionExpired = false;

                NX10SDKSession session = networkingManager.CurrentSession;
                telemetryManager.OnSessionStarted();
                inferenceManager.OnSessionStarted();
                telemetryManager.SetTelemetryVariables(session.gyroFrequencyHz, session.accelFrequencyHz,
                    session.touchFrequencyHz, session.magnetometerFrequencyHz, session.screenBrightnessDelta,
                    session.acquisitionWindowSize, session.telemetryCompression, session.dpi);

                _= inferenceManager.InitializeModelAsync(session.GetAdPropensityUrl("adPropensity-randomClassifier", "1"));

                startSuccess?.Invoke(sessionStartSuccess);
            });
        }

        [Obsolete("Start Session now should only take metadata and a success action", false)]
        public void StartSession(string input1 = null, string input2 = null, Dictionary<string, object> metaData = null, Action<bool> startSuccess = null)
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
                SessionExpired = false;

                NX10SDKSession session = networkingManager.CurrentSession;
                telemetryManager.OnSessionStarted();
                inferenceManager.OnSessionStarted();
                telemetryManager.SetTelemetryVariables(session.gyroFrequencyHz, session.accelFrequencyHz,
                    session.touchFrequencyHz, session.magnetometerFrequencyHz, session.screenBrightnessDelta,
                    session.acquisitionWindowSize, session.telemetryCompression, session.dpi);

                startSuccess?.Invoke(sessionStartSuccess);
            });
        }

        public void ShouldShowAd(Action<bool, float[]> shouldShowAd)
        {
            inferenceManager.RunInference((result =>
            {
                bool shouldShow = (result != null && result.Length > 0 && result[0] >= networkingManager.CurrentSession.GetAdPropensityThreshold("adPropensity-randomClassifier", "1"));
                shouldShowAd?.Invoke(shouldShow, result); 
            }));
        }

        public void StopSession()
        {
            SessionExpired = true;
            StopTelemetry();
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

        #region Strongly-Typed Attributes

        /// <summary>
        /// Updates multiple standard attributes at once and dispatches a single network payload.
        /// </summary>
        public void SetAttributes(Action<NX10AttributesBatchBuilder> configure) => AttributesInternal.Batch(configure);

        public void SetView(NX10View view) => AttributesInternal.SetView(view);

        public void SetBannerAdVisible(bool visible) => AttributesInternal.SetBannerAdVisible(visible);

        public void SetInterstitialAdRateSeconds(int seconds) => AttributesInternal.SetInterstitialAdRateSeconds(seconds);

        public void SetNoAds(bool noAds) => AttributesInternal.SetNoAds(noAds);

        public void SetInTutorial(bool inTutorial) => AttributesInternal.SetInTutorial(inTutorial);

        public void SetTotalActiveSaveSlots(int totalActiveSaveSlots) => AttributesInternal.SetTotalActiveSaveSlots(totalActiveSaveSlots);

        public void SetCurrentSaveSlot(string currentSaveSlot) => AttributesInternal.SetCurrentSaveSlot(currentSaveSlot);

        public void SetSaveSlots(string currentSaveSlot, int totalActiveSaveSlots) => AttributesInternal.SetSaveSlots(currentSaveSlot, totalActiveSaveSlots);

        #endregion

        #region Strongly-Typed Events

        /// <summary>
        /// Sends "ad_started" for a banner ad (omitting ads_in_set and ad_number) and returns a handle to call <see cref="NX10AdHandle.Finish"/>.
        /// </summary>
        public NX10AdHandle StartBannerAd(string customId = null) => EventsInternal.StartBannerAd(customId);

        /// <summary>
        /// Sends "ad_started" for a non-banner ad (interstitial, rewarded, app_start, or custom) and returns a handle to call <see cref="NX10AdHandle.Finish"/>.
        /// </summary>
        public NX10AdHandle StartAd(NX10AdSetType type, int adsInSet = 1, int adNumber = 1, string groupId = null) =>
            EventsInternal.StartAd(type, adsInSet, adNumber, groupId);

        /// <summary>
        /// Sends "rewarded_offer_shown" and returns a handle to call <see cref="NX10OfferHandle.Hide"/> when dismissed, expired, or navigated away.
        /// </summary>
        public NX10OfferHandle ShowRewardedOffer(NX10OfferType type, int amount, NX10OfferTrigger trigger, string customId = null) =>
            EventsInternal.ShowRewardedOffer(type, amount, trigger, customId);

        public void SendBannerAdStarted(string id) => EventsInternal.SendBannerAdStarted(id);

        public void SendBannerAdFinished(string id) => EventsInternal.SendBannerAdFinished(id);

        public void SendAdStarted(string id, NX10AdSetType type, int adsInSet, int adNumber) =>
            EventsInternal.SendAdStarted(id, type, adsInSet, adNumber);

        public void SendAdFinished(string id, NX10AdSetType type, int adsInSet, int adNumber) =>
            EventsInternal.SendAdFinished(id, type, adsInSet, adNumber);

        public void SendRewardedOfferShown(string id, NX10OfferType type, int amount, NX10OfferTrigger trigger) =>
            EventsInternal.SendRewardedOfferShown(id, type, amount, trigger);

        public void SendRewardedOfferHidden(string id, NX10OfferType type, int amount, NX10OfferTrigger trigger, NX10OfferHideReason reason) =>
            EventsInternal.SendRewardedOfferHidden(id, type, amount, trigger, reason);

        public void SendAppQuit() => EventsInternal.AppQuit();

        public void SendAppBackgrounded() => EventsInternal.AppBackgrounded();

        public void SendAppForegrounded() => EventsInternal.AppForegrounded();

        #endregion

        public void StartTelemetry()
        {
            telemetryManager.SetTelemetryCollection(true);
        }

        public void StopTelemetry()
        {
            telemetryManager.SetTelemetryCollection(false);
        }

        private void SendTelemetryData(string windowStartTimestamp, double windowEndOffset, List<IInputEvent> inputEvents)
        {
            networkingManager.SendTelemetryData(windowStartTimestamp, windowEndOffset, inputEvents);
        }
    }
}

