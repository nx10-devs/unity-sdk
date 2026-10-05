using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace NX10
{
    public static class IOSMagnetometer
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void _StartMagnetometer();

        [DllImport("__Internal")]
        private static extern void _StopMagnetometer();

        [DllImport("__Internal")]
        private static extern bool _IsMagnetometerAvailable();

        [DllImport("__Internal")]
        private static extern void _GetMagnetometerData(out float x, out float y, out float z);
#endif

        public static bool IsAvailable()
        {
#if UNITY_IOS && !UNITY_EDITOR
            return _IsMagnetometerAvailable();
#else
            return false;
#endif
        }

        public static void Start()
        {
#if UNITY_IOS && !UNITY_EDITOR
            _StartMagnetometer();
#endif
        }

        public static void Stop()
        {
#if UNITY_IOS && !UNITY_EDITOR
            _StopMagnetometer();
#endif
        }

        public static Vector3 GetRawData()
        {
#if UNITY_IOS && !UNITY_EDITOR
            _GetMagnetometerData(out float x, out float y, out float z);
            return new Vector3(x, y, z);
#else
            return Vector3.zero;
#endif
        }
    }
}