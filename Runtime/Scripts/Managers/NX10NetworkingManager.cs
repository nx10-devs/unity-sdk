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

        internal const float MetricsRetryInitialDelaySeconds = 2f;
        internal const float MetricsRetryMaxDelaySeconds = 60f;
        internal const float MetricsRetryBackoffMultiplier = 2f;
        internal const float MetricsRetryJitterFraction = 0.2f;

        internal delegate IEnumerator MetricsHttpTransportDelegate(string uri, string bearerToken, Action<bool, string> onComplete);

        internal MetricsHttpTransportDelegate MetricsTransportOverride { get; set; }
        internal string MetricsEndpointOverride { get; set; }
        internal Func<float, IEnumerator> MetricsRetryWaitOverride { get; set; }

        private Coroutine _metricsRefreshRoutine;
        private UnityWebRequest _activeMetricsWebRequest;
        private int _metricsRefreshGeneration;
        private bool _isMetricsRefreshPaused;

        public bool IsRefreshingMetrics => _metricsRefreshRoutine != null;

        internal void SetSessionForTesting(NX10SDKSession session)
        {
            currentSession = session;
        }

        public void SendAttributes(Dictionary<string, object> currentGameAttributes)
        {
            List<HeaderObject> headers = new List<HeaderObject>()
            {
                new HeaderObject("Authorization", "Bearer " + currentSession.Token)
            };
            
            string attributesEndPoint = currentSession.GetEndpoint("attributes", "v1");
            string timestamp = NX10Manager.Instance.GetCurrentTimestamp();
            NX10AttributesPayload attributesPayload = new NX10AttributesPayload()
            {
                timestamp = timestamp,
                data = currentGameAttributes
            };

            string attributeJson = JsonConvert.SerializeObject(attributesPayload);
            StartCoroutine(NX10PostRequest(attributesEndPoint, attributeJson, (success, message) =>
            {
                if (success)
                {
                    
                }
            }, headers));
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
            if (currentSession == null)
            {
                return;
            }

            string token = currentSession.Token;
            string telemetryV2EndPoint = currentSession.GetEndpoint("telemetry", "v2");
            if (string.IsNullOrEmpty(telemetryV2EndPoint))
            {
                return;
            }
            IInputEvent[] inputEventsSnapshot = inputEvents.ToArray();

            List<HeaderObject> headers = new List<HeaderObject>()
            {
                new HeaderObject("Authorization", "Bearer " + token),
            };

            if (CurrentSession?.telemetryCompression == true)
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

                headers.Add(new HeaderObject("Content-Encoding", "gzip"));

                StartCoroutine(NX10PostRequest(telemetryV2EndPoint, compressedData, (success, message) =>
                {

                }, headers));
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

                    string payloadJson = JsonConvert.SerializeObject(telemetryPayload);
                    return payloadJson;
                });

                StartCoroutine(NX10PostRequest(telemetryV2EndPoint, payload, (success, message) =>
                {

                }, headers));
            }
        }

        public void StartSession(SessionConfig sessionConfig, Action<bool> sessionStartSuccess)
        {
            CancelMetricsRefresh();
            currentSession = new NX10SDKSession();
            string apiKey = NX10RuntimeConfig.ApiKey;
            string endpoint = NX10RuntimeConfig.SessionStartEndpoint;

            NativeDeviceSettings nativeDeviceSettings = GetComponent<NativeDeviceSettings>();

            PackageRuntimeData packageData = Resources.Load<PackageRuntimeData>("NX10PackageVersion");

            if (apiKey == string.Empty || endpoint == string.Empty)
                return;

            DeviceInfo deviceInfo = new DeviceInfo
            {
                type = SystemInfo.deviceType.ToString(),
                os = GetOSName(),
                osVersion = SystemInfo.operatingSystem,
                deviceVersion = SystemInfo.deviceModel,
                deviceVariant = SystemInfo.deviceName,

                timezone = nativeDeviceSettings.TimeZone,
                locale = nativeDeviceSettings.Locale,
            };

            SDKData sdkData = new SDKData
            {
                device = deviceInfo,
                sdkVersion = packageData.version,
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
                if (success)
                {
                    HandleSessionStartSuccess(message);
                }

                sessionStartSuccess?.Invoke(success);
            }));
        }

        private void HandleSessionStartSuccess(string sessionStartJson)
        {
            SessionStartResponse response = JsonConvert.DeserializeObject<SessionStartResponse>(sessionStartJson);

            if (response == null)
            {
                Debug.LogError("SessionInitResponse deserialized to null.");
                return;
            }

            if (response.data == null || string.IsNullOrEmpty(response.data.token))
            {
                Debug.LogError("Invalid session response data.");
                return;
            }

            currentSession.Initialize(response.data);
        }

        public void SetMetricsRefreshPaused(bool paused)
        {
            _isMetricsRefreshPaused = paused;
        }

        public void CancelMetricsRefresh()
        {
            _metricsRefreshGeneration++;

            if (_metricsRefreshRoutine != null)
            {
                StopCoroutine(_metricsRefreshRoutine);
                _metricsRefreshRoutine = null;
            }

            CleanupActiveMetricsWebRequest();
        }

        private void CleanupActiveMetricsWebRequest()
        {
            if (_activeMetricsWebRequest != null)
            {
                try { _activeMetricsWebRequest.Abort(); } catch { }
                try { _activeMetricsWebRequest.Dispose(); } catch { }
                _activeMetricsWebRequest = null;
            }
        }

        /// <summary>
        /// Refetches cloud macro metrics from /routes/metrics for the active session.
        /// Retains existing session metrics on failure and retries with exponential backoff until success.
        /// </summary>
        public void RefreshMetrics(Func<bool> canContinue = null, Action<bool> onCompleted = null)
        {
            CancelMetricsRefresh();

            Debug.Log("[NX10] Refreshing macro metrics...");    

            NX10SDKSession targetSession = currentSession;
            if (targetSession == null || string.IsNullOrEmpty(targetSession.Token))
            {
                onCompleted?.Invoke(false);
                return;
            }

            string endpoint = !string.IsNullOrWhiteSpace(MetricsEndpointOverride)
                ? MetricsEndpointOverride
                : NX10RuntimeConfig.MetricsEndpoint;

            if (string.IsNullOrWhiteSpace(endpoint))
            {
                Debug.LogWarning("[NX10] Cannot refresh macro metrics: MetricsEndpoint is empty.");
                onCompleted?.Invoke(false);
                return;
            }

            int generation = _metricsRefreshGeneration;
            _metricsRefreshRoutine = StartCoroutine(RefreshMetricsRoutine(endpoint, targetSession, generation, canContinue, onCompleted));
        }

        internal IEnumerator RefreshMetricsRoutine(
            string endpoint,
            NX10SDKSession targetSession,
            int generation,
            Func<bool> canContinue = null,
            Action<bool> onCompleted = null)
        {
            int failedAttempts = 0;

            while (IsRefreshStillValid(targetSession, generation, canContinue))
            {
                while (_isMetricsRefreshPaused && IsRefreshStillValid(targetSession, generation, canContinue))
                {
                    yield return null;
                }

                if (!IsRefreshStillValid(targetSession, generation, canContinue))
                {
                    break;
                }

                bool requestSucceeded = false;
                string responseBody = string.Empty;

                if (MetricsTransportOverride != null)
                {
                    yield return MetricsTransportOverride(endpoint, targetSession.Token, (ok, body) =>
                    {
                        requestSucceeded = ok;
                        responseBody = body;
                    });
                }
                else
                {
                    yield return SendMetricsGetRequest(endpoint, targetSession.Token, (ok, body) =>
                    {
                        requestSucceeded = ok;
                        responseBody = body;
                    });
                }

                if (!IsRefreshStillValid(targetSession, generation, canContinue))
                {
                    break;
                }

                if (requestSucceeded && TryApplyMetricsResponse(targetSession, responseBody))
                {
                    if (generation == _metricsRefreshGeneration)
                    {
                        _metricsRefreshRoutine = null;
                    }
                    onCompleted?.Invoke(true);
                    yield break;
                }

                failedAttempts++;
                float delaySeconds = ComputeBackoffDelaySeconds(failedAttempts, UnityEngine.Random.value);

                if (failedAttempts == 1)
                {
                    Debug.LogWarning($"[NX10] Macro metrics refresh failed; retaining existing values and retrying in {delaySeconds:F1}s.");
                }

                if (MetricsRetryWaitOverride != null)
                {
                    yield return MetricsRetryWaitOverride(delaySeconds);
                }
                else
                {
                    float elapsed = 0f;
                    while (elapsed < delaySeconds && IsRefreshStillValid(targetSession, generation, canContinue))
                    {
                        if (!_isMetricsRefreshPaused)
                        {
                            elapsed += Time.unscaledDeltaTime;
                        }
                        yield return null;
                    }
                }
            }

            if (generation == _metricsRefreshGeneration)
            {
                _metricsRefreshRoutine = null;
            }
            onCompleted?.Invoke(false);
        }

        private bool IsRefreshStillValid(NX10SDKSession targetSession, int generation, Func<bool> canContinue)
        {
            return generation == _metricsRefreshGeneration &&
                   targetSession != null &&
                   ReferenceEquals(targetSession, currentSession) &&
                   !string.IsNullOrEmpty(targetSession.Token) &&
                   (canContinue == null || canContinue());
        }

        internal bool TryApplyMetricsResponse(NX10SDKSession targetSession, string responseJson)
        {
            if (targetSession == null ||
                !ReferenceEquals(targetSession, currentSession) ||
                string.IsNullOrWhiteSpace(responseJson))
            {
                return false;
            }

            try
            {
                MetricsRefreshResponse response = JsonConvert.DeserializeObject<MetricsRefreshResponse>(responseJson);
                if (response?.data?.metrics == null)
                {
                    return false;
                }

                return targetSession.UpdateMetrics(response.data.metrics);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NX10] Failed to parse metrics refresh response: {ex.Message}");
                return false;
            }
        }

        internal static float ComputeBackoffDelaySeconds(int failedAttemptCount, float jitterSampleZeroToOne = 0.5f)
        {
            int clampedExponent = Mathf.Clamp(failedAttemptCount - 1, 0, 10);
            float baseDelay = Mathf.Min(
                MetricsRetryMaxDelaySeconds,
                MetricsRetryInitialDelaySeconds * Mathf.Pow(MetricsRetryBackoffMultiplier, clampedExponent));

            float normalizedJitter = (Mathf.Clamp01(jitterSampleZeroToOne) * 2f) - 1f; // [-1, +1]
            float jitteredDelay = baseDelay * (1f + (normalizedJitter * MetricsRetryJitterFraction));

            return Mathf.Clamp(jitteredDelay, 0.5f, MetricsRetryMaxDelaySeconds);
        }

        private IEnumerator SendMetricsGetRequest(string uri, string bearerToken, Action<bool, string> onComplete)
        {
            CleanupActiveMetricsWebRequest();

            UnityWebRequest request = new UnityWebRequest(uri, UnityWebRequest.kHttpVerbGET);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + bearerToken);

            _activeMetricsWebRequest = request;

            yield return request.SendWebRequest();

            bool success = request.result == UnityWebRequest.Result.Success;
            string responseText = success && request.downloadHandler != null
                ? request.downloadHandler.text
                : string.Empty;

            if (ReferenceEquals(_activeMetricsWebRequest, request))
            {
                _activeMetricsWebRequest = null;
            }

            request.Dispose();
            onComplete?.Invoke(success, responseText);
        }

        private void OnDestroy()
        {
            CancelMetricsRefresh();
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
            string timestamp = NX10Manager.Instance.GetCurrentTimestamp();
            string saaqEndpoint = currentSession.GetEndpoint("saaq-triggered", "v1");
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
            Debug.Log(nx10jsonData);
            List<HeaderObject> headers = new List<HeaderObject>()
            {
                new HeaderObject("Authorization", "Bearer " + currentSession.Token)
            };

            StartCoroutine(NX10PostRequest(saaqEndpoint, nx10jsonData, (success, message) =>
            {
                if (success)
                {

                }
                else
                {

                }
            }, headers));
        }

        public void SendSaaqData(string feeling, int ranking, string feelingModalType, string feelingContext, string feelingFor, string promptDisplayTimestamp, string prompAnswerTimestamp)
        {
            string timestamp = NX10Manager.Instance.GetCurrentTimestamp();
            string saaqEndpoint = currentSession.GetEndpoint("saaq", "v1");
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
            Debug.Log(nx10jsonData);
            List<HeaderObject> headers = new List<HeaderObject>()
            {
                new HeaderObject("Authorization", "Bearer " + currentSession.Token)
            };

            StartCoroutine(NX10PostRequest(saaqEndpoint, nx10jsonData, (success, message) =>
            {
                if (success)
                {

                }
                else
                {

                }
            }, headers));
        }

        public void SendAnalytics(string eventName, string sourceName, string timeStamp, Dictionary<string, object> metdata = null)
        {
            string analyticsEndpoint = currentSession.GetEndpoint("analytics", "v1");
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

            StartCoroutine(NX10PostRequest(analyticsEndpoint, nx10jsonData, (success, message) =>
            {
                if (success)
                {

                }
                else
                {

                }
            }, headers));
        }
        
        public void SendEvent(string eventName, string timeStamp, string outcome = null, Dictionary<string, object> eventData = null)
        {
            if (!NX10Manager.Instance.IsSessionValid)
            {
                Debug.LogWarning("NX10 Manager not initialised, ensure it is before sending event");
                return;
            }

            string eventsEndpoint = currentSession?.GetEndpoint("events", "v1");
            if (string.IsNullOrEmpty(eventsEndpoint))
            {
                return;
            }
            NX10EventsPayload eventsPayload = new NX10EventsPayload()
            {
                eventName = eventName,
                timestamp = timeStamp,
                data = new Dictionary<string, object>()
            };

            if (eventData != null)
            {
                eventsPayload.data = eventData;
            }

            if (outcome != null)
            {
                eventsPayload.outcome = outcome;
            }

            string nx10jsonData = JsonConvert.SerializeObject(eventsPayload);
            List<HeaderObject> headers = new List<HeaderObject>()
            {
                new HeaderObject("Authorization", "Bearer " + currentSession.Token)
            };

            StartCoroutine(NX10PostRequest(eventsEndpoint, nx10jsonData, (success, message) =>
            {
                if (success)
                {

                }
                else
                {

                }
            }, headers));
        }

        public void PersonalDataRequested(bool dryRun, string timeStamp, Action<bool, string> completedAction)
        {
            string prefix = NX10RuntimeConfig.IngestionPrefix;
            string url = prefix + "compliance/access";
            NX10DataPayload payload = new NX10DataPayload()
            {
                datetimeRequested = timeStamp,
                dryRun = true,
            };

            string nx10jsonData = JsonConvert.SerializeObject(payload);

            List<HeaderObject> headers = new List<HeaderObject>()
            {
                new HeaderObject("Authorization", "Bearer " + currentSession.Token)
            };

            StartCoroutine(NX10PostRequest(url, nx10jsonData, (success, message) =>
            {
                if (success)
                {
                    RootDataRequestResponse response = JsonConvert.DeserializeObject<RootDataRequestResponse>(message);
                    completedAction.Invoke(success, response.data.requestUrl);
                }
                else
                {
                    completedAction.Invoke(success, "");
                }


            }, headers));
        }

        public void PersonalDataDeletionRequested(bool dryRun, string timeStamp, Action<bool> completedAction)
        {
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
                new HeaderObject("Authorization", "Bearer " + currentSession.Token)
            };

            StartCoroutine(NX10PostRequest(url, nx10jsonData, (success, message) =>
            {
                completedAction.Invoke(success);

            }, headers));
        }

        public IEnumerator NX10PostRequest(string uri, byte[] bodyData, System.Action<bool, string> onComplete = null, List<HeaderObject> additionalHeaders = null)
        {
            UnityWebRequest request = new UnityWebRequest(uri, "POST");

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

            bool success = false;
            string responseMessage = "";

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                success = true;
                responseMessage = request.downloadHandler.text;
            }
            else
            {
                Debug.LogError("Post failed:");
                Debug.LogError("Result: " + request.result);
                Debug.LogError("Error: " + request.error);
                Debug.LogError("Response Code: " + request.responseCode);
                Debug.LogError("Response Body: " + request.downloadHandler.text);
            }

            onComplete?.Invoke(success, responseMessage);
        }

        public IEnumerator NX10PostRequest(string uri, string jsonBody, System.Action<bool, string> onComplete = null, List<HeaderObject> additionalHeaders = null)
        {
            UnityWebRequest request = new UnityWebRequest(uri, "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            if (additionalHeaders != null)
            {
                foreach (HeaderObject headerObject in additionalHeaders)
                {
                    request.SetRequestHeader(headerObject.headerName, headerObject.headerValue);
                }
            }

            bool success = false;
            string responseMessage = "";

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                success = true;
                responseMessage = request.downloadHandler.text;
            }
            else
            {
                Debug.LogError("Post failed:");
                Debug.LogError("Result: " + request.result);
                Debug.LogError("Error: " + request.error);
                Debug.LogError("Response Code: " + request.responseCode);
                Debug.LogError("Response Body: " + request.downloadHandler.text);
            }

            onComplete?.Invoke(success, responseMessage);
        }

        public IEnumerator NX10GetRequest(string uri, System.Action<bool, string> onComplete = null, List<HeaderObject> additionalHeaders = null)
        {
            UnityWebRequest request = new UnityWebRequest(uri, "GET");

            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            if (additionalHeaders != null)
            {
                foreach (HeaderObject headerObject in additionalHeaders)
                {
                    request.SetRequestHeader(headerObject.headerName, headerObject.headerValue);
                }
            }

            bool success = false;
            string responseMessage = "";

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                success = true;
                responseMessage = request.downloadHandler.text;
            }
            else
            {
                Debug.LogError("GET failed:");
                Debug.LogError("Result: " + request.result);
                Debug.LogError("Error: " + request.error);
                Debug.LogError("Response Code: " + request.responseCode);
                Debug.LogError("Response Body: " + request.downloadHandler.text);
            }

            onComplete?.Invoke(success, responseMessage);
        }
    }
}

