using NX10;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace NX10
{
    public class NX10TelemetryInferenceWindow : IDisposable
    {
        public const float windowDurationMs = 10000f;

        public DateTime startTimestamp;
        public DateTime previousAccTimestamp;
        public DateTime previousGyroTimestamp;
        public DateTime previousMagTimestamp;
        public DateTime previousTouchTimestamp;

        public string startTimestampISO => startTimestamp
                    .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

        public List<float> accelEvents = new List<float>();
        public List<float> gyroEvents = new List<float>();
        public List<float> magEvents = new List<float>();
        public List<float> touchEvents = new List<float>();

        public Dictionary<string, int> eventCounts = new Dictionary<string, int>();
        public Dictionary<string, int> attributeCounts = new Dictionary<string, int>();

        public void RecordEvent(string eventName)
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
            return DateTime.UtcNow - startTimestamp;
        }

        public TimeSpan AccDelta()
        {
            return DateTime.UtcNow - previousAccTimestamp;
        }

        public TimeSpan GyroDelta()
        {
            return DateTime.UtcNow - previousGyroTimestamp;
        }

        public TimeSpan MagDelta()
        {
            return DateTime.UtcNow - previousMagTimestamp;
        }
        public TimeSpan TouchDelta()
        {
            return DateTime.UtcNow - previousTouchTimestamp;
        }

        public void PruneOldEvents()
        {
            float currentOffsetMs = (float)Offset().TotalMilliseconds;
            float cutoffMs = currentOffsetMs - windowDurationMs;

            if (cutoffMs <= 0f) return;

            PruneDeltaBuffer(accelEvents, 4);
            PruneDeltaBuffer(gyroEvents, 4);
            PruneDeltaBuffer(magEvents, 4);

            PruneDeltaBuffer(touchEvents, 6);
        }

        private void PruneDeltaBuffer(List<float> buffer, int stride)
        {
            if (buffer == null || buffer.Count < stride) return;

            float accumulatedTimeMs = 0f;
            int keepCount = 0;

            for (int i = buffer.Count - stride; i >= 0; i -= stride)
            {
                float delta = buffer[i];
                accumulatedTimeMs += delta;

                if (accumulatedTimeMs <= windowDurationMs)
                {
                    keepCount += stride;
                }
                else
                {
                    break;
                }
            }

            int removeCount = buffer.Count - keepCount;

            if (removeCount > 0)
            {
                buffer.RemoveRange(0, removeCount);
            }
        }

        public void Dispose()
        {
            EndWindow();
        }

        private void EndWindow()
        {
            accelEvents.Clear();
            gyroEvents.Clear();
            magEvents.Clear();
            touchEvents.Clear();
            eventCounts.Clear();
            attributeCounts.Clear();
        }
    }

}
