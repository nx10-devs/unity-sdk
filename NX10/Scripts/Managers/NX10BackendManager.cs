using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.Networking;
using static NX10.NX10BackendManager;
using Newtonsoft.Json;
//using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
//using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace NX10
{
    public class NX10BackendManager : MonoBehaviour
    {
        public string nX10ApiKey;

        public float apiIntervalSeconds;
        private float timer = 0.0f;

        public string partner = "nx10";
        public string source = "3MATCH_GAME";

        [Header("Telemetry")]
        public string telemetryApiKey = "";
        public string telemetryEndpointOverride = "";

        private string deviceName;
        private string deviceModel;
        private string deviceOsVersion;
        private string deviceId;
        private string appVersion;
        private string buildId;

        [System.Serializable]
        public class GyroDataPacket
        {
            public float x;
            public float y;
            public float z;
            public long timestamp;
        }

        [System.Serializable]
        public class AccelerometerDataPacket
        {
            public float x;
            public float y;
            public float z;
            public long timestamp;
        }

        [System.Serializable]
        public class TouchEvent
        {
            public float x;
            public float y;
            public float velocityX;
            public float velocityY;
            public long timestamp;
        }
        [System.Serializable]
        public class NX10VectorSamplePacket
        {
            public float x;
            public float y;
            public float z;
            public string timestamp;
        }

        [System.Serializable]
        public class Nx10TouchEvent
        {
            public float x;
            public float y;
            public float velocityX;
            public float velocityY;
            public string timestamp;
        }

        [System.Serializable]
        public class NX10TelemetryData
        {
            public string deviceSendTimestamp;
            public NX10VectorSamplePacket[] gyro;
            public NX10VectorSamplePacket[] acc;
            public Nx10TouchEvent[] touch;
            public NX10SerializableDictionary metaData;
        }

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

        [System.Serializable]
        public class DeviceType
        {
            public string osType;
            public string osVersion;
            public string deviceType;
            public string appVersion;
            public string buildVersionconsole;
        }

        [System.Serializable]
        public class DataPacket
        {
            public string deviceName;
            public string deviceToken; // device id?
            public string partner;
            public string source;
            public bool emulator = false;
            public string appVersion;
            public string appBuild;
            public long timestamp;
            public GyroDataPacket[] gyro;
            public AccelerometerDataPacket[] accelerometer;
            public TouchEvent[] touch;
            public DeviceType deviceType;
            public object telemetryData = new object { };
            public SAAQResponse[] modal;
        }

        [System.Serializable]
        public class SAAQResponse
        {
            public string feeling;
            public int? ranking;
            public long timestamp;
            public string feelingModalType;
            public string feelingContext;
            public string feelingFor;
        }

        [System.Serializable]
        public class NX10SAAQResponse
        {
            public string deviceSendTimestamp;
            public string feeling;
            public string saaqType;
            public string feelingContext;
            public string feelingFor;
            public NX10SerializableDictionary metaData;
        }

        [Serializable]
        public class GameDataPayload
        {
            public string deviceToken;
            public string partner;
            public string source;
            public long timestamp;
            public string userId;
            public string deviceName;
            public bool emulator;
            public string appVersion;
            public string appBuild;
            public TelemetryData telemetryData;
            public string gameData;
            public DeviceType deviceType;
        }

        [Serializable]
        public class TelemetryData
        {
            public string gameData; // Session data as JSON string
        }

        [HideInInspector]
        public List<TouchEvent> touchEvents = new List<TouchEvent>();
        [HideInInspector]
        public List<GyroDataPacket> gyroData = new List<GyroDataPacket>();
        [HideInInspector]
        public List<AccelerometerDataPacket> accelerometerData = new List<AccelerometerDataPacket>();

        [HideInInspector]
        public List<Nx10TouchEvent> nx10TouchEvents = new List<Nx10TouchEvent>();
        [HideInInspector]
        public List<NX10VectorSamplePacket> nx10GyroData = new List<NX10VectorSamplePacket>();
        [HideInInspector]
        public List<NX10VectorSamplePacket> nx10AccelerometerData = new List<NX10VectorSamplePacket>();

        private bool canCollectTelemetryData;
        private Dictionary<string, string> currentGameMetaData = new Dictionary<string, string>();

        private void Awake()
        {
            EnhancedTouchSupport.Enable();

            if(SystemInfo.supportsGyroscope)
                Input.gyro.enabled = true;
        }

        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
            deviceName = SystemInfo.deviceName;
            deviceModel = SystemInfo.deviceModel;
            deviceOsVersion = SystemInfo.operatingSystem;
            deviceId = SystemInfo.deviceUniqueIdentifier;
            appVersion = UnityEngine.Application.version;
            buildId = UnityEngine.Application.buildGUID;
            // nX10ApiKey = "rgS8CPbEHNaztIOoozy41IhoKIUH7FP2EVxDFoM0";

            StartSession();
        }

        // Update is called once per frame
        void Update()
        {
            UpdateTelemetryCollection();
        }

        public void UpdateNX10MetaData(string metaDataKey, string metaDataValue)
        {
            if(currentGameMetaData.ContainsKey(metaDataKey))
            {
                currentGameMetaData[metaDataKey] = metaDataValue;
            }
            else
            {
                currentGameMetaData.Add(metaDataKey, metaDataValue);
            }
        }

        public void SetTelemetryCollection(bool canCollect)
        {
            canCollectTelemetryData = canCollect;
        }

        private void UpdateTelemetryCollection()
        {
            if (!canCollectTelemetryData)
                return;

            timer += Time.deltaTime;
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            long timestampLong = (long)DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalMilliseconds;

            CollectTelemetryData(timestamp, timestampLong);

            if (timer > apiIntervalSeconds)
            {
                SendTelemetryData(timestamp, timestampLong);

                touchEvents.Clear();
                gyroData.Clear();
                accelerometerData.Clear();
            }
        }

        private void CollectTelemetryData(string timestamp, long timestampLong)
        {
            GyroDataPacket gyro = new GyroDataPacket();
            gyro.x = Input.gyro.rotationRate.x;
            gyro.y = Input.gyro.rotationRate.y;
            gyro.z = Input.gyro.rotationRate.z;
            gyro.timestamp = timestampLong;
            gyroData.Add(gyro);

            NX10VectorSamplePacket nX10Gyro = new NX10VectorSamplePacket();
            nX10Gyro.x = Input.gyro.rotationRate.x;
            nX10Gyro.y = Input.gyro.rotationRate.y;
            nX10Gyro.z = Input.gyro.rotationRate.z;
            nX10Gyro.timestamp = timestamp;
            nx10GyroData.Add(nX10Gyro);

            AccelerometerDataPacket accel = new AccelerometerDataPacket();
            accel.x = Input.gyro.userAcceleration.x;
            accel.y = Input.gyro.userAcceleration.y;
            accel.z = Input.gyro.userAcceleration.z;
            accel.timestamp = timestampLong;
            accelerometerData.Add(accel);

            NX10VectorSamplePacket nx10Accel = new NX10VectorSamplePacket();
            nx10Accel.x = Input.gyro.userAcceleration.x;
            nx10Accel.y = Input.gyro.userAcceleration.y;
            nx10Accel.z = Input.gyro.userAcceleration.z;
            nx10Accel.timestamp = timestamp;
            nx10AccelerometerData.Add(nx10Accel);

            foreach (var touch in Input.touches)
            {
                TouchEvent mTouch = new TouchEvent();
                mTouch.x = touch.position.x;
                mTouch.y = touch.position.y;
                mTouch.velocityX = touch.deltaPosition.x / Time.deltaTime;
                mTouch.velocityY = touch.deltaPosition.y / Time.deltaTime;
                mTouch.timestamp = timestampLong;
                touchEvents.Add(mTouch);

                Nx10TouchEvent nx10Touch = new Nx10TouchEvent();
                nx10Touch.x = touch.position.x;
                nx10Touch.y = touch.position.y;
                nx10Touch.velocityX = touch.deltaPosition.x / Time.deltaTime;
                nx10Touch.velocityY = touch.deltaPosition.y / Time.deltaTime;
                nx10Touch.timestamp = timestamp;
                nx10TouchEvents.Add(nx10Touch);
            }
        }

        private void SendTelemetryData(string timestamp, long timestampLong)
        {
            // According to the docs, subtracting the time as below is more reliable than resetting to 0 across longer stretches of time.
            timer = timer - apiIntervalSeconds;
            DataPacket apiData = new DataPacket()
            {
                deviceName = deviceName,
                deviceToken = deviceId,
                appVersion = appVersion,
                appBuild = buildId,
                partner = partner,
                source = source,
                gyro = gyroData.ToArray(),
                touch = touchEvents.ToArray(),
                accelerometer = accelerometerData.ToArray(),
                timestamp = timestampLong,
                deviceType = new DeviceType()
                {
                    appVersion = appVersion,
                    buildVersionconsole = buildId,
                    deviceType = deviceModel,
                    osType = "iOS",
                    osVersion = deviceOsVersion,
                },
                modal = null
            };

            string jsonData = JsonConvert.SerializeObject(apiData);
            Debug.Log($"JSON DATA: {jsonData}");

            StartCoroutine(PostRequest("https://ui88h87fs3.execute-api.eu-west-2.amazonaws.com/prod/telemetry", jsonData, (success, message) =>
            {
                if (success)
                {
                    Debug.Log("FatFish Success");
                }
                else
                {

                }
            }));

            string telemetryEndPoint = currentSession.GetEndpoint("telemetry");
            NX10TelemetryData telemetryData = new NX10TelemetryData()
            {
                deviceSendTimestamp = timestamp,
                gyro = nx10GyroData.ToArray(),
                acc = nx10AccelerometerData.ToArray(),
                touch = nx10TouchEvents.ToArray(),
                metaData = new NX10SerializableDictionary(currentGameMetaData),
            };

            string nx10jsonData = NX10CustomTelemetrySerializer.Serialize(telemetryData);

            List<HeaderObject> headers = new List<HeaderObject>()
            {
                new HeaderObject("Authorization", "Bearer " + currentSession.Token)
            };

            StartCoroutine(NX10PostRequest(telemetryEndPoint, nx10jsonData, (success, message) =>
            {
                if (success)
                {

                }
                else
                {

                }
            }, headers));
        }

        public class JsonRes
        {
            public string userId;
            public string id;
            public string title;
            public Boolean completed;

        }

        public void SetOverrideDeviceId(string overrideDeviceId)
        {
            deviceId = overrideDeviceId;
        }

        [Serializable]
        public class SessionStartPacket
        {
            public string apiKey;
            public Identifiers identifiers;
            public SDKData sdkProvided;
            public AppProvidedData appProvided;
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
            public NX10SerializableDictionary metaData;
            public string applicationVersion;
            public string buildNumber;
        }

        [Serializable]
        public class SessionStartResponse
        {
            public string status;
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

        private NX10SDKSession currentSession;

        private string GetOSName()
        {
#if UNITY_IOS
        return "iOS";
#elif UNITY_ANDROID
            return "Android";
#else
        return "Unknown";
#endif
        }

        public void StartSession()
        {
            currentSession = new NX10SDKSession();
            string apiKey = nX10ApiKey;

            Identifiers identifiers = new Identifiers
            {
                deviceId = deviceId
            };

            DeviceInfo deviceInfo = new DeviceInfo
            {
                type = "mobile",
                os = GetOSName(),
                osVersion = SystemInfo.operatingSystem,
                deviceVersion = SystemInfo.deviceModel,
                deviceVariant = SystemInfo.deviceName
            };

            SDKData sdkData = new SDKData
            {
                device = deviceInfo,
                sdkVersion = "1.0.0"
            };

            AppProvidedData appProvided = new AppProvidedData
            {
                metaData = new NX10SerializableDictionary(currentGameMetaData),
                applicationVersion = appVersion,
                buildNumber = buildId
            };

            SessionStartPacket sessionStartPacket = new SessionStartPacket
            {
                apiKey = apiKey,
                identifiers = identifiers,  
                sdkProvided = sdkData,
                appProvided = appProvided
            };

            string startSessionJson = NX10CustomStartSessionSerializer.Serialize(sessionStartPacket);

            StartCoroutine(NX10PostRequest("https://control-plane.affectstack-stage.com/routes/sessions/start", startSessionJson, (success, message) =>
            {
                if(success)
                    HandleSessionStartSuccess(message);
            }));

            Debug.Log(startSessionJson);
        }

        public void HandleSessionStartSuccess(string sessionStartJson)
        {
            SessionStartResponse response = JsonUtility.FromJson<SessionStartResponse>(sessionStartJson);

            if (response == null)
            {
                Debug.LogError("SessionInitResponse deserialized to null.");
                return;
            }

            if (response.status != "success")
            {
                Debug.LogError($"Session failed with status: {response.status}");
                return;
            }

            if (response.data == null || string.IsNullOrEmpty(response.data.token))
            {
                Debug.LogError("Invalid session response data.");
                return;
            }

            currentSession.Initialize(response.data);

            Debug.Log("Session initialized successfully.");
            Debug.Log("Token: " + currentSession.Token);
        }

        IEnumerator GetRequest(string uri)
        {
            using (UnityWebRequest webRequest = UnityWebRequest.Get(uri))
            {
                // Request and wait for the desired page.
                yield return webRequest.SendWebRequest();

                string[] pages = uri.Split('/');
                int page = pages.Length - 1;

                switch (webRequest.result)
                {
                    case UnityWebRequest.Result.ConnectionError:
                    case UnityWebRequest.Result.DataProcessingError:
                        Debug.LogError(pages[page] + ": Error: " + webRequest.error);
                        break;
                    case UnityWebRequest.Result.ProtocolError:
                        Debug.LogError(pages[page] + ": HTTP Error: " + webRequest.error);
                        break;
                    case UnityWebRequest.Result.Success:
                        string jsonVal = webRequest.downloadHandler.text;
                        JsonRes data = JsonUtility.FromJson<JsonRes>(jsonVal);
                        Debug.Log("User Id: " + data.title);
                        // OnAPIGetResponseEvent.Invoke();
                        // OnAPIGetResponseAction.Invoke();
                        break;
                }
            }
        }

        public class JsonBody
        {
            public string title;
            public string body;
            public int userId;
        }

        public void LogCurrentLevelData()
        {

        }

        public void LogAllLevelStatistics()
        {

        }

        // Call this at the start of each game
        public void OnGameStart()
        {
            LogCurrentLevelData();
        }

        // Call this at the end of each game (win or lose)
        public void OnGameEnd(bool won)
        {
            Debug.Log("On Game End Called");

            if (won)
            {
                LogCurrentLevelData();
            }
            else
            {
                LogCurrentLevelData();
            }

            // Log overall statistics
            LogAllLevelStatistics();
        }


        public IEnumerator PostRequest(string uri, string jsonBody, System.Action<bool, string> onComplete = null, string apiKey = null)
        {
/*#if UNITY_EDITOR
            onComplete?.Invoke(true, "");
            yield break;
#endif*/

            if (string.IsNullOrEmpty(jsonBody))
            {
                Debug.LogError("PostRequest: JSON body cannot be null or empty");
                onComplete?.Invoke(false, "JSON body is null or empty");
                yield break;
            }

            //var msg = new SlackMessage(jsonBody);
            //StartCoroutine(SlackWebhook.IPost(msg));
            string fatFishApiKey = "rgS8CPbEHNaztIOoozy41IhoKIUH7FP2EVxDFoM0";

            // Use provided API key or fall back to the nX10ApiKey field
            string keyToUse = fatFishApiKey;

            using (UnityWebRequest webRequest = UnityWebRequest.Post(uri, jsonBody, "application/json"))
            {
                // Add x-api-key header if API key is provided
                if (!string.IsNullOrEmpty(keyToUse))
                {
                    webRequest.SetRequestHeader("x-api-key", keyToUse);
                }
                // byte[] rawBody = Encoding.UTF8.GetBytes(jsonBody);
                // webRequest.uploadHandler = new UploadHandlerRaw(rawBody);

                // Request and wait for the desired page.
                yield return webRequest.SendWebRequest();

                string[] pages = uri.Split('/');
                int page = pages.Length - 1;
                bool success = false;
                string responseMessage = "";

                switch (webRequest.result)
                {
                    case UnityWebRequest.Result.ConnectionError:
                    case UnityWebRequest.Result.DataProcessingError:
                        responseMessage = $"Error: {webRequest.error}";
                        // Debug.LogError(pages[page] + ": " + responseMessage);
                        break;
                    case UnityWebRequest.Result.ProtocolError:
                        string errorResponse = webRequest.downloadHandler?.text ?? "No response body";
                        int responseCode = (int)webRequest.responseCode;
                        responseMessage = $"HTTP Error {responseCode}: {webRequest.error}. Response: {errorResponse}. APIKEY {keyToUse} Payload: {jsonBody}";
                        Debug.LogError($"{pages[page]}: HTTP {responseCode} - {webRequest.error}");
                        Debug.LogError($"{pages[page]}: Response Body: {errorResponse}");
                        break;
                    case UnityWebRequest.Result.Success:
                        success = true;
                        string jsonVal = webRequest.downloadHandler.text;
                        responseMessage = $"Success: {jsonVal}";
                        // Debug.Log(pages[page] + ": Request successful");
                        try
                        {
                            JsonRes data = JsonUtility.FromJson<JsonRes>(jsonVal);
                            // Debug.Log("Response Title: " + data.title);
                            // Debug.Log("Response Id: " + data.id);
                        }
                        catch
                        {
                            // Response might not match JsonRes format, just log the raw response
                            // Debug.Log("Response: " + jsonVal);
                        }
                        break;
                }

                // Call the callback with success status and response message
                onComplete?.Invoke(success, responseMessage);
            }
        }

        public IEnumerator NX10PostRequest(string uri, string jsonBody, System.Action<bool, string> onComplete = null, List<HeaderObject> additionalHeaders = null)
        {
/*#if DEBUG
            onComplete?.Invoke(true, "");
            yield break;
#endif*/

            UnityWebRequest request = new UnityWebRequest(uri, "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            if(additionalHeaders != null)
            {
                foreach(HeaderObject headerObject in additionalHeaders)
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

        public void SendSaaqPrompt(string feeling, int ranking, string feelingModalType, string feelingContext, string feelingFor)
        {
            long timestampLong = (long)DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalMilliseconds;
            SAAQResponse _saaq = new SAAQResponse()
            {
                feeling = feeling,
                ranking = null,
                timestamp = timestampLong,
                feelingModalType = feelingModalType,
                feelingContext = feelingContext,
                feelingFor = feelingFor
            };
            SAAQResponse[] modalArray = { _saaq };
            DataPacket apiData = new DataPacket()
            {
                deviceName = deviceName,
                deviceToken = deviceId,
                partner = partner,
                source = source,
                appVersion = appVersion,
                appBuild = buildId,
                timestamp = timestampLong,
                deviceType = new DeviceType()
                {
                    appVersion = appVersion,
                    buildVersionconsole = buildId,
                    deviceType = deviceModel,
                    osType = "iOS",
                    osVersion = deviceOsVersion,
                },
                modal = modalArray,
            };
            string jsonData = JsonConvert.SerializeObject(apiData);
            Debug.Log(jsonData);

            StartCoroutine(PostRequest("https://ui88h87fs3.execute-api.eu-west-2.amazonaws.com/prod/telemetry", jsonData));

            string timeStamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            string saaqEndpoint = currentSession.GetEndpoint("saaq");
            NX10SAAQResponse saaqResponse = new NX10SAAQResponse()
            {
                deviceSendTimestamp = timeStamp,
                feeling = feeling,
                saaqType = feelingModalType,
                feelingContext = feelingContext,
                feelingFor = feelingFor,
                metaData = new NX10SerializableDictionary(currentGameMetaData)
            };

            string nx10jsonData = NX10CustomSAAQSerializer.Serialize(saaqResponse);
            Debug.Log($"JSON DATA: {jsonData}");

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

        public void SendSessionDataToAPI(string sessionDataJson, string reason)
        {
#if DEBUG
            return;
#endif

            if (string.IsNullOrEmpty(sessionDataJson))
            {
                Debug.LogError($"[GameEventManager] Session data is empty");
                return;
            }

            string payloadJson = PrepareGameDataPayload(sessionDataJson, reason);
            if (!string.IsNullOrEmpty(payloadJson))
            {
                string apiEndpoint = string.IsNullOrEmpty(telemetryEndpointOverride)
                    ? "https://ui88h87fs3.execute-api.eu-west-2.amazonaws.com/prod/telemetry"
                    : telemetryEndpointOverride;


                StartCoroutine(PostRequest(apiEndpoint, payloadJson, (success, message) =>
                {
                    if (success)
                    {
                        Debug.Log($"[GameEventManager] API Request SUCCESS: {message}");
                    }
                    else
                    {
                        Debug.LogError($"[GameEventManager] API Request FAILED");
                        Debug.LogError($"[GameEventManager] Error Details: {message}");
                        Debug.LogError($"[GameEventManager] Request URL: {apiEndpoint}");
                        Debug.LogError($"[GameEventManager] Payload Size: {payloadJson?.Length ?? 0} bytes");
                    }
                }));
            }
        }

        /// <summary>
        /// Prepares a JSON payload with device information and game data for API submission
        /// </summary>
        /// <param name="gameDataJson">The game data JSON string to include in the payload</param>
        /// <returns>JSON string of the complete payload, or empty string if preparation fails</returns>
        private string PrepareGameDataPayload(string gameDataJson, string reason)
        {
            try
            {
                // Get device information
                string deviceToken = SystemInfo.deviceUniqueIdentifier;
                string deviceName = SystemInfo.deviceName; // e.g., "iPhone 5"
                string appVersion = Application.version;
                string appBuild = Application.buildGUID;

                // Detect if running on emulator
                // Common emulator detection: check if in editor or if device model contains emulator indicators
                bool isEmulator = false;
#if UNITY_EDITOR
                isEmulator = true;
#else
                // Check for common emulator device model patterns
                string model = SystemInfo.deviceModel.ToLower();
                isEmulator = model.Contains("emulator") || 
                            model.Contains("simulator") || 
                            model.Contains("sdk") ||
                            SystemInfo.deviceName.ToLower().Contains("emulator");
#endif

                // Get current timestamp (Unix timestamp in seconds)
                long timestamp = (long)DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalSeconds;

                // Get OS information for deviceType
                string osType = "unknown";
                string osVersion = SystemInfo.operatingSystem;
                string deviceTypeStr = deviceName;

#if UNITY_EDITOR
                osType = "Editor";
#elif UNITY_IOS
                osType = "iOS";
#elif UNITY_ANDROID
                osType = "Android";
#elif UNITY_STANDALONE_OSX
                osType = "macOS";
#elif UNITY_STANDALONE_WIN
                osType = "Windows";
#elif UNITY_STANDALONE_LINUX
                osType = "Linux";
#endif


                // Create telemetryData object
                var telemetryData = new TelemetryData
                {
                    gameData = gameDataJson
                };

                // Create deviceType object
                var deviceTypeObj = new DeviceType
                {
                    osType = osType,
                    osVersion = osVersion,
                    deviceType = deviceTypeStr
                };


                // Create payload object
                var payload = new GameDataPayload
                {
                    deviceToken = deviceToken,
                    partner = partner,
                    source = source,
                    timestamp = timestamp,
                    userId = deviceToken, // Using device unique ID as userId
                    deviceName = deviceName,
                    emulator = isEmulator,
                    appVersion = appVersion,
                    appBuild = appBuild,
                    telemetryData = telemetryData,
                    gameData = gameDataJson,
                    deviceType = deviceTypeObj
                };

                // Convert to JSON
                string jsonPayload = JsonUtility.ToJson(payload);

                Debug.Log($"[GameDataPayload] Prepared payload: {jsonPayload}");

                return jsonPayload;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameDataPayload] Error preparing payload: {ex.Message}");
                return string.Empty;
            }
        }
    }
}

