using System;
using System.Collections.Generic;
using UnityEngine;

namespace NX10
{
    public class NX10AnalyticsManager : MonoBehaviour
    {
        private const string sourceName = "unity-sdk";
        public event Action<string, string, Dictionary<string, object>> analyticsFired;

        public void FireEvent(string eventName, string overrideSourceName = "", Dictionary<string, object> metaData = null)
        {
            string source = sourceName;
            if(overrideSourceName != "")
                source = overrideSourceName;

            analyticsFired?.Invoke(eventName, source, metaData);
        }
    }
}
