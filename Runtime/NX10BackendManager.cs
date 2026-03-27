using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace NX10
{
    [Serializable]
    public class SessionConfig
    {
        public UserIdentifiers Identifiers;
        public AppProvidedData AppProvidedData;
    }

    [Serializable]
    public class UserIdentifiers
    {
        public string deviceId;
        public string email;
        public string phoneNumber;
    }

    [Serializable]
    public class SDKData
    {
        public DeviceInfo device;
        public string sdkVersion;
        public string sdkType;
    }

    [Serializable]
    public class DeviceInfo
    {
        public string type;
        public string os;
        public string osVersion;
        public string deviceVersion;
        public string deviceVariant;
    }

    [Serializable]
    public class AppProvidedData
    {
        public Dictionary<string, object> metaData;
        public string applicationVersion;
        public string buildNumber;
    }

    [Serializable]
    public class SAAQPrompt
    {
        public string triggerID;
        public string type;
        public string questionText;
        public List<SAAQAnswer> answers;
    }


    [Serializable]
    public class SAAQAnswer
    {
        public string displayName;
        public string feelingsType;
        public string id;
        public string suggestedEmoji;
    }


    public class NX10BackendManager : MonoBehaviour
    {
        [Serializable]
        public class SessionStartPayload
        {
            public string apiKey;
            public UserIdentifiers identifiers;
            public SDKData sdkProvided;
            public AppProvidedData appProvided;
        }

        [System.Serializable]
        public class NX10TelemetryPayload
        {
            public string bts;
            public double ets;
            public object[][] d;
        }

        [System.Serializable]
        public class NX10SAAQPayload
        {
            public string deviceSendTimestamp;
            public string promptDisplayTimestamp;
            public string promptAnswerTimestamp;
            public string feeling;
            public string saaqType;
            public string feelingContext;
            public string feelingFor;
            public Dictionary<string, object> metaData = new Dictionary<string, object>();
        }

        [System.Serializable]
        public class NX10SAAQTriggeredPayload
        {
            public string deviceSendTimestamp;
            public string promptDisplayTimestamp;
            public string promptAnswerTimestamp;
            public string triggerID;
            public string answerID;
            public Dictionary<string, object> metaData = new Dictionary<string, object>();
        }

        [Serializable]
        public class NX10AnalyticsPayload
        {
            public string eventName;
            public string sourceName;
            public string clientTimestamp;

            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public Dictionary<string, object> eventData;
        }

        [Serializable]
        public class SAAQRequest
        {
            public string status;
            public SAAQData data;

            public bool HasPrompt => data.prompt.triggerID != null;
        }

        [Serializable]
        public class SAAQData
        {
            public SAAQPrompt prompt;
        }

        [Serializable]
        public class AttributesPayload
        {
            public string timestamp;
            public Dictionary<string, object> data;
        }

       
        [Serializable]
        public class SessionStartResponse
        {
            public SessionStartData data;
        }

        [Serializable]
        public class SessionStartData
        {
            public string token;
            public List<EndpointInfo> endpoints;
        }

        [Serializable]
        public class EndpointInfo
        {
            public string location;
            public string type;
            public string version;
        }

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

        private Dictionary<string, object> currentGameAttributes = new Dictionary<string, object>();
        private NX10SDKSession currentSession;

        public Action<SAAQPrompt> OnPromptRequested;

        public void SetAttributes(Dictionary<string, object> attributes)
        {
            foreach (var kvp in attributes)
            {
                string key = kvp.Key;
                object value = kvp.Value;

                SetAttribute(key, value);
            }

            if (!NX10Manager.Instance.Initialised)
            {
                return;
            }

            SendAttributes();
        }

        public void SetAttribute(string key, object value, bool sendAttributes = false)
        {
            if (!NX10Manager.Instance.Initialised)
            {
                Debug.LogError("NX10 Manager not initialised, ensure it is before setting an attribute");
                return;
            }

            if (!currentGameAttributes.TryGetValue(key, out var existingValue))
            {
                currentGameAttributes[key] = value;
            }
            else if (!Equals(existingValue, value))
            {
                currentGameAttributes[key] = value;
            }

            if (sendAttributes)
                SendAttributes();
        }

        public void RemoveAttribute(string key)
        {
            if (!NX10Manager.Instance.Initialised)
            {
                Debug.LogError("NX10 Manager not initialised, ensure it is before removing an attribute");
                return;
            }

            if (currentGameAttributes.ContainsKey(key))
            {
                currentGameAttributes.Remove(key);
                SendAttributes();
            }
        }

        public void ClearAttributes()
        {
            if (!NX10Manager.Instance.Initialised)
            {
                Debug.LogError("NX10 Manager not initialised, ensure it is before clearing attributes");
                return;
            }

            currentGameAttributes.Clear();
            SendAttributes();
        }

        private void SendAttributes()
        {
            List<HeaderObject> headers = new List<HeaderObject>()
            {
                new HeaderObject("Authorization", "Bearer " + currentSession.Token)
            };

            string attributesEndPoint = currentSession.GetEndpoint("attributes", "v1");
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            AttributesPayload attributesPayload = new AttributesPayload()
            {
                timestamp = timestamp,
                data = currentGameAttributes
            };

            string attributeJson = JsonConvert.SerializeObject(attributesPayload);
            Debug.Log(attributeJson);
            StartCoroutine(NX10PostRequest(attributesEndPoint, attributeJson, (success, message) =>
            {
                if (success)
                {
                    
                }
            }, headers));
        }

        public void SendTelemetryData(string windowStartTimestamp, double windowEndOffset, List<IInputEvent> inputEvents)
        {          
            List<HeaderObject> headers = new List<HeaderObject>()
            {
                new HeaderObject("Authorization", "Bearer " + currentSession.Token)
            };

            string telemetryV2EndPoint = currentSession.GetEndpoint("telemetry", "v2");
            NX10TelemetryPayload telemetryPayload = new NX10TelemetryPayload()
            {
                bts = windowStartTimestamp,
                ets = windowEndOffset,
                d = inputEvents.Select(e => e.ToArray()).ToArray(),
            };

            string payloadJson = JsonConvert.SerializeObject(telemetryPayload);
            StartCoroutine(NX10PostRequest(telemetryV2EndPoint, payloadJson, (success, message) =>
            {
                HandleIncomingSAAQ(message);
                
            }, headers));
        }

        public void StartSession(SessionConfig sessionConfig, Action<bool> sessionStartSuccess)
        {
            currentSession = new NX10SDKSession();
            string apiKey = NX10RuntimeConfig.ApiKey;
            string endpoint = NX10RuntimeConfig.SessionStartEndpoint;

            PackageRuntimeData packageData = Resources.Load<PackageRuntimeData>("NX10PackageVersion");

            if (apiKey == string.Empty || endpoint == string.Empty)
                return;

            DeviceInfo deviceInfo = new DeviceInfo
            {
                type = SystemInfo.deviceType.ToString(),
                os = GetOSName(),
                osVersion = SystemInfo.operatingSystem,
                deviceVersion = SystemInfo.deviceModel,
                deviceVariant = SystemInfo.deviceName
            };

            SDKData sdkData = new SDKData
            {
                device = deviceInfo,
                sdkVersion = packageData.version,
                sdkType = "unity"
            };

            SessionStartPayload payload = new SessionStartPayload
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
            SessionStartResponse response = JsonUtility.FromJson<SessionStartResponse>(sessionStartJson);

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

            Debug.Log("Token: " + currentSession.Token);

            foreach(EndpointInfo endpointInfo in currentSession.Endpoints)
            {
                Debug.Log("EndPoint: " + endpointInfo.type + ", version: " + endpointInfo.version);
            }
        }

        private void HandleIncomingSAAQ(string json)
        {
            SAAQRequest request = JsonUtility.FromJson<SAAQRequest>(json);

            if (request.status == "success" && request.HasPrompt)
            {
                Debug.Log($"Trigger Received: {request.data.prompt.questionText}");
                OnPromptRequested.Invoke(request.data.prompt);
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

        public void SendTriggeredSAAQData(SAAQAnswer answer, string displayTimestamp, string answerTimestamp, string triggerId)
        {
            string timeStamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            string saaqEndpoint = currentSession.GetEndpoint("saaq-triggered", "v1");
            NX10SAAQTriggeredPayload payload = new NX10SAAQTriggeredPayload()
            {
                deviceSendTimestamp = timeStamp,
                promptDisplayTimestamp = displayTimestamp,
                promptAnswerTimestamp = answerTimestamp,
                triggerID = triggerId,
                answerID = answer.id,
                metaData = new Dictionary<string, object>(currentGameAttributes)
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
            string timeStamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            string saaqEndpoint = currentSession.GetEndpoint("saaq", "v1");
            NX10SAAQPayload saaqPayload = new NX10SAAQPayload()
            {
                deviceSendTimestamp = timeStamp,
                promptDisplayTimestamp = promptDisplayTimestamp,
                promptAnswerTimestamp = prompAnswerTimestamp,
                feeling = feeling,
                saaqType = feelingModalType,
                feelingContext = feelingContext,
                feelingFor = feelingFor,
                metaData = new Dictionary<string, object>(currentGameAttributes)
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

            Debug.Log(nx10jsonData);

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
    }
}

