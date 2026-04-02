using System;
using System.Collections.Generic;
using UnityEngine;

namespace NX10
{
    public class NX10AnalyticsManager : MonoBehaviour
    {
        private const string sourceName = "unity-sdk";
        public event Action<NX10AnalyticsEvent> analyticsFired;

        public class NX10AnalyticsEvent
        {
            public NX10AnalyticsEvent(string eventName, string sourceName, Dictionary<string, object> data)
            {
                this.eventName = eventName;
                this.sourceName = sourceName;
                this.timeStamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                this.data = data;
            }

            public string eventName;
            public string sourceName;
            public string timeStamp;
            public Dictionary<string, object> data;
        }

        public void FireEvent(string eventName, string overrideSourceName = "", Dictionary<string, object> metaData = null)
        {
            string source = sourceName;
            if(overrideSourceName != "")
                source = overrideSourceName;

            NX10AnalyticsEvent analyticsEvent = new NX10AnalyticsEvent(eventName, source, metaData);

            analyticsFired?.Invoke(analyticsEvent);
        }
    }
}
