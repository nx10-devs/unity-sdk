using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
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
        private bool sensorsEnabled;

#if ENABLE_INPUT_SYSTEM
        private bool didEnableGyroDevice;
        private bool didEnableAccelDevice;
        private bool didEnableMagDevice;
        private bool didEnableEnhancedTouch;
        private bool didEnableTouchSimulation;
#elif ENABLE_LEGACY_INPUT_MANAGER
        private bool didEnableLegacyGyro;
        private bool didEnableLegacyCompass;
#endif

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

        // Used by debug manager / inspection
        public bool CanCollectTelemetryData => canCollectTelemetryData;
        public bool AreSensorsEnabled => sensorsEnabled;
        public NX10TelemetryCollectionWindow CurrentCollectionWindow => currentCollectionWindow;
        public NX10TelemetryInferenceWindow CurrentInferenceWindow => currentInferenceWindow;
        public float Timer => timer;
        public int? TouchHz => touchHZ;
        public int? AcquisitionWindowSize => acquisitionWindowSize;
        public float Dpi => dpi;

        private void Awake()
        {
            lastRecordedBrightness = -1f;
            lastRecordedOrientation = null;

            nativeGyro = GetComponent<NativeGyro>();
            nativeAccelerometer = GetComponent<NativeAccelerometer>();
            nativeMagnetometer = GetComponent<NativeMagnetometer>();
            nativeScale = GetComponent<NativeScale>();
            nativeTouch = GetComponent<NativeTouch>();
        }

        private void OnEnable()
        {
            isRunning = true;
        }

        private void OnDisable()
        {
            isRunning = false;
            StopTelemetry(flushRemainingWindow: false, clearConfig: false);
        }

        private void OnDestroy()
        {
            DisableSensors();
        }

        private void EnableSensors()
        {
            if (sensorsEnabled)
                return;

            if (canCollectGyro && nativeGyro != null)
            {
                nativeGyro.StartSensor();
            }

            if (canCollectMagnetometer && nativeMagnetometer != null)
            {
                nativeMagnetometer.StartSensor();
            }

#if ENABLE_INPUT_SYSTEM
            if (canCollectGyro && Gyroscope.current != null && !Gyroscope.current.enabled)
            {
                InputSystem.EnableDevice(Gyroscope.current);
                didEnableGyroDevice = true;
            }

            if (canCollectAccelerometer && Accelerometer.current != null && !Accelerometer.current.enabled)
            {
                InputSystem.EnableDevice(Accelerometer.current);
                didEnableAccelDevice = true;
            }

            if (canCollectMagnetometer && MagneticFieldSensor.current != null && !MagneticFieldSensor.current.enabled)
            {
                InputSystem.EnableDevice(MagneticFieldSensor.current);
                didEnableMagDevice = true;
            }

            if (canCollectTouch && !EnhancedTouchSupport.enabled)
            {
                EnhancedTouchSupport.Enable();
                didEnableEnhancedTouch = true;
            }

#if UNITY_EDITOR
            if (canCollectTouch && (TouchSimulation.instance == null || !TouchSimulation.instance.enabled))
            {
                TouchSimulation.Enable();
                didEnableTouchSimulation = true;
            }
#endif

#elif ENABLE_LEGACY_INPUT_MANAGER
            if (canCollectGyro && SystemInfo.supportsGyroscope && !Input.gyro.enabled)
            {
                Input.gyro.enabled = true;
                didEnableLegacyGyro = true;
            }

            if (canCollectMagnetometer && !Input.compass.enabled)
            {
                Input.compass.enabled = true;
                didEnableLegacyCompass = true;
            }
#endif

            sensorsEnabled = true;
        }

        private void DisableSensors()
        {
            nativeGyro?.StopSensor();
            nativeMagnetometer?.StopSensor();

            if (!sensorsEnabled)
                return;

#if ENABLE_INPUT_SYSTEM
            if (didEnableGyroDevice && Gyroscope.current != null && Gyroscope.current.enabled)
            {
                InputSystem.DisableDevice(Gyroscope.current);
            }
            didEnableGyroDevice = false;

            if (didEnableAccelDevice && Accelerometer.current != null && Accelerometer.current.enabled)
            {
                InputSystem.DisableDevice(Accelerometer.current);
            }
            didEnableAccelDevice = false;

            if (didEnableMagDevice && MagneticFieldSensor.current != null && MagneticFieldSensor.current.enabled)
            {
                InputSystem.DisableDevice(MagneticFieldSensor.current);
            }
            didEnableMagDevice = false;

#if UNITY_EDITOR
            if (didEnableTouchSimulation)
            {
                TouchSimulation.Disable();
                didEnableTouchSimulation = false;
            }
#endif

            if (didEnableEnhancedTouch && EnhancedTouchSupport.enabled)
            {
                EnhancedTouchSupport.Disable();
            }
            didEnableEnhancedTouch = false;

#elif ENABLE_LEGACY_INPUT_MANAGER
            if (didEnableLegacyGyro && SystemInfo.supportsGyroscope)
            {
                Input.gyro.enabled = false;
            }
            didEnableLegacyGyro = false;

            if (didEnableLegacyCompass)
            {
                Input.compass.enabled = false;
            }
            didEnableLegacyCompass = false;
#endif

            sensorsEnabled = false;
        }

        private void Update()
        {
            if (!canCollectTelemetryData || !NX10Manager.Instance.CanCollect)
            {
                return;
            }

            UpdateTelemetryCollectionWindow();

            if (currentCollectionWindow != null)
            {
                CheckAndCollectBrightnessData();
                CheckAndCollectOrientationData();
            }

            if (currentInferenceWindow != null)
            {
                currentInferenceWindow.PruneOldEvents();
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

        public void ClearTelemetryVariables()
        {
            gyroHZ = null;
            accelerometerHZ = null;
            touchHZ = null;
            magnetometerHZ = null;
            screenBrightnessDelta = null;
            acquisitionWindowSize = null;
            compressTelemetry = null;
            lastRecordedBrightness = -1f;
            lastRecordedOrientation = null;
            timer = 0f;
        }

        private IEnumerator CollectionWorker(float frequency, System.Action collectionMethod)
        {
            if (frequency <= 0f)
                yield break;

            float interval = 1f / frequency;
            var wait = new WaitForSecondsRealtime(interval);

            while (isRunning && canCollectTelemetryData)
            {
                if (NX10Manager.Instance.CanCollect && currentCollectionWindow != null && currentInferenceWindow != null)
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
            if (screenBrightnessDelta == null)
                return;

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
            if (canCollect)
            {
                if (!NX10Manager.Instance.HasConsent)
                {
                    return;
                }

                if (!NX10Manager.Instance.IsSessionValid)
                {
                    Debug.LogWarning("[NX10] Cannot start telemetry before an active session is initialised.");
                    return;
                }

                if (!canOpenWindow)
                    return;

                if (canCollectTelemetryData && currentCollectionWindow != null)
                    return;

                canCollectTelemetryData = true;
                EnableSensors();
                StartTelemetryInferenceWindow();
                StartTelemetryCollectionWindow();
            }
            else
            {
                StopTelemetry(flushRemainingWindow: NX10Manager.Instance.CanCollect, clearConfig: false);
            }
        }

        public void StopTelemetry(bool flushRemainingWindow, bool clearConfig)
        {
            canCollectTelemetryData = false;
            StopAllCoroutines();

            EndTelemetryCollectionWindow(flush: flushRemainingWindow);
            EndTelemetryInferenceWindow();
            DisableSensors();

            lastRecordedBrightness = -1f;
            lastRecordedOrientation = null;
            timer = 0f;

            if (clearConfig)
            {
                ClearTelemetryVariables();
            }
        }

        public void RecordEvent(string eventName, Dictionary<string, object> eventData)
        {
            if (!canCollectTelemetryData || !NX10Manager.Instance.CanCollect)
                return;

            currentInferenceWindow?.RecordEvent(eventName, eventData);
        }

        public void RecordAttribute(string attributeKey)
        {
            if (!canCollectTelemetryData || !NX10Manager.Instance.CanCollect)
                return;

            currentInferenceWindow?.RecordAttribute(attributeKey);
        }

        public void RecordAttributes(IEnumerable<string> attributeKeys)
        {
            if (!canCollectTelemetryData || !NX10Manager.Instance.CanCollect)
                return;

            currentInferenceWindow?.RecordAttributes(attributeKeys);
        }

        private void StartTelemetryInferenceWindow()
        {
            if (currentInferenceWindow != null)
                EndTelemetryInferenceWindow();

            currentInferenceWindow = new NX10TelemetryInferenceWindow()
            {
                startTimestamp = DateTime.UtcNow,
                previousAccTimestamp = DateTime.UtcNow,
                previousGyroTimestamp = DateTime.UtcNow,
                previousMagTimestamp = DateTime.UtcNow,
                previousTouchTimestamp = DateTime.UtcNow,
            };
        }

        private void EndTelemetryInferenceWindow()
        {
            if (currentInferenceWindow == null) return;

            currentInferenceWindow.Dispose();
            currentInferenceWindow = null;
        }

        private void StartTelemetryCollectionWindow()
        {
            if (currentCollectionWindow != null)
                EndTelemetryCollectionWindow(flush: true);

            currentCollectionWindow = new NX10TelemetryCollectionWindow()
            {
                startTimestamp = DateTime.UtcNow,
                inputEvents = new List<IInputEvent>()
            };

            if (canCollectGyro)
                StartCoroutine(CollectionWorker(gyroHZ.Value, CollectGyroData));

            if (canCollectAccelerometer)
                StartCoroutine(CollectionWorker(accelerometerHZ.Value, CollectAccelData));

            if (canCollectMagnetometer)
                StartCoroutine(CollectionWorker(magnetometerHZ.Value, CollectMagData));

            if (canCollectTouch)
                StartCoroutine(CollectionWorker(touchHZ.Value, CollectTouchDataV2));
        }

        private void EndTelemetryCollectionWindow(bool flush)
        {
            if (currentCollectionWindow == null) return;

            if (flush && NX10Manager.Instance.CanCollect)
            {
                SendTelemetryData(currentCollectionWindow.startTimestampISO);
            }

            currentCollectionWindow.Dispose();
            currentCollectionWindow = null;
            timer = 0f;

            StopAllCoroutines();
        }

        private void SendTelemetryData(string timestamp)
        {
            if (!NX10Manager.Instance.CanCollect)
                return;

            if (currentCollectionWindow != null && currentCollectionWindow.inputEvents.Count >= 6)
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
                    x = rawMag.x,
                    y = rawMag.y,
                    z = rawMag.z,
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
