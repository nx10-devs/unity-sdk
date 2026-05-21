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
        public NX10SDKSession CurrentSession => currentSession;

        public Action<SAAQData> OnPromptRequested;

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
                if (success)
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

        private void HandleIncomingSAAQ(string json)
        {
            SAAQResponse request = JsonConvert.DeserializeObject<SAAQResponse>(json);

            if (request.status == "success" && request.HasPrompt)
            {
                Debug.Log($"Trigger Received: {request.data.prompt.questionText}");
                OnPromptRequested.Invoke(request.data);
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
            string timeStamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            string saaqEndpoint = currentSession.GetEndpoint("saaq-triggered", "v1");
            NX10SAAQTriggeredPayload payload = new NX10SAAQTriggeredPayload()
            {
                triggerID = triggerId,
                answer = answer,
                deviceSendTimestamp = timeStamp,
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

        public void RequestActivity(Action<KineticState> activityAction)
        {
            string activityEndpoint = currentSession.GetEndpoint("activity", "v1");
            NX10ActivityPayload activityPayload = new NX10ActivityPayload()
            {
                stationaryMaxThreshold = CurrentSession.stationaryThreshold.Value,
                movingMinThreshold = CurrentSession.movingMinThreshold.Value,
            };

            List<HeaderObject> headers = new List<HeaderObject>()
            {
                new HeaderObject("Authorization", "Bearer " + currentSession.Token)
            };

            string nx10jsonData = JsonConvert.SerializeObject(activityPayload);
            StartCoroutine(NX10PostRequest(activityEndpoint, nx10jsonData, (success, message) =>
            {
                KineticState kineticState = KineticState.Unknown;
                if (success)
                {
                    kineticState = ParseActivityJson(message);
                }
               
                activityAction(kineticState);

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
                return KineticState.Unknown;
            }
        }

        public void RequestAffect(Action<Affect, float> affectAndConfidenceAction)
        {
            string affectEndpoint = currentSession.GetEndpoint("frustration", "v1");
            NX10ActivityPayload activityPayload = new NX10ActivityPayload()
            {
                stationaryMaxThreshold = CurrentSession.stationaryThreshold.Value,
                movingMinThreshold = CurrentSession.movingMinThreshold.Value,
            };

            List<HeaderObject> headers = new List<HeaderObject>()
            {
                new HeaderObject("Authorization", "Bearer " + currentSession.Token)
            };

            StartCoroutine(NX10GetRequest(affectEndpoint, (success, message) =>
            {
                Affect affect = Affect.Unknown;
                float confidence = 0;
                if (success)
                {
                    affect = ParseAffectJson(message, out float conf);
                    confidence = conf;
                }

                affectAndConfidenceAction(affect, confidence);

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
                return Affect.Unknown;
            }
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

