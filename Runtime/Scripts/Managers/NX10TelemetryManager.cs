using System;
using System.Collections.Generic;
using UnityEngine;

#if (UNITY_EDITOR || (!UNITY_IOS && !UNITY_ANDROID)) && ENABLE_INPUT_SYSTEM
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
        private bool isPaused;
        private NX10TelemetryCollectionWindow currentCollectionWindow;
        private NX10TelemetryInferenceWindow currentInferenceWindow;
        private float timer = 0.0f;

        private readonly List<NativeSensorSample> _sensorDrainBuffer = new List<NativeSensorSample>(256);
        private readonly List<NativeTouch.TimestampedTouchSample> _touchDrainBuffer = new List<NativeTouch.TimestampedTouchSample>(256);

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

        private bool canCollectGyro => gyroHZ != null && gyroHZ.Value > 0;
        private bool canCollectAccelerometer => accelerometerHZ != null && accelerometerHZ.Value > 0;
        private bool canCollectTouch => touchHZ != null && touchHZ.Value > 0;
        private bool canOpenWindow => acquisitionWindowSize != null && acquisitionWindowSize.Value > 0;
        private bool canCollectMagnetometer => magnetometerHZ != null && magnetometerHZ.Value > 0;

        // Used by NX10DebugManager
        public bool CanCollectTelemetryData => canCollectTelemetryData;
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

