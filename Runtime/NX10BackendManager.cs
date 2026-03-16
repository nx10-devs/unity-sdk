using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using static UnityEngine.InputSystem.LowLevel.InputEventTrace;

namespace NX10
{
    [Serializable]
    public class SessionConfig
    {
        public Identifiers Identifiers;
        public AppProvidedData AppProvidedData;
    }

    [Serializable]
    public class Identifiers
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

    public class NX10BackendManager : MonoBehaviour
    {
        [Serializable]
        public class SessionStartPayload
        {
            public string apiKey;
            public Identifiers identifiers;
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

        private Dictionary<string, object> currentGameMetaData = new Dictionary<string, object>();
        private NX10SDKSession currentSession;

        public void UpdateAttributes(Dictionary<string, object> metaData)
        {
            var changedValues = new Dictionary<string, object>();

            foreach (var kvp in metaData)
            {
                if (!currentGameMetaData.TryGetValue(kvp.Key, out var existingValue))
                {
                    currentGameMetaData[kvp.Key] = kvp.Value;
                    changedValues[kvp.Key] = kvp.Value;
                }
                else if (!Equals(existingValue, kvp.Value))
                {
                    currentGameMetaData[kvp.Key] = kvp.Value;
                    changedValues[kvp.Key] = kvp.Value;
                }
            }

            if(changedValues.Count > 0)
            {
                SendAttributes(currentGameMetaData);
            }
        }

        private void SendAttributes(Dictionary<string, object> newAttributes)
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
                data = newAttributes
            };

            string attributeJson = JsonConvert.SerializeObject(attributesPayload);
            Debug.Log(attributeJson);
            StartCoroutine(NX10PostRequest(attributesEndPoint, attributeJson, (success, message) =>
            {
                if (success)
                {
                    Debug.Log("Attribute Success");
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
                
            }, headers));
        }

        public void StartSession(SessionConfig sessionConfig, Action<bool> sessionStartSuccess)
        {
            currentSession = new NX10SDKSession();
            string apiKey = NX10RuntimeConfig.ApiKey;

            if (apiKey == string.Empty)
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
                sdkVersion = "1.0.0",
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

            StartCoroutine(NX10PostRequest("https://control-plane.affectstack.com/routes/sessions/start", startSessionJson, (success, message) =>
            {
                if (success)
                {
                    HandleSessionStartSuccess(message);
                }

                sessionStartSuccess?.Invoke(success);
            }));
        }

        public void HandleSessionStartSuccess(string sessionStartJson)
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

        public void SendSaaqData(string feeling, int ranking, string feelingModalType, string feelingContext, string feelingFor, string promptDisplayTimestamp, string prompAnswerTimestamp)
        {
            string timeStamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            string saaqEndpoint = currentSession.GetEndpoint("saaq", "v1");
            NX10SAAQPayload saaqResponse = new NX10SAAQPayload()
            {
                deviceSendTimestamp = timeStamp,
                promptDisplayTimestamp = promptDisplayTimestamp,
                promptAnswerTimestamp = prompAnswerTimestamp,
                feeling = feeling,
                saaqType = feelingModalType,
                feelingContext = feelingContext,
                feelingFor = feelingFor,
                metaData = new Dictionary<string, object>(currentGameMetaData)
            };

            string nx10jsonData = JsonConvert.SerializeObject(saaqResponse);
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
                Debug.Log("Post Successful");
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

