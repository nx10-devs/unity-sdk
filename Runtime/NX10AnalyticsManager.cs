using System;
using UnityEngine;

namespace NX10
{
    public class NX10AnalyticsManager : MonoBehaviour
    {
        [SerializeField] private string sourceName;
        public event Action<string, string> analyticsFired;

        private void OnApplicationQuit()
        {
            FireEvent("app_closed");
        }

        public void FireEvent(string eventName)
        {
            analyticsFired?.Invoke(eventName, sourceName);
        }
    }
}
