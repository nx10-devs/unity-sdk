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
        private const float pointToMmScaleFactor = 0.15875f;

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


        private bool guiMenuToggle = false;

        private float _holdTimer = 0f;
        private const float TargetHoldTime = 3f;
        private bool _hasTriggered = false;

        private void Awake()
        {
#if UNITY_EDITOR
            guiMenuToggle = true;
#endif

#if ENABLE_INPUT_SYSTEM
            if (Gyroscope.current != null)
                InputSystem.EnableDevice(Gyroscope.current);

            if (LinearAccelerationSensor.current != null)
                InputSystem.EnableDevice(LinearAccelerationSensor.current);

            EnhancedTouchSupport.Enable();

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
            UpdateDebugToggle();
        }

        private void UpdateDebugToggle()
        {
            if (Touchscreen.current == null) return;

            int activeTouches = 0;

            foreach (var touch in Touchscreen.current.touches)
            {
                if (touch.press.isPressed)
                {
                    activeTouches++;
                }
            }

            if (activeTouches == 3)
            {
                if (!_hasTriggered)
                {
                    _holdTimer += Time.deltaTime;

                    if (_holdTimer >= TargetHoldTime)
                    {
                        guiMenuToggle = !guiMenuToggle; 

                        _hasTriggered = true; 
                    }
                }
            }
            else
            {
                // Reset the timer and trigger state if they lift a finger or add a 4th
                _holdTimer = 0f;
                _hasTriggered = false;
            }
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
            double offset = currentCollectionWindow.Offset().TotalMilliseconds;
#if ENABLE_INPUT_SYSTEM
            if (Gyroscope.current != null)
            {
                var gyro = Gyroscope.current.angularVelocity.ReadValue();
                currentCollectionWindow.inputEvents.Add(new GyroEvent
                {
                    timestampOffsetMs = offset,
                    x = gyro.x,
                    y = gyro.y,
                    z = gyro.z
                });
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (SystemInfo.supportsGyroscope)
        {
            currentCollectionWindow.inputEvents.Add(new GyroEvent {
                timestampOffsetMs = offset, x = Input.gyro.rotationRate.x, y = Input.gyro.rotationRate.y, z = Input.gyro.rotationRate.z
            });
        }
#endif
        }

        private void CollectAccelData()
        {
            double offset = currentCollectionWindow.Offset().TotalMilliseconds;
#if ENABLE_INPUT_SYSTEM
            if (LinearAccelerationSensor.current != null)
            {
                var accel = LinearAccelerationSensor.current.acceleration.ReadValue();
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
            currentCollectionWindow.inputEvents.Add(new AccelerometerEvent {
                timestampOffsetMs = offset, x = Input.gyro.userAcceleration.x, y = Input.gyro.userAcceleration.y, z = Input.gyro.userAcceleration.z
            });
        }
#endif
        }

        private void CollectTouchDataV2()
        {
            double offset = currentCollectionWindow.Offset().TotalMilliseconds;
#if ENABLE_INPUT_SYSTEM
            foreach (var touch in Touch.activeTouches)
            {
                currentCollectionWindow.inputEvents.Add(new TouchInputEventV2
                {
                    timestampOffsetMs = offset,
                    touchId = touch.touchId.ToString(),
                    touchType = ConvertTouchPhaseToTouchType(touch.phase),
                    touchObject = null,
                    xMm = PixelsToMillimeters(touch.screenPosition.x),
                    yMm = PixelsToMillimeters(touch.screenPosition.y),
                    touchRadiusMm = PixelsToMillimeters(touch.radius.x)//touch.radius,
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

        public float PixelsToMillimeters(float pixels)
        {
            float inches = pixels / dpi;
            float millimeters = inches * 25.4f;
            return (float)Math.Round(millimeters, 3, MidpointRounding.AwayFromZero);
        }

        private float lastSensorUpdateTime = -999f;
        private string cachedAccelText = "Loading...";
        private string cachedGyroText = "Loading...";
        private void OnGUI()
        {
            if (!guiMenuToggle)
                return;

            // 1. Calculate dynamic layout based on screen size (Half Screen Width)
            float padding = 20f;
            float boxWidth = Screen.width * 0.5f;
            float boxHeight = Screen.height - (padding * 2);

            // 2. Setup a custom, high-visibility GUIStyle for text and boxes
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 60; // Increased text size
            labelStyle.richText = true; // Allows <b> tags to work

            GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.fontSize = 35;
            boxStyle.fontStyle = FontStyle.Bold;

            if (!canCollectTelemetryData || currentCollectionWindow == null)
            {
                GUI.Box(new Rect(padding, padding, boxWidth, 50), "Telemetry: Not Collecting", boxStyle);
                return;
            }

            // Draw main container taking up half the screen
            GUI.Box(new Rect(padding, padding, boxWidth, boxHeight), $"Telemetry Active ({touchHZ}Hz)", boxStyle);

            // Position content area slightly inside the box container
            GUILayout.BeginArea(new Rect(padding + 15, padding + 45, boxWidth - 30, boxHeight - 60));

            GUILayout.Label($"Window Start: {currentCollectionWindow.startTimestampISO}", labelStyle);
            GUILayout.Label($"Events Recorded: {currentCollectionWindow.inputEvents.Count}", labelStyle);
            GUILayout.Label($"DPI: {dpi}", labelStyle);
            GUILayout.Label($"Timer: {timer:F2}s / {acquisitionWindowSize}s", labelStyle);

            GUILayout.Space(10);
            GUILayout.Label("<b>Sensors (Updates every 2s):</b>", labelStyle);

            // 3. Throttle sensor reading updates to every 2 seconds
            if (Time.time - lastSensorUpdateTime >= 2f)
            {
                lastSensorUpdateTime = Time.time;

#if ENABLE_INPUT_SYSTEM
                if (UnityEngine.InputSystem.LinearAccelerationSensor.current != null)
                {
                    var accel = UnityEngine.InputSystem.LinearAccelerationSensor.current.acceleration.ReadValue();
                    cachedAccelText = $"  Accel: {accel.x:F2}, {accel.y:F2}, {accel.z:F2} m/s²";
                }
                else
                {
                    cachedAccelText = "  Accel: Not Detected";
                }

                if (UnityEngine.InputSystem.Gyroscope.current != null)
                {
                    var gyro = UnityEngine.InputSystem.Gyroscope.current.angularVelocity.ReadValue();
                    cachedGyroText = $"  Gyro:  {gyro.x:F2}, {gyro.y:F2}, {gyro.z:F2} rad/s";
                }
                else
                {
                    cachedGyroText = "  Gyro: Not Detected";
                }
#else
        Vector3 accel = Input.acceleration;
        cachedAccelText = $"  Accel: {accel.x:F2}, {accel.y:F2}, {accel.z:F2} G";

        if (SystemInfo.supportsGyroscope)
        {
            Vector3 gyro = Input.gyro.rotationRate;
            cachedGyroText = $"  Gyro:  {gyro.x:F2}, {gyro.y:F2}, {gyro.z:F2} rad/s";
        }
        else
        {
            cachedGyroText = "  Gyro: Not Supported";
        }
#endif
            }

            // Display the cached sensor text
            GUILayout.Label(cachedAccelText, labelStyle);
            GUILayout.Label(cachedGyroText, labelStyle);

            GUILayout.Space(15);
            GUILayout.Label("<b>Active Touches (Raw -> mm):</b>", labelStyle);

#if ENABLE_INPUT_SYSTEM
            foreach (var touch in UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches)
            {
                float xMm = PixelsToMillimeters(touch.screenPosition.x);
                float yMm = PixelsToMillimeters(touch.screenPosition.y);
                GUILayout.Label($"ID {touch.touchId}: {xMm:F1}mm, {yMm:F1}mm ({touch.phase})", labelStyle);
            }
#else
    foreach (var touch in Input.touches)
    {
        float xMm = PixelsToMillimeters(touch.position.x);
        float yMm = PixelsToMillimeters(touch.position.y);
        GUILayout.Label($"ID {touch.fingerId}: {xMm:F1}mm, {yMm:F1}mm ({touch.phase})", labelStyle);
    }
#endif
            GUILayout.EndArea();
        }
    }
}