using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace NX10
{
    public class NX10NetworkingManager : MonoBehaviour
    {
        #region Headers
        public class HeaderObject
        {
            public string headerName;
            public string headerValue;

            public HeaderObject(string headerName, string headerValue)
            {
                this.headerName = headerName;
                this.headerValue = headerValue;
            }
        }
        #endregion

        private NX10SDKSession currentSession;
        public NX10SDKSession CurrentSession => currentSession;

        // Retained in memory solely so GDPR access/forget requests still succeed if invoked right after SetConsent(false).
        private string _lastComplianceToken;

        private readonly HashSet<UnityWebRequest> _inFlightRequests = new HashSet<UnityWebRequest>();

        public Action<SAAQData> OnPromptRequested;
        public Action<CooldownData> OnCooldownRequested;

        public bool HasActiveSession => currentSession != null && !string.IsNullOrEmpty(currentSession.Token);
        private bool CanSendSessionData => NX10Manager.Instance.CanCollect && HasActiveSession;

        public void AbortAllRequests()
        {
            StopAllCoroutines();

            if (_inFlightRequests.Count == 0)
                return;

            var snapshot = _inFlightRequests.ToArray();
            _inFlightRequests.Clear();

            foreach (var req in snapshot)
            {
                try
                {
                    req?.Abort();
                }
                catch
                {
                }
            }
        }

        public void ClearSession(bool clearComplianceToken = false)
        {
            if (currentSession != null && !string.IsNullOrEmpty(currentSession.Token))
            {
                _lastComplianceToken = currentSession.Token;
            }

            if (clearComplianceToken)
            {
                _lastComplianceToken = null;
            }

            AbortAllRequests();
            currentSession?.Clear();
            currentSession = null;
        }

        private void OnDisable()
        {
            AbortAllRequests();
        }

        public void SendAttributes(Dictionary<string, object> currentGameAttributes)
        {
            if (!CanSendSessionData || currentGameAttributes == null)
                return;

            string attributesEndPoint = currentSession.GetEndpoint("attributes", "v1");
            if (string.IsNullOrEmpty(attributesEndPoint))
                return;

            List<HeaderObject> headers = new List<HeaderObject>()
            {
                new HeaderObject("Authorization", "Bearer " + currentSession.Token)
            };

            string timestamp = NX10Manager.Instance.GetCurrentTimestamp();
            NX10AttributesPayload attributesPayload = new NX10AttributesPayload()
            {
                timestamp = timestamp,
                data = currentGameAttributes
            };

            string attributeJson = JsonConvert.SerializeObject(attributesPayload);
            StartCoroutine(NX10PostRequest(attributesEndPoint, attributeJson, null, headers));
        }

        public byte[] CompressStringToGzip(string text)
        {
            byte[] rawData = Encoding.UTF8.GetBytes(text);
            using (MemoryStream outputStream = new MemoryStream())
            {
                using (GZipStream gzipStream = new GZipStream(outputStream, CompressionMode.Compress, true))
                {
                    gzipStream.Write(rawData, 0, rawData.Length);
                }
                return outputStream.ToArray();
            }
        }

        public async void SendTelemetryData(string windowStartTimestamp, double windowEndOffset, List<IInputEvent> inputEvents)
        {
            if (!CanSendSessionData || inputEvents == null || inputEvents.Count == 0)
                return;

            NX10SDKSession sessionSnapshot = currentSession;
            string token = sessionSnapshot.Token;
            string telemetryV2EndPoint = sessionSnapshot.GetEndpoint("telemetry", "v2");
            if (string.IsNullOrEmpty(telemetryV2EndPoint))
                return;

            bool useCompression = sessionSnapshot.telemetryCompression ?? false;
            IInputEvent[] inputEventsSnapshot = inputEvents.ToArray();

            List<HeaderObject> headers = new List<HeaderObject>()
            {
                new HeaderObject("Authorization", "Bearer " + token),
            };

            if (useCompression)
            {
                byte[] compressedData = await Task.Run(() =>
                {
                    NX10TelemetryPayload telemetryPayload = new NX10TelemetryPayload()
                    {
                        bts = windowStartTimestamp,
                        ets = windowEndOffset,
                        d = inputEventsSnapshot.Select(e => e.ToArray()).ToArray(),
                    };

                    string payloadJson = JsonConvert.SerializeObject(telemetryPayload);
                    return CompressStringToGzip(payloadJson);
                });

                if (!CanSendSessionData || currentSession != sessionSnapshot)
                    return;

                headers.Add(new HeaderObject("Content-Encoding", "gzip"));

                StartCoroutine(NX10PostRequest(telemetryV2EndPoint, compressedData, null, headers));
            }
            else
            {
                string payload = await Task.Run(() =>
                {
                    NX10TelemetryPayload telemetryPayload = new NX10TelemetryPayload()
                    {
                        bts = windowStartTimestamp,
                        ets = windowEndOffset,
                        d = inputEventsSnapshot.Select(e => e.ToArray()).ToArray(),
                    };

                    return JsonConvert.SerializeObject(telemetryPayload);
                });

                if (!CanSendSessionData || currentSession != sessionSnapshot)
                    return;

                StartCoroutine(NX10PostRequest(telemetryV2EndPoint, payload, null, headers));
            }
        }

        public void StartSession(SessionConfig sessionConfig, Action<bool> sessionStartSuccess)
        {
            if (!NX10Manager.Instance.HasConsent)
            {
                sessionStartSuccess?.Invoke(false);
                return;
            }

            string apiKey = NX10RuntimeConfig.ApiKey;
            string endpoint = NX10RuntimeConfig.SessionStartEndpoint;

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(endpoint))
            {
                Debug.LogError("[NX10] Cannot start session: API Key or SessionStartEndpoint is missing in NX10Config.");
                sessionStartSuccess?.Invoke(false);
                return;
            }

            currentSession = new NX10SDKSession();
            NX10SDKSession sessionBeingStarted = currentSession;

            NativeDeviceSettings nativeDeviceSettings = GetComponent<NativeDeviceSettings>();
            PackageRuntimeData packageData = Resources.Load<PackageRuntimeData>("NX10PackageVersion");

            DeviceInfo deviceInfo = new DeviceInfo
            {
                type = SystemInfo.deviceType.ToString(),
                os = GetOSName(),
                osVersion = SystemInfo.operatingSystem,
                deviceVersion = SystemInfo.deviceModel,
                deviceVariant = SystemInfo.deviceName,

                timezone = nativeDeviceSettings != null ? nativeDeviceSettings.TimeZone : TimeZoneInfo.Local.Id,
                locale = nativeDeviceSettings != null ? nativeDeviceSettings.Locale : System.Globalization.CultureInfo.CurrentCulture.Name,
            };

            SDKData sdkData = new SDKData
            {
                device = deviceInfo,
                sdkVersion = packageData != null ? packageData.version : "unknown",
                sdkType = "unity"
            };

            NX10SessionStartPayload payload = new NX10SessionStartPayload
            {
                apiKey = apiKey,
                identifiers = sessionConfig.Identifiers,
                sdkProvided = sdkData,
                appProvided = sessionConfig.AppProvidedData
            };

            string startSessionJson = JsonConvert.SerializeObject(payload);
            StartCoroutine(NX10PostRequest(endpoint, startSessionJson, (success, message) =>
            {
                if (!NX10Manager.Instance.HasConsent || currentSession != sessionBeingStarted)
                {
                    sessionStartSuccess?.Invoke(false);
                    return;
                }

                bool initialised = false;
                if (success)
                {
                    initialised = HandleSessionStartSuccess(message);
                }

                if (!initialised && currentSession == sessionBeingStarted)
                {
                    currentSession = null;
                }

                sessionStartSuccess?.Invoke(initialised);
            }));
        }

        private bool HandleSessionStartSuccess(string sessionStartJson)
        {
            SessionStartResponse response;
            try
            {
                response = JsonConvert.DeserializeObject<SessionStartResponse>(sessionStartJson);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NX10] Failed to deserialize SessionStartResponse: {ex.Message}");
                return false;
            }

            if (response == null)
            {
                Debug.LogError("[NX10] SessionInitResponse deserialized to null.");
                return false;
            }

            if (response.data == null || string.IsNullOrEmpty(response.data.token))
            {
                Debug.LogError("[NX10] Invalid session response data.");
                return false;
            }

            currentSession.Initialize(response.data);
            _lastComplianceToken = currentSession.Token;
            return true;
        }

        public void CheckPrompt()
        {
            if (!CanSendSessionData)
                return;

            string saaqPollEndpoint = currentSession.GetEndpoint("saaq-polling", "v1");
            if (string.IsNullOrEmpty(saaqPollEndpoint))
                return;

            List<HeaderObject> headers = new List<HeaderObject>()
            {
                new HeaderObject("Authorization", "Bearer " + currentSession.Token)
            };

            StartCoroutine(NX10GetRequest(saaqPollEndpoint, (success, message) =>
            {
                if (success && CanSendSessionData)
                    _ = HandleIncomingSAAQ(message);

            }, headers));
        }

        private async Task HandleIncomingSAAQ(string json)
        {
            var (isCooldown, cooldownData, isPrompt, promptData) = await Task.Run(() =>
            {
                if (TryDeserialize(json, out CooldownResponse cooldown) && cooldown?.status == "success" && cooldown.data != null)
                {
                    return (true, cooldown.data, false, (SAAQData)null);
                }

                if (TryDeserialize(json, out SAAQResponse response) && response?.status == "success" && response.HasPrompt == true)
                {
                    return (false, (CooldownData)null, true, response.data);
                }

                return (false, (CooldownData)null, false, (SAAQData)null);
            });

            if (!CanSendSessionData)
                return;

            if (isCooldown)
            {
                OnCooldownRequested?.Invoke(cooldownData);
            }
            else if (isPrompt)
            {
                Debug.Log($"Trigger Received: {promptData.prompt.questionText}");
                OnPromptRequested?.Invoke(promptData);
            }
        }

        private bool TryDeserialize<T>(string json, out T result)
        {
            try
            {
                result = JsonConvert.DeserializeObject<T>(json);
                return result != null;
            }
            catch (JsonException)
            {
                result = default;
                return false;
            }
        }

        private string GetOSName()
        {
#if UNITY_EDITOR
            return "Editor";
#elif UNITY_IOS
            return "iOS";
#elif UNITY_ANDROID
            return "Android";
#else
            return "Unknown";
#endif
        }

        public void SendTriggeredSAAQData(SAAQAnswer answer, string displayTimestamp, string closedTimestamp, string triggerId)
        {
            if (!CanSendSessionData)
                return;

            string saaqEndpoint = currentSession.GetEndpoint("saaq-triggered", "v1");
            if (string.IsNullOrEmpty(saaqEndpoint))
                return;

            string timestamp = NX10Manager.Instance.GetCurrentTimestamp();
            NX10SAAQTriggeredPayload payload = new NX10SAAQTriggeredPayload()
            {
                triggerID = triggerId,
                answer = answer,
                deviceSendTimestamp = timestamp,
                promptDisplayTimestamp = displayTimestamp,
                promptClosedTimestamp = closedTimestamp,
                metaData = new Dictionary<string, object>()
            };

            string nx10jsonData = JsonConvert.SerializeObject(payload);
            List<HeaderObject> headers = new List<HeaderObject>()
            {
                new HeaderObject("Authorization", "Bearer " + currentSession.Token)
            };

            StartCoroutine(NX10PostRequest(saaqEndpoint, nx10jsonData, null, headers));
        }

        public void SendSaaqData(string feeling, int ranking, string feelingModalType, string feelingContext, string feelingFor, string promptDisplayTimestamp, string prompAnswerTimestamp)
        {
            if (!CanSendSessionData)
                return;

            string saaqEndpoint = currentSession.GetEndpoint("saaq", "v1");
            if (string.IsNullOrEmpty(saaqEndpoint))
                return;

            string timestamp = NX10Manager.Instance.GetCurrentTimestamp();
            NX10SAAQPayload saaqPayload = new NX10SAAQPayload()
            {
                deviceSendTimestamp = timestamp,
                promptDisplayTimestamp = promptDisplayTimestamp,
                promptAnswerTimestamp = prompAnswerTimestamp,
                feeling = feeling,
                saaqType = feelingModalType,
                feelingContext = feelingContext,
                feelingFor = feelingFor,
                metaData = new Dictionary<string, object>()
            };

            string nx10jsonData = JsonConvert.SerializeObject(saaqPayload);
            List<HeaderObject> headers = new List<HeaderObject>()
            {
                new HeaderObject("Authorization", "Bearer " + currentSession.Token)
            };

            StartCoroutine(NX10PostRequest(saaqEndpoint, nx10jsonData, null, headers));
        }

        public void SendAnalytics(string eventName, string sourceName, string timeStamp, Dictionary<string, object> metdata = null)
        {
            if (!CanSendSessionData)
                return;

            string analyticsEndpoint = currentSession.GetEndpoint("analytics", "v1");
            if (string.IsNullOrEmpty(analyticsEndpoint))
                return;

            NX10AnalyticsPayload analyticsPayload = new NX10AnalyticsPayload()
            {
                eventName = eventName,
                sourceName = sourceName,
                clientTimestamp = timeStamp,
            };

            if (metdata != null)
            {
                analyticsPayload.eventData = metdata;
            }

            string nx10jsonData = JsonConvert.SerializeObject(analyticsPayload);
            List<HeaderObject> headers = new List<HeaderObject>()
            {
                new HeaderObject("Authorization", "Bearer " + currentSession.Token)
            };

            StartCoroutine(NX10PostRequest(analyticsEndpoint, nx10jsonData, null, headers));
        }

        public void RequestActivity(Action<KineticState> activityAction)
        {
            if (!CanSendSessionData || currentSession.stationaryThreshold == null || currentSession.movingMinThreshold == null)
            {
                activityAction?.Invoke(KineticState.Unknown);
                return;
            }

            string activityEndpoint = currentSession.GetEndpoint("activity", "v1");
            if (string.IsNullOrEmpty(activityEndpoint))
            {
                activityAction?.Invoke(KineticState.Unknown);
                return;
            }

            NX10ActivityPayload activityPayload = new NX10ActivityPayload()
            {
                stationaryMaxThreshold = currentSession.stationaryThreshold.Value,
                movingMinThreshold = currentSession.movingMinThreshold.Value,
            };

            List<HeaderObject> headers = new List<HeaderObject>()
            {
                new HeaderObject("Authorization", "Bearer " + currentSession.Token)
            };

            string nx10jsonData = JsonConvert.SerializeObject(activityPayload);
            StartCoroutine(NX10PostRequest(activityEndpoint, nx10jsonData, (success, message) =>
            {
                KineticState kineticState = KineticState.Unknown;
                if (success && CanSendSessionData)
                {
                    kineticState = ParseActivityJson(message);
                }

                activityAction?.Invoke(kineticState);

            }, headers));
        }

        public KineticState ParseActivityJson(string jsonString)
        {
            try
            {
                RootActivityResponse response = JsonConvert.DeserializeObject<RootActivityResponse>(jsonString);

                if (response != null && response.status == "success")
                {
                    KineticState state = response.data.device.kineticState;
                    return state;
                }

                return KineticState.Unknown;
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                return KineticState.Unknown;
            }
        }

        public void RequestAffect(Action<Affect, float> affectAndConfidenceAction)
        {
            if (!CanSendSessionData)
            {
                affectAndConfidenceAction?.Invoke(Affect.Unknown, 0f);
                return;
            }

            string affectEndpoint = currentSession.GetEndpoint("frustration", "v1");
            if (string.IsNullOrEmpty(affectEndpoint))
            {
                affectAndConfidenceAction?.Invoke(Affect.Unknown, 0f);
                return;
            }

            List<HeaderObject> headers = new List<HeaderObject>()
            {
                new HeaderObject("Authorization", "Bearer " + currentSession.Token)
            };

            StartCoroutine(NX10GetRequest(affectEndpoint, (success, message) =>
            {
                Affect affect = Affect.Unknown;
                float confidence = 0f;
                if (success && CanSendSessionData)
                {
                    affect = ParseAffectJson(message, out float conf);
                    confidence = conf;
                }

                affectAndConfidenceAction?.Invoke(affect, confidence);

            }, headers));
        }

        public Affect ParseAffectJson(string jsonString, out float confidence)
        {
            try
            {
                RootRedGreenResponse response = JsonConvert.DeserializeObject<RootRedGreenResponse>(jsonString);

                if (response != null && response.status == "success")
                {
                    Affect state = response.data.currentAffect;
                    confidence = response.data.confidence;

                    return state;
                }

                confidence = 0.0f;
                return Affect.Unknown;
            }
            catch (System.Exception e)
            {
                confidence = 0.0f;

                Debug.LogException(e);

                return Affect.Unknown;
            }
        }

        public void SendEvent(string eventName, string timeStamp, string outcome = null, Dictionary<string, object> eventData = null)
        {
            if (!CanSendSessionData)
            {
                return;
            }

            string eventsEndpoint = currentSession.GetEndpoint("events", "v1");
            if (string.IsNullOrEmpty(eventsEndpoint))
                return;

            NX10EventsPayload eventsPayload = new NX10EventsPayload()
            {
                eventName = eventName,
                timestamp = timeStamp,
                data = eventData ?? new Dictionary<string, object>()
            };

            if (outcome != null)
            {
                eventsPayload.outcome = outcome;
            }

            string nx10jsonData = JsonConvert.SerializeObject(eventsPayload);
            List<HeaderObject> headers = new List<HeaderObject>()
            {
                new HeaderObject("Authorization", "Bearer " + currentSession.Token)
            };

            StartCoroutine(NX10PostRequest(eventsEndpoint, nx10jsonData, null, headers));
        }

        public void PersonalDataRequested(bool dryRun, string timeStamp, Action<bool, string> completedAction)
        {
            string complianceToken = HasActiveSession ? currentSession.Token : _lastComplianceToken;
            if (string.IsNullOrEmpty(complianceToken))
            {
                Debug.LogWarning("[NX10] Cannot request personal data without a session token.");
                completedAction?.Invoke(false, string.Empty);
                return;
            }

            string prefix = NX10RuntimeConfig.IngestionPrefix;
            string url = prefix + "compliance/access";
            NX10DataPayload payload = new NX10DataPayload()
            {
                datetimeRequested = timeStamp,
                dryRun = dryRun,
            };

            string nx10jsonData = JsonConvert.SerializeObject(payload);

            List<HeaderObject> headers = new List<HeaderObject>()
            {
                new HeaderObject("Authorization", "Bearer " + complianceToken)
            };

            StartCoroutine(NX10PostRequest(url, nx10jsonData, (success, message) =>
            {
                if (success)
                {
                    RootDataRequestResponse response = JsonConvert.DeserializeObject<RootDataRequestResponse>(message);
                    completedAction?.Invoke(true, response?.data?.requestUrl ?? string.Empty);
                }
                else
                {
                    completedAction?.Invoke(false, string.Empty);
                }

            }, headers, trackForAbort: false));
        }

        public void PersonalDataDeletionRequested(bool dryRun, string timeStamp, Action<bool> completedAction)
        {
            string complianceToken = HasActiveSession ? currentSession.Token : _lastComplianceToken;
            if (string.IsNullOrEmpty(complianceToken))
            {
                Debug.LogWarning("[NX10] Cannot request personal data deletion without a session token.");
                completedAction?.Invoke(false);
                return;
            }

            string prefix = NX10RuntimeConfig.IngestionPrefix;
            string url = prefix + "compliance/forget";
            NX10DataPayload payload = new NX10DataPayload()
            {
                datetimeRequested = timeStamp,
                dryRun = dryRun,
            };

            string nx10jsonData = JsonConvert.SerializeObject(payload);

            List<HeaderObject> headers = new List<HeaderObject>()
            {
                new HeaderObject("Authorization", "Bearer " + complianceToken)
            };

            StartCoroutine(NX10PostRequest(url, nx10jsonData, (success, message) =>
            {
                if (success && !dryRun)
                {
                    _lastComplianceToken = null;
                }
                completedAction?.Invoke(success);
            }, headers, trackForAbort: false));
        }

        public IEnumerator NX10PostRequest(string uri, byte[] bodyData, System.Action<bool, string> onComplete = null, List<HeaderObject> additionalHeaders = null, bool trackForAbort = true)
        {
            using (UnityWebRequest request = new UnityWebRequest(uri, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyData);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                if (additionalHeaders != null)
                {
                    foreach (HeaderObject headerObject in additionalHeaders)
                    {
                        request.SetRequestHeader(headerObject.headerName, headerObject.headerValue);
                    }
                }

                if (trackForAbort)
                {
                    _inFlightRequests.Add(request);
                }

                bool success = false;
                string responseMessage = "";

                try
                {
                    yield return request.SendWebRequest();

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        success = true;
                        responseMessage = request.downloadHandler.text;
                    }
                    else if (!IsIntentionalAbort(request))
                    {
                        Debug.LogError("Post failed:");
                        Debug.LogError("Result: " + request.result);
                        Debug.LogError("Error: " + request.error);
                        Debug.LogError("Response Code: " + request.responseCode);
                        Debug.LogError("Response Body: " + request.downloadHandler?.text);
                    }
                }
                finally
                {
                    _inFlightRequests.Remove(request);
                }

                onComplete?.Invoke(success, responseMessage);
            }
        }

        public IEnumerator NX10PostRequest(string uri, string jsonBody, System.Action<bool, string> onComplete = null, List<HeaderObject> additionalHeaders = null, bool trackForAbort = true)
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            yield return NX10PostRequest(uri, bodyRaw, onComplete, additionalHeaders, trackForAbort);
        }

        public IEnumerator NX10GetRequest(string uri, System.Action<bool, string> onComplete = null, List<HeaderObject> additionalHeaders = null, bool trackForAbort = true)
        {
            using (UnityWebRequest request = new UnityWebRequest(uri, "GET"))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                if (additionalHeaders != null)
                {
                    foreach (HeaderObject headerObject in additionalHeaders)
                    {
                        request.SetRequestHeader(headerObject.headerName, headerObject.headerValue);
                    }
                }

                if (trackForAbort)
                {
                    _inFlightRequests.Add(request);
                }

                bool success = false;
                string responseMessage = "";

                try
                {
                    yield return request.SendWebRequest();

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        success = true;
                        responseMessage = request.downloadHandler.text;
                    }
                    else if (!IsIntentionalAbort(request))
                    {
                        Debug.LogError("Get failed:");
                        Debug.LogError("Result: " + request.result);
                        Debug.LogError("Error: " + request.error);
                        Debug.LogError("Response Code: " + request.responseCode);
                        Debug.LogError("Response Body: " + request.downloadHandler?.text);
                    }
                }
                finally
                {
                    _inFlightRequests.Remove(request);
                }

                onComplete?.Invoke(success, responseMessage);
            }
        }

        private static bool IsIntentionalAbort(UnityWebRequest request)
        {
            return request != null &&
                   !string.IsNullOrEmpty(request.error) &&
                   request.error.IndexOf("aborted", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
