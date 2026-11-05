using NX10;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace NX10
{
    public class NX10TelemetryInferenceWindow : IDisposable
    {
        public DateTime startTimestamp;
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
