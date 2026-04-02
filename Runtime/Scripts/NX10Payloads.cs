using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace NX10
{
    [Serializable]
    public class NX10SessionStartPayload
    {
        public string apiKey;
        public UserIdentifiers identifiers;
        public SDKData sdkProvided;
        public AppProvidedData appProvided;
    }

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
    public class NX10AttributesPayload
    {
        public string timestamp;
        public Dictionary<string, object> data;
    }
}
