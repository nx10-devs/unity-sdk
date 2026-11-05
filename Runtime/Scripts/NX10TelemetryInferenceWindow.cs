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

            PruneBuffer(accelEvents, 4, cutoffMs);
            PruneBuffer(gyroEvents, 4, cutoffMs);
            PruneBuffer(magEvents, 4, cutoffMs);

            PruneBuffer(touchEvents, 6, cutoffMs);
        }

        private void PruneBuffer(List<float> buffer, int stride, float cutoffMs)
        {
            int removeCount = 0;
            for (int i = 0; i < buffer.Count; i += stride)
            {
                if (buffer[i] < cutoffMs)
                {
                    removeCount += stride;
                }
                else
                {
                    break; 
                }
            }

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
        }
    }

}
