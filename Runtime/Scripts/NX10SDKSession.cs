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

        public float dpi;

        public void Initialize(SessionStartData data)
        {
            Token = data.token;
            _endpoints = data.endpoints ?? new List<EndpointInfo>();

            gyroFrequencyHz = data.deviceConfig.sensor.gyroscopeSampleHz;
            accelFrequencyHz = data.deviceConfig.sensor.accelerometerSampleHz;
            touchFrequencyHz = data.deviceConfig.sensor.touchSampleHz;
            acquisitionWindowSize = data.deviceConfig.sensor.acquisitionWindowSize;

            string deviceModel = SystemInfo.deviceModel;
            if(data.deviceConfig.device.deviceModelToDpiMap.TryGetValue(deviceModel, out float value))
            {
                dpi = value;
            }
            else
            {
                dpi = Screen.dpi / 25.4f;
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
