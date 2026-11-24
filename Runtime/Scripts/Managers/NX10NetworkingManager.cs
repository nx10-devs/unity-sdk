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
            string token = currentSession.Token;
            string telemetryV2EndPoint = currentSession.GetEndpoint("telemetry", "v2");
            IInputEvent[] inputEventsSnapshot = inputEvents.ToArray();

            List<HeaderObject> headers = new List<HeaderObject>()
            {
                new HeaderObject("Authorization", "Bearer " + token),
            };

            if (CurrentSession.telemetryCompression.Value)
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

            string eventsEndpoint = currentSession.GetEndpoint("events", "v1");
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
                Debug.LogError("Post failed:");
                Debug.LogError("Result: " + request.result);
                Debug.LogError("Error: " + request.error);
                Debug.LogError("Response Code: " + request.responseCode);
                Debug.LogError("Response Body: " + request.downloadHandler.text);
            }

            onComplete?.Invoke(success, responseMessage);
        }
    }
}

