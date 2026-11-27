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
        private List<ModelDefinition> _ad_propensity_models = new List<ModelDefinition>();
        private Dictionary<string, float> _metrics = new Dictionary<string, float>();

        public int? gyroFrequencyHz { get; private set; }
        public int? accelFrequencyHz { get; private set; }
        public int? touchFrequencyHz { get; private set; }
        public int? magnetometerFrequencyHz { get; private set; }
        public float? screenBrightnessDelta { get; private set; }
        public int? acquisitionWindowSize { get; private set; }

        public float? saaqPollingPeriod { get; private set; }
        public bool? telemetryCompression {  get; private set; }

        public float dpi;

        private const string stationaryThresholdKey = "stationaryMaxThreshold";
        private const string movingMinThresholdKey = "movingMinThreshold";

        public float? stationaryThreshold { get; private set; }
        public float? movingMinThreshold { get; private set; }

        public void Initialize(SessionStartData data)
        {
            if (data == null)
            {
                return;
            }

            Token = data.token;

            _endpoints = data.endpoints ?? new List<EndpointInfo>();
            _ad_propensity_models = data.models?.adPropensity ?? new List<ModelDefinition>();

            if (data.deviceConfig != null)
            {
                if (data.deviceConfig.sensor != null)
                {
                    gyroFrequencyHz = data.deviceConfig.sensor.gyroscopeSampleHz;
                    accelFrequencyHz = data.deviceConfig.sensor.accelerometerSampleHz;
                    touchFrequencyHz = data.deviceConfig.sensor.touchSampleHz;
                    acquisitionWindowSize = data.deviceConfig.sensor.acquisitionWindowSize;
                    magnetometerFrequencyHz = data.deviceConfig.sensor.magnetometerSampleHz;
                    screenBrightnessDelta = data.deviceConfig.sensor?.screenBrightnessDelta;
                    telemetryCompression = data.deviceConfig.sensor.compressTelemetry;
                }

                if (data.deviceConfig.saaq != null)
                {
                    saaqPollingPeriod = data.deviceConfig.saaq.saaqPollingPeriodSeconds;
                }

                string deviceModel = SystemInfo.deviceModel;
                if (data.deviceConfig.device?.deviceModelToDpiMap != null &&
                    data.deviceConfig.device.deviceModelToDpiMap.TryGetValue(deviceModel, out float value))
                {
                    dpi = value;
                }
                else
                {
                    dpi = Screen.dpi;
                }

                if (data.deviceConfig.activity?.thresholds != null)
                {
                    if (data.deviceConfig.activity.thresholds.TryGetValue(stationaryThresholdKey, out float stationaryThreshold))
                    {
                        this.stationaryThreshold = stationaryThreshold;
                    }

                    if (data.deviceConfig.activity.thresholds.TryGetValue(movingMinThresholdKey, out float movingMinThreshold))
                    {
                        this.movingMinThreshold = movingMinThreshold;
                    }
                }
            }
            else
            {
                dpi = Screen.dpi;
            }

            if (data.metrics != null)
            {
                UpdateMetrics(data.metrics);
            }
        }

        /// <summary>
        /// Replaces the session's cloud macro metrics when a valid refresh dictionary is received.
        /// Retains existing metrics untouched if <paramref name="metrics"/> is null.
        /// </summary>
        public bool UpdateMetrics(Dictionary<string, float> metrics)
        {
            if (metrics == null)
            {
                return false;
            }

            var sanitized = new Dictionary<string, float>(metrics.Count);
            foreach (var metric in metrics)
            {
                if (!string.IsNullOrEmpty(metric.Key) &&
                    !float.IsNaN(metric.Value) &&
                    !float.IsInfinity(metric.Value))
                {
                    sanitized[metric.Key] = metric.Value;
                    Debug.Log($"Metric: {metric.Key} = {metric.Value}");
                }
            }

            _metrics = sanitized;
            return true;
        }

        public float GetMetric(string metricName)
        {
            if(_metrics.TryGetValue(metricName, out var metric))
            {
                return metric;
            }

            Debug.LogWarning("No metic of name " + metricName + ". returning 0");
            return 0f;
        }

        public string GetEndpoint(string type, string version)
        {
            return _endpoints.FindAll(e => e.type == type)?.FirstOrDefault(e => e.version == version)?.location;
        }

        public string GetAdPropensityUrl(string name = null, string version = null)
        {
            return GetModel(name, version)?.downloadUrl;
        }

        public float GetAdPropensityThreshold(string name = null, string version = null, float defaultThreshold = 0f)
        {
            return GetModel(name, version)?.modelThreshold ?? defaultThreshold;
        }

        public ModelDefinition GetAdPropensityModel(string name = null, string version = null)
        {
            return GetModel(name, version);
        }

        private ModelDefinition GetModel(string name = null, string version = null)
        {
            if (_ad_propensity_models == null || _ad_propensity_models.Count == 0)
            {
                Debug.LogWarning("[NX10] No adPropensity models available in session.");
                return null;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                ModelDefinition firstModel = _ad_propensity_models.FirstOrDefault(
                    e => e != null && !string.IsNullOrWhiteSpace(e.downloadUrl));

                if (firstModel == null)
                {
                    Debug.LogWarning("[NX10] No valid adPropensity model with a downloadUrl found in session.");
                }

                return firstModel;
            }

            ModelDefinition model = _ad_propensity_models.FirstOrDefault(
                e => e != null &&
                     e.name == name &&
                     (string.IsNullOrWhiteSpace(version) || e.version == version));

            if (model == null)
            {
                Debug.LogWarning($"[NX10] Model '{name}' (version '{version ?? "any"}') not found in session.");
            }

            return model;
        }

        public void Clear()
        {
            Token = null;
            _endpoints.Clear();
            _ad_propensity_models.Clear();
            _metrics.Clear();
        }
    }
}
