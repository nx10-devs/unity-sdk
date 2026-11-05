using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;
using Gyroscope = UnityEngine.InputSystem.Gyroscope;
#endif

namespace NX10
{
    public class NX10TelemetryManager : MonoBehaviour
    {
        public NativeGyro nativeGyro { get; private set; }
        public NativeScale nativeScale { get; private set; }
        public NativeAccelerometer nativeAccelerometer { get; private set; }
        public NativeMagnetometer nativeMagnetometer { get; private set; }
        public NativeTouch nativeTouch { get; private set; }

        private bool canCollectTelemetryData;
        private bool isRunning;
        private NX10TelemetryCollectionWindow currentCollectionWindow;
        private NX10TelemetryInferenceWindow currentInferenceWindow;
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
        public NX10TelemetryCollectionWindow CurrentCollectionWindow => currentCollectionWindow;
        public NX10TelemetryInferenceWindow CurrentInferenceWindow => currentInferenceWindow;
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
            nativeTouch = GetComponent<NativeTouch>();

#if ENABLE_INPUT_SYSTEM
            if (Gyroscope.current != null)
                InputSystem.EnableDevice(Gyroscope.current);

            if (Accelerometer.current != null)
                InputSystem.EnableDevice(Accelerometer.current);

            if (MagneticFieldSensor.current != null) 
                InputSystem.EnableDevice(MagneticFieldSensor.current);

            EnhancedTouchSupport.Enable();

#elif ENABLE_LEGACY_INPUT_MANAGER

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

            if(canCollectTelemetryData && currentInferenceWindow != null)
            {
                currentInferenceWindow?.PruneOldEvents();
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

            nativeTouch.Initialise(nativeScale, dpi);
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
            {
                StartTelemetryInferenceWindow();
                StartTelemetryCollectionWindow();
            }
            else
            {
                EndTelemetryCollectionWindow();
            }
        }

        private void StartTelemetryInferenceWindow()
        {
            currentInferenceWindow = new NX10TelemetryInferenceWindow()
            {
                startTimestamp = DateTime.Now,
                previousAccTimestamp = DateTime.Now,
                previousGyroTimestamp = DateTime.Now,
                previousMagTimestamp = DateTime.Now,
                previousTouchTimestamp = DateTime.Now,
            };
        }

        private void StartTelemetryCollectionWindow()
        {
            if (currentCollectionWindow != null)
                EndTelemetryCollectionWindow();

            currentCollectionWindow = new NX10TelemetryCollectionWindow()
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
            double offsetCollection = Math.Round(currentCollectionWindow.Offset().TotalMilliseconds, 3, MidpointRounding.AwayFromZero);
            float offsetInference = (float)Math.Round(currentInferenceWindow.GyroDelta().TotalMilliseconds, 3, MidpointRounding.AwayFromZero);
            currentInferenceWindow.previousGyroTimestamp = DateTime.Now;

            if (nativeGyro.GyroSupported)
            {
                Vector3 rotationRate = nativeGyro.GyroRotationRate;
                currentCollectionWindow.inputEvents.Add(new GyroEvent
                {
                    timestampOffsetMs = offsetCollection,
                    x = rotationRate.x,
                    y = rotationRate.y,
                    z = rotationRate.z,
                });

                currentInferenceWindow.gyroEvents.Add(offsetInference);
                currentInferenceWindow.gyroEvents.Add(rotationRate.x);
                currentInferenceWindow.gyroEvents.Add(rotationRate.y);
                currentInferenceWindow.gyroEvents.Add(rotationRate.z);
            }
        }

        private void CollectAccelData()
        {
            double offset = Math.Round(currentCollectionWindow.Offset().TotalMilliseconds, 3, MidpointRounding.AwayFromZero);
            float offsetInference = (float)Math.Round(currentInferenceWindow.AccDelta().TotalMilliseconds, 3, MidpointRounding.AwayFromZero);
            currentInferenceWindow.previousAccTimestamp = DateTime.Now;

            if (nativeAccelerometer.AccSupported)
            {
                Vector3 acceleration = nativeAccelerometer.AccAcceleration;
                currentCollectionWindow.inputEvents.Add(new AccelerometerEvent
                {
                    timestampOffsetMs = offset,
                    x = acceleration.x,
                    y = acceleration.y,
                    z = acceleration.z
                });

                currentInferenceWindow.accelEvents.Add(offsetInference);
                currentInferenceWindow.accelEvents.Add(acceleration.x);
                currentInferenceWindow.accelEvents.Add(acceleration.y);
                currentInferenceWindow.accelEvents.Add(acceleration.z);
            }
        }

        private void CollectMagData()
        {
            double offset = Math.Round(currentCollectionWindow.Offset().TotalMilliseconds, 3, MidpointRounding.AwayFromZero);
            float offsetInference = (float)Math.Round(currentInferenceWindow.MagDelta().TotalMilliseconds, 3, MidpointRounding.AwayFromZero);
            currentInferenceWindow.previousMagTimestamp = DateTime.Now;

            if (nativeMagnetometer.MagSupported)
            {
                Vector3 rawMag = nativeMagnetometer.RawMag;
                currentCollectionWindow.inputEvents.Add(new MagnetometerEvent
                {
                    timestampOffsetMs = offset,
                    x = nativeMagnetometer.RawMag.x,
                    y = nativeMagnetometer.RawMag.y,
                    z = nativeMagnetometer.RawMag.z,
                });

                currentInferenceWindow.magEvents.Add(offsetInference);
                currentInferenceWindow.magEvents.Add(rawMag.x);
                currentInferenceWindow.magEvents.Add(rawMag.y);
                currentInferenceWindow.magEvents.Add(rawMag.z);
            }
        }

        private void CollectTouchDataV2()
        {
            double offset = Math.Round(currentCollectionWindow.Offset().TotalMilliseconds, 3, MidpointRounding.AwayFromZero);
            float offsetInference = (float)Math.Round(currentInferenceWindow.TouchDelta().TotalMilliseconds, 3, MidpointRounding.AwayFromZero);
            currentInferenceWindow.previousTouchTimestamp = DateTime.Now;

            foreach (NativeTouch.TouchObject touchObject in nativeTouch.Touches)
            {
                currentCollectionWindow.inputEvents.Add(new TouchInputEventV2
                {
                    timestampOffsetMs = offset,
                    touchId = touchObject.touchId,
                    touchType = touchObject.touchType,
                    touchObject = null,
                    xMm = touchObject.x,
                    yMm = touchObject.y,
                    touchRadiusMm = touchObject.touchRadius
                });

                currentInferenceWindow.touchEvents.Add(offsetInference);
                currentInferenceWindow.touchEvents.Add(nativeTouch.Touches.IndexOf(touchObject));
                currentInferenceWindow.touchEvents.Add(touchObject.touchTypeFloat);
                currentInferenceWindow.touchEvents.Add((float)touchObject.x);
                currentInferenceWindow.touchEvents.Add((float)touchObject.y);
                currentInferenceWindow.touchEvents.Add((float)touchObject.touchRadius);
            }
        }
    }
}