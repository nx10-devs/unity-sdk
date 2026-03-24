using System;
using UnityEngine;

namespace NX10
{
    public class NX10AnalyticsManager : MonoBehaviour
    {
        private const string sourceName = "unity-sdk";
        public event Action<string, string> analyticsFired;

        public void FireEvent(string eventName, string overrideSourceName = "")
        {
            string source = sourceName;
            if(overrideSourceName != "")
                source = overrideSourceName;

            analyticsFired?.Invoke(eventName, source);
        }
    }
}
