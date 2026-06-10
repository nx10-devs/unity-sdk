using System;
using System.Collections.Generic;

namespace NX10
{
    [Serializable]
    public class SessionStartResponse
    {
        public SessionStartData data;
    }

    [Serializable]
    public class SessionStartData
    {
        public string token;
        public DeviceConfig deviceConfig;
        public List<EndpointInfo> endpoints;
    }

    [Serializable]
    public class EndpointInfo
    {
        public string location;
        public string type;
        public string version;
    }

    [Serializable]
    public class DeviceConfig
    {
        public SaaqConfig saaq;
        public Sensors sensor;
        public DeviceSettings device;
        public ActivtyThresholds activity;
    }

    [Serializable]
    public class SaaqConfig
    {
        public float? saaqPollingPeriodSeconds;
    }

    [Serializable]
    public class Sensors
    {
        public int? touchSampleHz;
        public int? gyroscopeSampleHz;
        public int? accelerometerSampleHz;
        public int? acquisitionWindowSize;
    }

    [Serializable]
    public class DeviceSettings
    {
        public Dictionary<string, float> deviceModelToDpiMap;
    }

    [Serializable]
    public class ActivtyThresholds
    {
        public Dictionary<string, float> thresholds;
    }
}
