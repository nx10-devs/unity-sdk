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

        public Action<SAAQBlock> OnPromptRequested;

        public void SendAttributes(Dictionary<string, object> currentGameAttributes)
        {
            List<HeaderObject> headers = new List<HeaderObject>()
            {
                new HeaderObject("Authorization", "Bearer " + currentSession.Token)
            };

            string attributesEndPoint = currentSession.GetEndpoint("attributes", "v1");
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            NX10AttributesPayload attributesPayload = new NX10AttributesPayload()
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
            SAAQResponse request = JsonUtility.FromJson<SAAQResponse>(json);

            if (request.status == "success" && request.HasPrompt)
            {
                Debug.Log($"Trigger Received: {request.data.prompt.questionText}");
                //OnPromptRequested.Invoke(request.data.prompt);
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

