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
        public NativeAccelerometer nativeAccelerometer { get; private set; }
        public NativeMagnetometer nativeMagnetometer { get; private set; }

        private bool canCollectTelemetryData;
        private bool isRunning;
        private NX10TelemetryWindow currentCollectionWindow;
        private float timer = 0.0f;

        public Action<string, double, List<IInputEvent>> sendTelemetryDataRequest;

        public int? gyroHZ;
        public int? accelerometerHZ;
        public int? touchHZ;
        public int? magnetometerHZ;
        public int? acquisitionWindowSize;
        public float? screenBrightnessDelta;
        public bool? compressTelemetry;

        private float lastRecordedBrightness = -1f;
        private string lastRecordedOrientation = null;

        private float dpi;

        private bool canCollectGyro => gyroHZ != null;
        private bool canCollectAccelerometer => accelerometerHZ != null;
        private bool canCollectTouch => touchHZ != null;
        private bool canOpenWindow => acquisitionWindowSize != null;
        private bool canCollectMagnetometer => magnetometerHZ != null;

        //these are used by debugmanager
        public bool CanCollectTelemetryData => canCollectTelemetryData;
        public NX10TelemetryWindow CurrentCollectionWindow => currentCollectionWindow;
        public float Timer => timer;
        public int? TouchHz => touchHZ;
        public int? AcquisitionWindowSize => acquisitionWindowSize;
        public float Dpi => dpi;

        private void Awake()
        {
            lastRecordedBrightness = -1;
            lastRecordedOrientation = null;

            nativeGyro = GetComponent<NativeGyro>();
            nativeAccelerometer = GetComponent<NativeAccelerometer>();
            nativeMagnetometer = GetComponent<NativeMagnetometer>();
            nativeScale = GetComponent<NativeScale>();

#if ENABLE_INPUT_SYSTEM
            if (Gyroscope.current != null)
                InputSystem.EnableDevice(Gyroscope.current);

            if (Accelerometer.current != null)
                InputSystem.EnableDevice(Accelerometer.current);

            if (MagneticFieldSensor.current != null) 
                InputSystem.EnableDevice(MagneticFieldSensor.current);

            EnhancedTouchSupport.Enable();

           

            return;
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (SystemInfo.supportsGyroscope)
                Input.gyro.enabled = true;

            Input.compass.enabled = true;
#endif
        }

        private void OnEnable()
        {
            isRunning = true;

#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM

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

            if (canCollectTelemetryData && currentCollectionWindow != null)
            {
                CheckAndCollectBrightnessData();
                CheckAndCollectOrientationData();
            }
        }

        public void SetTelemetryVariables(int? gyroHz, int? accelerometerHz, int? touchHz, int? magnetometerHz, float? screenBrightnessDelta, int? acquisitionWindowSize, bool? compressTelemetry, float dpi)
        {
            this.gyroHZ = gyroHz;
            this.accelerometerHZ = accelerometerHz;
            this.touchHZ = touchHz;
            this.magnetometerHZ = magnetometerHz;
            this.screenBrightnessDelta = screenBrightnessDelta;
            this.acquisitionWindowSize = acquisitionWindowSize;
            this.compressTelemetry = compressTelemetry;

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

        private void CheckAndCollectBrightnessData()
        {
            float currentBrightness = Screen.brightness;

            if (currentBrightness < 0f) currentBrightness = 0.5f;

            if (lastRecordedBrightness < 0f || Mathf.Abs(currentBrightness - lastRecordedBrightness) >= screenBrightnessDelta)
            {
                double offset = Math.Round(currentCollectionWindow.Offset().TotalMilliseconds, 3, MidpointRounding.AwayFromZero);

                currentCollectionWindow.inputEvents.Add(new BrightnessEvent
                {
                    timestampOffsetMs = offset,
                    screenBrightness = (float)Math.Round(currentBrightness, 2, MidpointRounding.AwayFromZero)
                });

                lastRecordedBrightness = currentBrightness;
            }
        }

        private void CheckAndCollectOrientationData()
        {
            DeviceOrientation physicalOrientation = Input.deviceOrientation;
            string currentOrientation = MapDeviceOrientation(physicalOrientation);

            if (lastRecordedOrientation == null || lastRecordedOrientation != currentOrientation)
            {
                double offset = Math.Round(currentCollectionWindow.Offset().TotalMilliseconds, 3, MidpointRounding.AwayFromZero);

                currentCollectionWindow.inputEvents.Add(new OrientationEvent
                {
                    timestampOffsetMs = offset,
                    screenOrientation = currentOrientation
                });

                lastRecordedOrientation = currentOrientation;
            }
        }

        private string MapDeviceOrientation(DeviceOrientation orientation)
        {
            switch (orientation)
            {
                case DeviceOrientation.Portrait:
                    return "vertical-up";
                case DeviceOrientation.PortraitUpsideDown:
                    return "vertical-down";
                case DeviceOrientation.LandscapeLeft:
                    return "horizontal-down"; 
                case DeviceOrientation.LandscapeRight:
                    return "horizontal-up";   
                default:
                    return "vertical-up";
            }
        }

        public void SetTelemetryCollection(bool canCollect)
        {
            if(!NX10Manager.Instance.IsSessionValid && canCollect)
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

            if (canCollectGyro)
                StartCoroutine(CollectionWorker(gyroHZ.Value, CollectGyroData));

            if(canCollectAccelerometer)
                StartCoroutine(CollectionWorker(accelerometerHZ.Value, CollectAccelData));

            if (canCollectMagnetometer) 
                StartCoroutine(CollectionWorker(magnetometerHZ.Value, CollectMagData));

            if (canCollectTouch)
                StartCoroutine(CollectionWorker(touchHZ.Value, CollectTouchDataV2));
        }

        private void EndTelemetryCollectionWindow()
        {
            if (currentCollectionWindow == null) return;

            SendTelemetryData(currentCollectionWindow.startTimestampISO);

            currentCollectionWindow.Dispose();
            currentCollectionWindow = null;
            timer = 0;

            StopAllCoroutines();
        }

        private void SendTelemetryData(string timestamp)
        {
            if (!NX10Manager.Instance.IsSessionValid)
                return;

            if(currentCollectionWindow.inputEvents.Count >= 6)
                sendTelemetryDataRequest?.Invoke(timestamp, currentCollectionWindow.Offset().TotalMilliseconds, currentCollectionWindow.inputEvents);
        }

        private void CollectGyroData()
        {
            double offset = Math.Round(currentCollectionWindow.Offset().TotalMilliseconds, 3, MidpointRounding.AwayFromZero);
            if(nativeGyro.GyroSupported)
            {
                currentCollectionWindow.inputEvents.Add(new GyroEvent
                {
                    timestampOffsetMs = offset,
                    x = nativeGyro.GyroRotationRate.x,
                    y = nativeGyro.GyroRotationRate.y,
                    z = nativeGyro.GyroRotationRate.z,
                });
            }
        }

        private void CollectAccelData()
        {
            double offset = Math.Round(currentCollectionWindow.Offset().TotalMilliseconds, 3, MidpointRounding.AwayFromZero);
            if(nativeAccelerometer.AccSupported)
            {
                currentCollectionWindow.inputEvents.Add(new AccelerometerEvent
                {
                    timestampOffsetMs = offset,
                    x = nativeAccelerometer.AccAcceleration.x,
                    y = nativeAccelerometer.AccAcceleration.y,
                    z = nativeAccelerometer.AccAcceleration.z
                });
            }
        }

        private void CollectMagData()
        {
            double offset = Math.Round(currentCollectionWindow.Offset().TotalMilliseconds, 3, MidpointRounding.AwayFromZero);
            if(nativeMagnetometer.MagSupported)
            {
                currentCollectionWindow.inputEvents.Add(new MagnetometerEvent
                {
                    timestampOffsetMs = offset,
                    x = nativeMagnetometer.RawMag.x,
                    y = nativeMagnetometer.RawMag.y,
                    z = nativeMagnetometer.RawMag.z,
                });
            }
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
#elif UNITY_ANDROID && !UNITY_EDITOR
                majorRadius *= Mathf.Min(Screen.width, Screen.height);
                radiusMm = majorRadius;
                radiusMm = Math.Round(radiusMm, 4, MidpointRounding.AwayFromZero);
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

#if ENABLE_INPUT_SYSTEM
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
#endif

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
    }
}