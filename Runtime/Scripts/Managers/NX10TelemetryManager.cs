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
        private readonly List<Coroutine> activeWorkerCoroutines = new List<Coroutine>();
        private float timer = 0.0f;

        public int SessionEventAddCount { get; private set; }
        public DateTime? TimeOfLastInterstitial { get; private set; }

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

            if (canCollectTelemetryData)
            {
                StartSensorWorkers();
            }

#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM

            TouchSimulation.Enable();
#endif
        }

        private void OnDisable()
        {
            isRunning = false;
            activeWorkerCoroutines.Clear();
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

        public void OnSessionStarted()
        {
            SessionEventAddCount = 0;
            TimeOfLastInterstitial = null;
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
                StartSensorWorkers();
            }
            else
            {
                StopSensorWorkers();
                EndTelemetryCollectionWindow();
                EndTelemetryInferenceWindow();
            }
        }

        public void RecordEvent(string eventName, Dictionary<string, object> eventData)
        {
            if (!string.IsNullOrEmpty(eventName))
            {
                if (eventName == NX10SchemaKeys.Events.AdStarted &&
                    eventData != null &&
                    eventData.TryGetValue(NX10SchemaKeys.Metadata.Type, out object type) &&
                    type != null &&
                    string.Equals(type.ToString(), NX10SchemaKeys.AdTypes.Interstitial, StringComparison.OrdinalIgnoreCase))
                {
                    TimeOfLastInterstitial = DateTime.UtcNow;
                }

                SessionEventAddCount++;
            }

            currentInferenceWindow?.RecordEvent(eventName, eventData);
        }

        public void RecordAttribute(string attributeKey)
        {
            currentInferenceWindow?.RecordAttribute(attributeKey);
        }

        public void RecordAttributes(IEnumerable<string> attributeKeys)
        {
            currentInferenceWindow?.RecordAttributes(attributeKeys);
        }

        private void StartTelemetryInferenceWindow()
        {
            if (currentInferenceWindow != null)
                EndTelemetryInferenceWindow();

            currentInferenceWindow = new NX10TelemetryInferenceWindow()
            {
                startTimestamp = DateTime.UtcNow,
                eventAddCount = SessionEventAddCount,
                timeOfLastInterstitial = TimeOfLastInterstitial,
            };
        }

        private void EndTelemetryInferenceWindow()
        {
            if (currentInferenceWindow == null) return;

            currentInferenceWindow.Dispose();
            currentInferenceWindow = null;
        }

        private void StartSensorWorkers()
        {
            StopSensorWorkers();

            if (canCollectGyro)
                activeWorkerCoroutines.Add(StartCoroutine(CollectionWorker(gyroHZ.Value, CollectGyroData)));

            if (canCollectAccelerometer)
                activeWorkerCoroutines.Add(StartCoroutine(CollectionWorker(accelerometerHZ.Value, CollectAccelData)));

            if (canCollectMagnetometer)
                activeWorkerCoroutines.Add(StartCoroutine(CollectionWorker(magnetometerHZ.Value, CollectMagData)));

            if (canCollectTouch)
                activeWorkerCoroutines.Add(StartCoroutine(CollectionWorker(touchHZ.Value, CollectTouchDataV2)));
        }

        private void StopSensorWorkers()
        {
            for (int i = 0; i < activeWorkerCoroutines.Count; i++)
            {
                if (activeWorkerCoroutines[i] != null)
                {
                    StopCoroutine(activeWorkerCoroutines[i]);
                }
            }
            activeWorkerCoroutines.Clear();
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
        }

        private void EndTelemetryCollectionWindow()
        {
            if (currentCollectionWindow == null) return;

            SendTelemetryData(currentCollectionWindow.startTimestampISO);

            currentCollectionWindow.Dispose();
            currentCollectionWindow = null;
            timer = 0;
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
            if (!nativeGyro.GyroSupported)
                return;

            Vector3 rotationRate = nativeGyro.GyroRotationRate;
            double offsetCollection = Math.Round(currentCollectionWindow.Offset().TotalMilliseconds, 3, MidpointRounding.AwayFromZero);

            currentCollectionWindow.inputEvents.Add(new GyroEvent
            {
                timestampOffsetMs = offsetCollection,
                x = rotationRate.x,
                y = rotationRate.y,
                z = rotationRate.z,
            });

            currentInferenceWindow?.RecordGyro(rotationRate);
        }

        private void CollectAccelData()
        {
            if (!nativeAccelerometer.AccSupported)
                return;

            Vector3 acceleration = nativeAccelerometer.AccAcceleration;
            double offset = Math.Round(currentCollectionWindow.Offset().TotalMilliseconds, 3, MidpointRounding.AwayFromZero);

            currentCollectionWindow.inputEvents.Add(new AccelerometerEvent
            {
                timestampOffsetMs = offset,
                x = acceleration.x,
                y = acceleration.y,
                z = acceleration.z
            });

            currentInferenceWindow?.RecordAccel(acceleration);
        }

        private void CollectMagData()
        {
            if (!nativeMagnetometer.MagSupported)
                return;

            Vector3 rawMag = nativeMagnetometer.RawMag;
            double offset = Math.Round(currentCollectionWindow.Offset().TotalMilliseconds, 3, MidpointRounding.AwayFromZero);

            currentCollectionWindow.inputEvents.Add(new MagnetometerEvent
            {
                timestampOffsetMs = offset,
                x = rawMag.x,
                y = rawMag.y,
                z = rawMag.z,
            });

            currentInferenceWindow?.RecordMag(rawMag);
        }

        private void CollectTouchDataV2()
        {
            List<NativeTouch.TouchObject> touches = nativeTouch.Touches;
            if (touches.Count == 0)
                return;

            double offset = Math.Round(currentCollectionWindow.Offset().TotalMilliseconds, 3, MidpointRounding.AwayFromZero);

            for (int i = 0; i < touches.Count; i++)
            {
                NativeTouch.TouchObject touchObject = touches[i];
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
            }

            currentInferenceWindow?.RecordTouches(touches);
        }
    }
}