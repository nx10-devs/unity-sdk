using UnityEngine;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace NX10
{
    public class NativeMagnetometer : MonoBehaviour
    {
        private bool isRunning = false;

        public bool MagSupported
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                return IOSMagnetometer.IsAvailable();
#elif ENABLE_INPUT_SYSTEM
                return MagneticFieldSensor.current != null;
#elif ENABLE_LEGACY_INPUT_MANAGER
                return true;
#else
                return false;
#endif
            }
        }

        public void StartSensor()
        {
            if (isRunning)
                return;

#if UNITY_IOS && !UNITY_EDITOR
            IOSMagnetometer.Start();
#endif
            isRunning = true;
        }

        public void StopSensor()
        {
            if (!isRunning)
                return;

#if UNITY_IOS && !UNITY_EDITOR
            IOSMagnetometer.Stop();
#endif
            isRunning = false;
        }

        private void OnDisable()
        {
            StopSensor();
        }

        private void OnDestroy()
        {
            StopSensor();
        }

        public Vector3 RawMag
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                if (!isRunning)
                {
                    return Vector3.zero;
                }
                Vector3 rawMag = IOSMagnetometer.GetRawData();
                rawMag = rawMag.Round(1, System.MidpointRounding.AwayFromZero);
                return rawMag;
#elif ENABLE_INPUT_SYSTEM
                if (MagneticFieldSensor.current == null)
                {
                    return Vector3.zero;
                }
                Vector3 rawMag = MagneticFieldSensor.current.magneticField.ReadUnprocessedValue();
                rawMag = rawMag.Round(1, System.MidpointRounding.AwayFromZero);
                return rawMag;
#elif ENABLE_LEGACY_INPUT_MANAGER
                Vector3 rawMag = Input.compass.rawVector;
                rawMag = rawMag.Round(1, System.MidpointRounding.AwayFromZero);
                return rawMag;
#else
                return Vector3.zero;
#endif
            }
        }
    }
}