#if UNITY_EDITOR || (!UNITY_IOS && !UNITY_ANDROID)
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
#endif
        }

        private void OnEnable()
        {
            isRunning = true;

            if (canCollectTelemetryData && !isPaused)
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
            StopSensorWorkers();
        }

        private void OnDestroy()
        {
            StopSensorWorkers();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus == isPaused)
                return;

            isPaused = pauseStatus;

            if (!canCollectTelemetryData)
                return;

            if (pauseStatus)
            {
                FlushPendingSensorSamples();
                StopSensorWorkers();
                EndTelemetryCollectionWindow();
            }
            else if (isRunning)
            {
                StartTelemetryCollectionWindow();
                StartSensorWorkers();
            }
        }

        private void Update()
        {
            if (!canCollectTelemetryData || isPaused)
                return;

            FlushPendingSensorSamples();

            if (currentCollectionWindow != null)
            {
                CheckAndCollectBrightnessData();
                CheckAndCollectOrientationData();
            }

            UpdateTelemetryCollectionWindow();

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

            if (nativeTouch != null)
            {
                nativeTouch.Initialise(nativeScale, dpi);
            }

            if (canCollectTelemetryData && isRunning && !isPaused)
            {
                StartSensorWorkers();
            }
        }

        /// <summary>
        /// Drains all sub-frame timestamped sensor samples collected by the native fixed-Hz background plugins
        /// into the active collection and inference windows. Each sensor channel is fault-isolated.
        /// </summary>
        public void FlushPendingSensorSamples()
        {
            if (!canCollectTelemetryData)
                return;

            if (canCollectGyro && nativeGyro != null && nativeGyro.IsSampling)
            {
                try
                {
                    _sensorDrainBuffer.Clear();
                    int count = nativeGyro.DrainSamples(_sensorDrainBuffer);
                    for (int i = 0; i < count; i++)
                    {
                        NativeSensorSample s = _sensorDrainBuffer[i];
                        if (currentCollectionWindow != null)
                        {
                            double offset = Math.Round(currentCollectionWindow.MonotonicOffsetMs(s.timestampMs), 3, MidpointRounding.AwayFromZero);
                            currentCollectionWindow.inputEvents.Add(new GyroEvent
                            {
                                timestampOffsetMs = offset,
                                x = s.x,
                                y = s.y,
                                z = s.z
                            });
                        }
                        currentInferenceWindow?.RecordGyro(new Vector3(s.x, s.y, s.z), s.timestampMs);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[NX10] Isolated error flushing Gyro samples: {ex.Message}");
                }
            }

            if (canCollectAccelerometer && nativeAccelerometer != null && nativeAccelerometer.IsSampling)
            {
                try
                {
                    _sensorDrainBuffer.Clear();
                    int count = nativeAccelerometer.DrainSamples(_sensorDrainBuffer);
                    for (int i = 0; i < count; i++)
                    {
                        NativeSensorSample s = _sensorDrainBuffer[i];
                        if (currentCollectionWindow != null)
                        {
                            double offset = Math.Round(currentCollectionWindow.MonotonicOffsetMs(s.timestampMs), 3, MidpointRounding.AwayFromZero);
                            currentCollectionWindow.inputEvents.Add(new AccelerometerEvent
                            {
                                timestampOffsetMs = offset,
                                x = s.x,
                                y = s.y,
                                z = s.z
                            });
                        }
                        currentInferenceWindow?.RecordAccel(new Vector3(s.x, s.y, s.z), s.timestampMs);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[NX10] Isolated error flushing Accelerometer samples: {ex.Message}");
                }
            }

            if (canCollectMagnetometer && nativeMagnetometer != null && nativeMagnetometer.IsSampling)
            {
                try
                {
                    _sensorDrainBuffer.Clear();
                    int count = nativeMagnetometer.DrainSamples(_sensorDrainBuffer);
                    for (int i = 0; i < count; i++)
                    {
                        NativeSensorSample s = _sensorDrainBuffer[i];
                        if (currentCollectionWindow != null)
                        {
                            double offset = Math.Round(currentCollectionWindow.MonotonicOffsetMs(s.timestampMs), 3, MidpointRounding.AwayFromZero);
                            currentCollectionWindow.inputEvents.Add(new MagnetometerEvent
                            {
                                timestampOffsetMs = offset,
                                x = s.x,
                                y = s.y,
                                z = s.z
                            });
                        }
                        currentInferenceWindow?.RecordMag(new Vector3(s.x, s.y, s.z), s.timestampMs);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[NX10] Isolated error flushing Magnetometer samples: {ex.Message}");
                }
            }

            if (canCollectTouch && nativeTouch != null && nativeTouch.IsSampling)
            {
                try
                {
                    _touchDrainBuffer.Clear();
                    int count = nativeTouch.DrainSamples(_touchDrainBuffer);
                    for (int i = 0; i < count; i++)
                    {
                        NativeTouch.TimestampedTouchSample item = _touchDrainBuffer[i];
                        NativeTouch.TouchObject touchObj = item.touch;
                        if (currentCollectionWindow != null)
                        {
                            double offset = Math.Round(currentCollectionWindow.MonotonicOffsetMs(item.timestampMs), 3, MidpointRounding.AwayFromZero);
                            currentCollectionWindow.inputEvents.Add(new TouchInputEventV2
                            {
                                timestampOffsetMs = offset,
                                touchId = touchObj.touchId,
                                touchType = touchObj.touchType,
                                touchObject = null,
                                xMm = touchObj.x,
                                yMm = touchObj.y,
                                touchRadiusMm = touchObj.touchRadius
                            });
                        }
                        currentInferenceWindow?.RecordTouchSample(touchObj, item.touchIndex, item.timestampMs);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[NX10] Isolated error flushing Touch samples: {ex.Message}");
                }
            }
        }

        private void UpdateTelemetryCollectionWindow()
        {
            if (!canOpenWindow || !canCollectTelemetryData || currentCollectionWindow == null)
                return;

            timer += Time.unscaledDeltaTime;

            if (timer >= acquisitionWindowSize.Value)
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

            if (lastRecordedBrightness < 0f || Mathf.Abs(currentBrightness - lastRecordedBrightness) >= screenBrightnessDelta.Value)
            {
                double offset = Math.Round(currentCollectionWindow.MonotonicOffsetMs(NativeSensorTime.NowMs()), 3, MidpointRounding.AwayFromZero);

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
                double offset = Math.Round(currentCollectionWindow.MonotonicOffsetMs(NativeSensorTime.NowMs()), 3, MidpointRounding.AwayFromZero);

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
            if (!NX10Manager.Instance.IsSessionValid && canCollect)
            {
                Debug.LogError("NX10 Manager not initialised, ensure it is before starting a collection window");
                return;
            }

            if (!canOpenWindow)
                return;

            if (canCollect)
            {
                canCollectTelemetryData = true;
                StartTelemetryInferenceWindow();
                StartTelemetryCollectionWindow();
                if (!isPaused)
                {
                    StartSensorWorkers();
                }
            }
            else
            {
                FlushPendingSensorSamples();
                canCollectTelemetryData = false;
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

            currentInferenceWindow = new NX10TelemetryInferenceWindow(NativeSensorTime.NowMs())
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

            if (canCollectGyro && nativeGyro != null)
                nativeGyro.StartSampling(gyroHZ.Value);

            if (canCollectAccelerometer && nativeAccelerometer != null)
                nativeAccelerometer.StartSampling(accelerometerHZ.Value);

            if (canCollectMagnetometer && nativeMagnetometer != null)
                nativeMagnetometer.StartSampling(magnetometerHZ.Value);

            if (canCollectTouch && nativeTouch != null)
                nativeTouch.StartSampling(touchHZ.Value);
        }

        private void StopSensorWorkers()
        {
            if (nativeGyro != null)
                nativeGyro.StopSampling();

            if (nativeAccelerometer != null)
                nativeAccelerometer.StopSampling();

            if (nativeMagnetometer != null)
                nativeMagnetometer.StopSampling();

            if (nativeTouch != null)
                nativeTouch.StopSampling();
        }

        private void StartTelemetryCollectionWindow()
        {
            if (currentCollectionWindow != null)
            {
                FlushPendingSensorSamples();
                EndTelemetryCollectionWindow();
            }

            timer = 0f;
            currentCollectionWindow = new NX10TelemetryCollectionWindow(NativeSensorTime.NowMs())
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
            timer = 0f;
        }

        private void SendTelemetryData(string timestamp)
        {
            if (!NX10Manager.Instance.IsSessionValid)
                return;

            List<IInputEvent> events = currentCollectionWindow.inputEvents;
            if (events.Count >= 6)
            {
                events.Sort((a, b) => a.timestampOffsetMs.CompareTo(b.timestampOffsetMs));
                double windowEndMs = Math.Max(
                    currentCollectionWindow.MonotonicOffsetMs(NativeSensorTime.NowMs()),
                    events[events.Count - 1].timestampOffsetMs);

                sendTelemetryDataRequest?.Invoke(timestamp, windowEndMs, events);
            }
        }
    }
}