using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace NX10
{
    public class NX10TelemetryInferenceWindow : IDisposable
    {
        public const double windowDurationMs = 10000.0;

        private interface ITimestampedSample
        {
            double TimestampMs { get; }
        }

        public struct SensorSample : ITimestampedSample
        {
            public double timestampMs;
            public float x;
            public float y;
            public float z;

            public double TimestampMs => timestampMs;
        }

        public struct TouchSample : ITimestampedSample
        {
            public double timestampMs;
            public float index;
            public float touchType;
            public float x;
            public float y;
            public float radius;

            public double TimestampMs => timestampMs;
        }

        public DateTime startTimestamp = DateTime.UtcNow;
        public double startMonoMs;

        public string startTimestampISO => startTimestamp
                    .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

        public double ElapsedMs => Math.Max(0.0, NativeSensorTime.NowMs() - startMonoMs);

        public List<SensorSample> accelSamples = new List<SensorSample>();
        public List<SensorSample> gyroSamples = new List<SensorSample>();
        public List<SensorSample> magSamples = new List<SensorSample>();
        public List<TouchSample> touchSamples = new List<TouchSample>();

        public Dictionary<string, int> eventCounts = new Dictionary<string, int>();
        public Dictionary<string, int> attributeCounts = new Dictionary<string, int>();

        public int eventAddCount;
        public DateTime? timeOfLastInterstitial = null;

        public NX10TelemetryInferenceWindow()
        {
            startMonoMs = NativeSensorTime.NowMs();
        }

        public NX10TelemetryInferenceWindow(double startMonotonicMs)
        {
            startMonoMs = startMonotonicMs;
        }

        private double ToWindowMs(double sampleMonoMs)
        {
            double delta = sampleMonoMs - startMonoMs;
            return delta > 0.0 ? delta : 0.0;
        }

        public void RecordAccel(Vector3 acceleration)
        {
            RecordAccel(acceleration, NativeSensorTime.NowMs());
        }

        public void RecordAccel(Vector3 acceleration, double sampleMonoMs)
        {
            accelSamples.Add(new SensorSample
            {
                timestampMs = ToWindowMs(sampleMonoMs),
                x = acceleration.x,
                y = acceleration.y,
                z = acceleration.z
            });
        }

        public void RecordGyro(Vector3 rotationRate)
        {
            RecordGyro(rotationRate, NativeSensorTime.NowMs());
        }

        public void RecordGyro(Vector3 rotationRate, double sampleMonoMs)
        {
            gyroSamples.Add(new SensorSample
            {
                timestampMs = ToWindowMs(sampleMonoMs),
                x = rotationRate.x,
                y = rotationRate.y,
                z = rotationRate.z
            });
        }

        public void RecordMag(Vector3 rawMag)
        {
            RecordMag(rawMag, NativeSensorTime.NowMs());
        }

        public void RecordMag(Vector3 rawMag, double sampleMonoMs)
        {
            magSamples.Add(new SensorSample
            {
                timestampMs = ToWindowMs(sampleMonoMs),
                x = rawMag.x,
                y = rawMag.y,
                z = rawMag.z
            });
        }

        public void RecordTouches(IList<NativeTouch.TouchObject> touches)
        {
            if (touches == null || touches.Count == 0)
                return;

            double sampleMonoMs = NativeSensorTime.NowMs();
            for (int i = 0; i < touches.Count; i++)
            {
                RecordTouchSample(touches[i], i, sampleMonoMs);
            }
        }

        public void RecordTouchSample(NativeTouch.TouchObject touch, int touchIndex, double sampleMonoMs)
        {
            touchSamples.Add(new TouchSample
            {
                timestampMs = ToWindowMs(sampleMonoMs),
                index = touchIndex,
                touchType = touch.touchTypeFloat,
                x = (float)touch.x,
                y = (float)touch.y,
                radius = (float)touch.touchRadius
            });
        }

        public void RecordEvent(string eventName, Dictionary<string, object> eventData)
        {
            if (string.IsNullOrEmpty(eventName)) return;

            if (eventCounts.TryGetValue(eventName, out int count))
            {
                eventCounts[eventName] = count + 1;
            }
            else
            {
                eventCounts[eventName] = 1;
            }

            if (eventName == NX10SchemaKeys.Events.AdStarted)
            {
                if (eventData != null && eventData.TryGetValue(NX10SchemaKeys.Metadata.Type, out object type) && type != null)
                {
                    if (string.Equals(type.ToString(), NX10SchemaKeys.AdTypes.Interstitial, StringComparison.OrdinalIgnoreCase))
                    {
                        timeOfLastInterstitial = DateTime.UtcNow;
                    }
                }
            }

            eventAddCount++;
        }

        public void RecordAttribute(string attributeKey)
        {
            if (string.IsNullOrEmpty(attributeKey)) return;

            if (attributeCounts.TryGetValue(attributeKey, out int count))
            {
                attributeCounts[attributeKey] = count + 1;
            }
            else
            {
                attributeCounts[attributeKey] = 1;
            }
        }

        public void RecordAttributes(IEnumerable<string> attributeKeys)
        {
            if (attributeKeys == null) return;

            foreach (string key in attributeKeys)
            {
                RecordAttribute(key);
            }
        }

        public TimeSpan Offset()
        {
            return TimeSpan.FromMilliseconds(ElapsedMs);
        }

        public double PruneAndCaptureTimestampMs()
        {
            double nowMs = ElapsedMs;
            PruneOldEvents(nowMs);
            return nowMs;
        }

        public void PruneOldEvents()
        {
            PruneOldEvents(ElapsedMs);
        }

        public void PruneOldEvents(double nowMs)
        {
            double cutoffMs = nowMs - windowDurationMs;
            if (cutoffMs <= 0.0)
                return;

            PruneSamples(accelSamples, cutoffMs);
            PruneSamples(gyroSamples, cutoffMs);
            PruneSamples(magSamples, cutoffMs);
            PruneSamples(touchSamples, cutoffMs);
        }

        public float[] BuildAccelTensorData(double nowMs)
        {
            return BuildSensorTensorData(accelSamples, nowMs);
        }

        public float[] BuildGyroTensorData(double nowMs)
        {
            return BuildSensorTensorData(gyroSamples, nowMs);
        }

        public float[] BuildMagTensorData(double nowMs)
        {
            return BuildSensorTensorData(magSamples, nowMs);
        }

        public float[] BuildTouchTensorData(double nowMs)
        {
            PruneSamples(touchSamples, nowMs - windowDurationMs);

            if (touchSamples.Count > 0)
            {
                double windowStartMs = Math.Max(0.0, nowMs - windowDurationMs);
                float[] flat = new float[touchSamples.Count * 6 + 1];

                double prevTimestampMs = windowStartMs;
                for (int i = 0; i < touchSamples.Count; i++)
                {
                    TouchSample sample = touchSamples[i];
                    int baseIdx = i * 6;
                    flat[baseIdx] = RoundMs(sample.timestampMs - prevTimestampMs);
                    flat[baseIdx + 1] = sample.index;
                    flat[baseIdx + 2] = sample.touchType;
                    flat[baseIdx + 3] = sample.x;
                    flat[baseIdx + 4] = sample.y;
                    flat[baseIdx + 5] = sample.radius;
                    prevTimestampMs = sample.timestampMs;
                }

                flat[flat.Length - 1] = RoundMs(nowMs - prevTimestampMs);
                return flat;
            }
            else
            {
                float[] flat = new float[7];
                flat[6] = RoundMs(Math.Min(nowMs, windowDurationMs));
                return flat;
            }
        }

        private static float[] BuildSensorTensorData(List<SensorSample> samples, double nowMs)
        {
            PruneSamples(samples, nowMs - windowDurationMs);

            if (samples.Count > 0)
            {
                double windowStartMs = Math.Max(0.0, nowMs - windowDurationMs);
                float[] flat = new float[samples.Count * 4 + 1];

                double prevTimestampMs = windowStartMs;
                for (int i = 0; i < samples.Count; i++)
                {
                    SensorSample sample = samples[i];
                    int baseIdx = i * 4;
                    flat[baseIdx] = RoundMs(sample.timestampMs - prevTimestampMs);
                    flat[baseIdx + 1] = sample.x;
                    flat[baseIdx + 2] = sample.y;
                    flat[baseIdx + 3] = sample.z;
                    prevTimestampMs = sample.timestampMs;
                }

                flat[flat.Length - 1] = RoundMs(nowMs - prevTimestampMs);
                return flat;
            }
            else
            {
                float[] flat = new float[5];
                flat[4] = RoundMs(Math.Min(nowMs, windowDurationMs));
                return flat;
            }
        }

        private static void PruneSamples<T>(List<T> samples, double cutoffMs) where T : struct, ITimestampedSample
        {
            if (cutoffMs <= 0.0 || samples == null || samples.Count == 0)
                return;

            int removeCount = 0;
            while (removeCount < samples.Count && samples[removeCount].TimestampMs < cutoffMs)
            {
                removeCount++;
            }

            if (removeCount > 0)
            {
                samples.RemoveRange(0, removeCount);
            }
        }

        private static float RoundMs(double ms)
        {
            if (ms <= 0.0)
                return 0f;

            return (float)Math.Round(ms, 3, MidpointRounding.AwayFromZero);
        }

        public void Dispose()
        {
            EndWindow();
        }

        private void EndWindow()
        {
            accelSamples.Clear();
            gyroSamples.Clear();
            magSamples.Clear();
            touchSamples.Clear();
            eventCounts.Clear();
            attributeCounts.Clear();
        }
    }
}
