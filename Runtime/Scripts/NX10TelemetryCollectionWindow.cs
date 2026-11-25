using System;
using System.Collections.Generic;
using System.Globalization;

namespace NX10
{
    public class NX10TelemetryCollectionWindow : IDisposable
    {
        public DateTime startTimestamp;
        public double startMonoMs;

        public string startTimestampISO => startTimestamp
                    .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

        public List<IInputEvent> inputEvents = new List<IInputEvent>();

        public NX10TelemetryCollectionWindow()
        {
            startMonoMs = NativeSensorTime.NowMs();
        }

        public NX10TelemetryCollectionWindow(double startMonotonicMs)
        {
            startMonoMs = startMonotonicMs;
        }

        public TimeSpan Offset()
        {
            return DateTime.UtcNow - startTimestamp;
        }

        public double MonotonicOffsetMs(double sampleMonoMs)
        {
            double delta = sampleMonoMs - startMonoMs;
            return delta > 0.0 ? delta : 0.0;
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
