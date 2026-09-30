using UnityEngine;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace NX10
{
    public class NativeMagnetometer : MonoBehaviour
    {
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
#endif
            }
        }

        public Vector3 RawMag
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                IOSMagnetometer.Start(); 
                Vector3 rawMag = IOSMagnetometer.GetRawData();
                rawMag = rawMag.Round(1, System.MidpointRounding.AwayFromZero);
                return rawMag;
#elif ENABLE_INPUT_SYSTEM
                Vector3 rawMag = MagneticFieldSensor.current.magneticField.ReadUnprocessedValue();
                rawMag = rawMag.Round(1, System.MidpointRounding.AwayFromZero);
                return rawMag;
#elif ENABLE_LEGACY_INPUT_MANAGER
                Vector3 rawMag = Input.compass.rawVector;
                rawMag = rawMag.Round(1, System.MidpointRounding.AwayFromZero);
                return rawMag;
#endif
            }
        }
    }
}
