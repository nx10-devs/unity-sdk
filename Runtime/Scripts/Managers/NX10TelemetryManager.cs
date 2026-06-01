using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Gyroscope = UnityEngine.InputSystem.Gyroscope;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
#endif

namespace NX10
{
    public class NX10TelemetryManager : MonoBehaviour
    {
        public NativeGyro nativeGyro { get; private set; }
        public NativeScale nativeScale { get; private set; }

        private bool canCollectTelemetryData;
        private bool isRunning;
        private NX10TelemetryWindow currentCollectionWindow;
        private float timer = 0.0f;

        public Action<string, double, List<IInputEvent>> sendTelemetryDataRequest;

        public int? gyroHZ;
        public int? accelerometerHZ;
        public int? touchHZ;
        public int? acquisitionWindowSize;

        private float dpi;

        private bool canCollectGyro => gyroHZ != null;
        private bool canCollectAccelerometer => accelerometerHZ != null;
        private bool canCollectTouch => touchHZ != null;
        private bool canOpenWindow => acquisitionWindowSize != null;

        //these are used by debugmanager
        public bool CanCollectTelemetryData => canCollectTelemetryData;
        public NX10TelemetryWindow CurrentCollectionWindow => currentCollectionWindow;
        public float Timer => timer;
        public int? TouchHz => touchHZ;
        public int? AcquisitionWindowSize => acquisitionWindowSize;
        public float Dpi => dpi;

        private void Awake()
        {
#if ENABLE_INPUT_SYSTEM
            if (Gyroscope.current != null)
                InputSystem.EnableDevice(Gyroscope.current);

            if (Accelerometer.current != null)
                InputSystem.EnableDevice(Accelerometer.current);

            EnhancedTouchSupport.Enable();

            nativeGyro = GetComponent<NativeGyro>();
            nativeScale = GetComponent<NativeScale>();

            return;
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (SystemInfo.supportsGyroscope)
                Input.gyro.enabled = true;
#endif
        }

        private void OnEnable()
        {
            isRunning = true;

#if UNITY_EDITOR
            TouchSimulation.Enable();
#endif
        }

        private void OnDisable()
        {
            isRunning = false;
        }

        private void Update()
        {
            UpdateTelemetryCollectionWindow();
        }

        public void SetTelemetryVariables(int? gyroHz, int? accelerometerHz, int? touchHz, int? acquisitionWindowSize, float dpi)
        {
            this.gyroHZ = gyroHz;
            this.accelerometerHZ = accelerometerHz;
            this.touchHZ = touchHz;
            this.acquisitionWindowSize = acquisitionWindowSize;
            this.dpi = dpi;
        }

        private IEnumerator CollectionWorker(float frequency, System.Action collectionMethod)
        {
            float interval = 1f / frequency;
            var wait = new WaitForSecondsRealtime(interval);

            while (isRunning)
            {
                if (canCollectTelemetryData && currentCollectionWindow != null)
                {
                    collectionMethod.Invoke();
                }
                yield return wait;
            }
        }

        private void UpdateTelemetryCollectionWindow()
        {
            if (!canOpenWindow)
                return;

            if (!canCollectTelemetryData || currentCollectionWindow == null)
                return;

            timer += Time.deltaTime;

            if (timer > acquisitionWindowSize.Value)
            {
                StartTelemetryCollectionWindow();
            }
        }

        public void SetTelemetryCollection(bool canCollect)
        {
            if(!NX10Manager.Instance.Initialised)
            {
                Debug.LogError("NX10 Manager not initialised, ensure it is before starting a collection window");
                return;
            }

            if (!canOpenWindow)
                return;

            canCollectTelemetryData = canCollect;

            if (canCollect)
                StartTelemetryCollectionWindow();
            else
                EndTelemetryCollectionWindow();
        }

        private void StartTelemetryCollectionWindow()
        {
            if (currentCollectionWindow != null)
                EndTelemetryCollectionWindow();

            currentCollectionWindow = new NX10TelemetryWindow()
            {
                startTimestamp = DateTime.UtcNow,
                inputEvents = new List<IInputEvent>()
            };

            if(canCollectGyro)
                StartCoroutine(CollectionWorker(gyroHZ.Value, CollectGyroData));

            if(canCollectAccelerometer)
                StartCoroutine(CollectionWorker(accelerometerHZ.Value, CollectAccelData));

            if(canCollectTouch)
                StartCoroutine(CollectionWorker(touchHZ.Value, CollectTouchDataV2));
        }

        private void EndTelemetryCollectionWindow()
        {
            if (currentCollectionWindow == null) return;

            SendTelemetryData(currentCollectionWindow.startTimestampISO);
            currentCollectionWindow.Dispose();
            currentCollectionWindow = null;
            timer -= acquisitionWindowSize.Value;

            StopAllCoroutines();
        }

        private void SendTelemetryData(string timestamp)
        {
            sendTelemetryDataRequest?.Invoke(timestamp, currentCollectionWindow.Offset().TotalMilliseconds, currentCollectionWindow.inputEvents);
        }

