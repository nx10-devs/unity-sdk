using NX10;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace NX10
{
    public class NX10TelemetryCollectionWindow : IDisposable
    {
        public DateTime startTimestamp;
        public string startTimestampISO => startTimestamp
                    .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

        public List<IInputEvent> inputEvents = new List<IInputEvent>();

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
            inputEvents.Clear();
        }
    }

}
