using System;
using System.Runtime.InteropServices;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

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

        public Vector3 rotationRateUnbiased
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                NativeVector3 nativeVec = _GetNativeRotationRateUnbiased();
                Vector3 rotation = new Vector3(nativeVec.x, nativeVec.y, nativeVec.z);
                rotation = rotation.RoundToFivePlaces();
                return rotation;
#elif ENABLE_INPUT_SYSTEM
                Vector3 rotation = UnityEngine.InputSystem.Gyroscope.current.angularVelocity.ReadValue();
                rotation = ConvertGyroData(rotation);
                return rotation;
#endif

                return Vector3.zero;
            }
        }

        public Vector3 ConvertGyroData(Vector3 screenGyro)
        {
            if (!Input.compensateSensors) return screenGyro;

            Vector3 convertedVector;
            switch (Screen.orientation)
            {
                case (UnityEngine.ScreenOrientation.LandscapeLeft):
                    convertedVector = new Vector3(screenGyro.y, -screenGyro.x, screenGyro.z);
                    break;
                case UnityEngine.ScreenOrientation.LandscapeRight:
                    convertedVector = new Vector3(-screenGyro.y, screenGyro.x, screenGyro.z);
                    break;
                case UnityEngine.ScreenOrientation.PortraitUpsideDown:
                    convertedVector = new Vector3(-screenGyro.x, -screenGyro.y, screenGyro.z);
                    break;
                case UnityEngine.ScreenOrientation.Portrait:
                default:
                    convertedVector = screenGyro;
                    break;
            }

            convertedVector = convertedVector.RoundToFivePlaces();
            return convertedVector;
        }


        private bool isRunning = false;

        void Start()
        {
#if UNITY_IOS && !UNITY_EDITOR
        _StartNativeGyro();
        isRunning = true;
#else
#endif
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
