using NX10;
using System;
using System.Collections.Generic;
using UnityEngine;

public class NX10TelemetryWindow
{
    public DateTime startTimestamp;
    public string startTimestampISO => startTimestamp.ToString(("yyyy-MM-ddTHH:mm:ss.fffZ"));

    public List<IInputEvent> inputEvents = new List<IInputEvent>();

    public TimeSpan Offset()
    {
        return DateTime.UtcNow - startTimestamp;
    }

    public void EndWindow()
    {
        inputEvents.Clear();
    }
}
