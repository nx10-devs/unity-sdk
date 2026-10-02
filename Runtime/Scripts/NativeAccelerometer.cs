using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace NX10
{
    public class NativeAccelerometer : MonoBehaviour
    {
        private const float metresPerSecondSquaredConverstion = 9.80665f;

        public bool AccSupported
        {

            get
            {
#if ENABLE_INPUT_SYSTEM
                return UnityEngine.InputSystem.Accelerometer.current != null;
#elif ENABLE_LEGACY_INPUT_MANAGER
                return SystemInfo.supportsGyroscope;
#endif
            }

        }

        public Vector3 AccAcceleration
        {
            get
            {

#if ENABLE_INPUT_SYSTEM
                Vector3 accel = Accelerometer.current.acceleration.ReadUnprocessedValue();
#if UNITY_IOS
                accel *= -metresPerSecondSquaredConverstion;
#endif
//Strange twist here, Android seemingly gives us the value in m/s2 which is odd as all other values given in gs
                accel = accel.RoundToFivePlaces();
                return accel;
#elif ENABLE_LEGACY_INPUT_MANAGER
                Vector3 accel = ConvertAccelerometerData(Input.acceleration);
                return accel;
#endif

                return Vector3.zero;
            }
        }

        public Vector3 ConvertAccelerometerData(Vector3 screenAccel)
        {
            if (!Input.compensateSensors) return screenAccel;

            Vector3 convertedVector;
            switch (Screen.orientation)
            {
                case (UnityEngine.ScreenOrientation.LandscapeLeft):
                    convertedVector = new Vector3(-screenAccel.y, screenAccel.x, -screenAccel.z);
                    break;
                case UnityEngine.ScreenOrientation.LandscapeRight:
                    convertedVector = new Vector3(screenAccel.y, -screenAccel.x, -screenAccel.z);
                    break;
                case UnityEngine.ScreenOrientation.PortraitUpsideDown:
                    convertedVector = new Vector3(screenAccel.x, screenAccel.y, -screenAccel.z);
                    break;
                case UnityEngine.ScreenOrientation.Portrait:
                    convertedVector = new Vector3(-screenAccel.x, -screenAccel.y, -screenAccel.z);
                    break;
                default:
                    convertedVector = screenAccel;
                    break;
            }

            convertedVector = convertedVector * metresPerSecondSquaredConverstion;
            convertedVector = convertedVector.RoundToFivePlaces();
            return convertedVector;
        }
    }
}
