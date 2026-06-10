using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NX10
{
    public class NX10SDKSession
    {
        public string Token { get; private set; }
        public IReadOnlyList<EndpointInfo> Endpoints => _endpoints;

        private List<EndpointInfo> _endpoints = new List<EndpointInfo>();

        public int? gyroFrequencyHz { get; private set; }
        public int? accelFrequencyHz { get; private set; }
        public int? touchFrequencyHz { get; private set; }

        public int? acquisitionWindowSize { get; private set; }

        public float? saaqPollingPeriod { get; private set; }

        public float dpi;

        private const string stationaryThresholdKey = "stationaryMaxThreshold";
        private const string movingMinThresholdKey = "movingMinThreshold";

        public float? stationaryThreshold { get; private set; }
        public float? movingMinThreshold { get; private set; }

        public void Initialize(SessionStartData data)
        {
            Token = data.token;

            Debug.Log(Token);

            _endpoints = data.endpoints ?? new List<EndpointInfo>();

            foreach(EndpointInfo ep in data.endpoints)
            {
                Debug.Log(ep.location);
            }

            if(data.deviceConfig.sensor != null)
            {
                gyroFrequencyHz = data.deviceConfig.sensor.gyroscopeSampleHz;
                accelFrequencyHz = data.deviceConfig.sensor.accelerometerSampleHz;
                touchFrequencyHz = data.deviceConfig.sensor.touchSampleHz;
                acquisitionWindowSize = data.deviceConfig.sensor.acquisitionWindowSize;
            }
           
            if(data.deviceConfig.saaq != null)
            {
                saaqPollingPeriod = data.deviceConfig.saaq.saaqPollingPeriodSeconds;
            }

            string deviceModel = SystemInfo.deviceModel;
            if(data.deviceConfig.device.deviceModelToDpiMap.TryGetValue(deviceModel, out float value))
            {
                dpi = value;
            }
            else
            {
                dpi = Screen.dpi;
            }

            if(data.deviceConfig.activity.thresholds.TryGetValue(stationaryThresholdKey, out float stationaryThreshold))
            {
                this.stationaryThreshold = stationaryThreshold;
            }

            if (data.deviceConfig.activity.thresholds.TryGetValue(movingMinThresholdKey, out float movingMinThreshold))
            {
                this.movingMinThreshold = movingMinThreshold;
            }

        }

        public string GetEndpoint(string type, string version)
        {
            return _endpoints.FindAll(e => e.type == type)?.FirstOrDefault(e => e.version == version)?.location;
        }

        public void Clear()
        {
            Token = null;
            _endpoints.Clear();
        }
    }
}
