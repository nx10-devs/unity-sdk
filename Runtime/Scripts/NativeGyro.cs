using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace NX10
{
    public class NativeGyro : MonoBehaviour
    {
        private struct NativeVector3
        {
            public float x;
            public float y;
            public float z;
        }

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void _StartNativeGyro();

    [DllImport("__Internal")]
    private static extern void _StopNativeGyro();

    [DllImport("__Internal")]
    private static extern NativeVector3 _GetNativeRotationRateUnbiased();
#else
        private static void _StartNativeGyro() { }
        private static void _StopNativeGyro() { }
        private static NativeVector3 _GetNativeRotationRateUnbiased() { return new NativeVector3(); }
#endif

        [Header("Sensor Output")]
        public Vector3 rotationRateUnbiased;


        private bool isRunning = false;

        void Start()
        {
#if UNITY_IOS && !UNITY_EDITOR
        _StartNativeGyro();
        isRunning = true;
#else
#endif
        }

        void Update()
        {
            if (!isRunning) return;

            NativeVector3 nativeVec = _GetNativeRotationRateUnbiased();

            rotationRateUnbiased = new Vector3(ConvertToMetresPerSecondSquared(nativeVec.x), ConvertToMetresPerSecondSquared(nativeVec.y), ConvertToMetresPerSecondSquared(nativeVec.z));
        }

        private const float conversionFloat = 9.80665f;
        private float ConvertToMetresPerSecondSquared(float radsPerSecond)
        {
            float metresPerSecond = conversionFloat * -radsPerSecond;
            return (float)Math.Round(metresPerSecond, 5, MidpointRounding.AwayFromZero);
        }

        void OnDestroy()
        {
            if (isRunning)
            {
                _StopNativeGyro();
            }
        }
    }
}