        private void CollectGyroData()
        {
            double offset = Math.Round(currentCollectionWindow.Offset().TotalMilliseconds, 3, MidpointRounding.AwayFromZero);
#if ENABLE_INPUT_SYSTEM
            if (Gyroscope.current != null)
            {
                currentCollectionWindow.inputEvents.Add(new GyroEvent
                {
                    timestampOffsetMs = offset,
                    x = (float)Math.Round(nativeGyro.rotationRateUnbiased.x, 5, MidpointRounding.AwayFromZero),
                    y = (float)Math.Round(nativeGyro.rotationRateUnbiased.y, 5, MidpointRounding.AwayFromZero),
                    z = (float)Math.Round(nativeGyro.rotationRateUnbiased.z, 5, MidpointRounding.AwayFromZero),
                });
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (SystemInfo.supportsGyroscope)
        {
            currentCollectionWindow.inputEvents.Add(new GyroEvent {
                timestampOffsetMs = offset,
                x = (float)Math.Round(Input.gyro.rotationRateUnbiased.x, 5, MidpointRounding.AwayFromZero),
                y = (float)Math.Round(Input.gyro.rotationRateUnbiased.y, 5, MidpointRounding.AwayFromZero),
                z = (float)Math.Round(Input.gyro.rotationRateUnbiased.z, 5, MidpointRounding.AwayFromZero),
            });
        }
#endif
        }

        private void CollectAccelData()
        {
            double offset = Math.Round(currentCollectionWindow.Offset().TotalMilliseconds, 3, MidpointRounding.AwayFromZero);
#if ENABLE_INPUT_SYSTEM
            if (Accelerometer.current != null)
            {
                Vector3 accel = ConvertAccelerometerData(Accelerometer.current.acceleration.ReadValue());
                currentCollectionWindow.inputEvents.Add(new AccelerometerEvent
                {
                    timestampOffsetMs = offset,
                    x = accel.x,
                    y = accel.y,
                    z = accel.z
                });
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (SystemInfo.supportsGyroscope)
        {
            Vector3 accel = ConvertAccelerometerData(Input.gyro.userAcceleration);
            currentCollectionWindow.inputEvents.Add(new AccelerometerEvent {
                timestampOffsetMs = offset,
                x = (float)Math.Round(accel.x, 5, MidpointRounding.AwayFromZero),
                x = (float)Math.Round(accel.y, 5, MidpointRounding.AwayFromZero),
                x = (float)Math.Round(accel.z, 5, MidpointRounding.AwayFromZero),
            });
        }
#endif
        }

        private void CollectTouchDataV2()
        {
            double offset = Math.Round(currentCollectionWindow.Offset().TotalMilliseconds, 3, MidpointRounding.AwayFromZero); 
#if ENABLE_INPUT_SYSTEM
            foreach (var touch in Touch.activeTouches)
            {
                float majorRadius = Mathf.Max(touch.radius.x, touch.radius.y);
                double radiusMm = PixelsToMillimeters(majorRadius);
#if UNITY_IOS && !UNITY_EDITOR
                radiusMm = MmPerPoint() * majorRadius;
                radiusMm = Math.Round(radiusMm, 3, MidpointRounding.AwayFromZero);
#endif
                currentCollectionWindow.inputEvents.Add(new TouchInputEventV2
                {
                    timestampOffsetMs = offset,
                    touchId = touch.touchId.ToString(),
                    touchType = ConvertTouchPhaseToTouchType(touch.phase),
                    touchObject = null,
                    xMm = PixelsToMillimeters(touch.screenPosition.x),
                    yMm = PixelsToMillimeters(touch.screenPosition.y),
                    touchRadiusMm = radiusMm
                });
            }

#else
                foreach (var touch in Input.touches)
            {
                currentCollectionWindow.inputEvents.Add(new TouchInputEventV2 {
                    timestampOffsetMs = offset,
                    touchId = touch.fingerId.ToString(),
                    touchType = ConvertTouchPhaseToTouchType(touch.phase),
                    touchObject = null,
                    xMm = PixelsToMillimeters(touch.position.x),
                    yMm = PixelsToMillimeters(touch.position.y),
                    touchRadiusMm = PixelsToMillimeters(touch.radius),
                });
            }
#endif
        }

        private string ConvertTouchPhaseToTouchType(UnityEngine.InputSystem.TouchPhase touchPhase)
        {
            switch (touchPhase)
            {
                case UnityEngine.InputSystem.TouchPhase.Began:
                    return "down";
                case UnityEngine.InputSystem.TouchPhase.Ended:
                    return "up";
                case UnityEngine.InputSystem.TouchPhase.Moved:
                    return "move";
                case UnityEngine.InputSystem.TouchPhase.Stationary:
                    return "stationary";
                case UnityEngine.InputSystem.TouchPhase.Canceled:
                    return "cancelled";
            }

            throw new NotImplementedException();
        }

        private string ConvertTouchPhaseToTouchType(UnityEngine.TouchPhase touchPhase)
        {
            switch (touchPhase)
            {
                case UnityEngine.TouchPhase.Began:
                    return "down";
                case UnityEngine.TouchPhase.Ended:
                    return "up";
                case UnityEngine.TouchPhase.Moved:
                    return "move";
                case UnityEngine.TouchPhase.Stationary:
                    return "stationary";
                case UnityEngine.TouchPhase.Canceled:
                    return "cancelled";
            }

            throw new NotImplementedException();
        }

        public double MmPerPoint()
        {
            double nativeIOSScale = nativeScale.GetNativeScale();
            return (nativeIOSScale / dpi) * 25.4;
        }

        public double PixelsToMillimeters(double pixels)
        {
            double inches = pixels / dpi;
            double millimeters = inches * 25.4f;
            return Math.Round(millimeters, 3, MidpointRounding.AwayFromZero);
        }

        private const float metresPerSecondSquaredConverstion = 9.80665f;
        public Vector3 ConvertAccelerometerData(Vector3 screenAccel)
        {
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
                    convertedVector = new Vector3(-screenAccel.x, -screenAccel.y, -screenAccel.z);
                    break;
                case UnityEngine.ScreenOrientation.Portrait:
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