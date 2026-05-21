using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;

namespace NX10
{
    public class NX10DebugManager : MonoBehaviour
    {
        private float lastSensorUpdateTime = -999f;
        private string cachedAccelText = "Loading...";
        private string cachedGyroText = "Loading...";
        private float lastApiUpdateTime = -10f; 
        private string cachedActivityText = "Activity: Fetching...";
        private string cachedAffectText = "Affect: Fetching...";

        private bool guiMenuToggle = false;

        private float _holdTimer = 0f;
        private const float TargetHoldTime = 3f;
        private bool _hasTriggered = false;

        private NX10TelemetryManager _telemetryManager;

        private bool canCollectTelemetryData => _telemetryManager.CanCollectTelemetryData;
        private NX10TelemetryWindow currentCollectionWindow => _telemetryManager.CurrentCollectionWindow;
        private float timer => _telemetryManager.Timer;

        public int? touchHZ => _telemetryManager.TouchHz;
        public int? acquisitionWindowSize => _telemetryManager.AcquisitionWindowSize;
        private float dpi => _telemetryManager.Dpi;

        private bool initialised = false;

        private void Awake()
        {
#if UNITY_EDITOR
            guiMenuToggle = true;
#endif
        }

        public void Initialise(NX10TelemetryManager telemetryManager)
        {
            _telemetryManager = telemetryManager;
            initialised = true;
        }

        private void Update()
        {
            if (!initialised)
                return;

            UpdateDebugToggle();
            UpdateApiCalls();
        }

        private void UpdateApiCalls()
        {
            if (!guiMenuToggle) return;

            if (Time.time - lastApiUpdateTime >= 10f)
            {
                lastApiUpdateTime = Time.time;

                NX10Manager.Instance.RequestActivity((state) =>
                {
                    cachedActivityText = $"Activity: {state}";
                });

                NX10Manager.Instance.RequestAffect((affect, confidence) =>
                {
                    cachedAffectText = $"Affect: {affect} ({confidence})"; 
                });
            }
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
                _holdTimer = 0f;
                _hasTriggered = false;
            }
        }

        private void OnGUI()
        {
            if (!guiMenuToggle)
                return;

            if (!initialised)
                return;

            float padding = 20f;
            float boxWidth = Screen.width * 0.5f;
            float boxHeight = Screen.height - (padding * 2);

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 60; 
            labelStyle.richText = true; 

            GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.fontSize = 35;
            boxStyle.fontStyle = FontStyle.Bold;

            if (!canCollectTelemetryData || currentCollectionWindow == null)
            {
                GUI.Box(new Rect(padding, padding, boxWidth, 50), "Telemetry: Not Collecting", boxStyle);
                return;
            }

            GUI.Box(new Rect(padding, padding, boxWidth, boxHeight), $"Telemetry Active ({touchHZ}Hz)", boxStyle);

            GUILayout.BeginArea(new Rect(padding + 15, padding + 45, boxWidth - 30, boxHeight - 60));

            GUILayout.Label($"Window Start: {currentCollectionWindow.startTimestampISO}", labelStyle);
            GUILayout.Label($"Events Recorded: {currentCollectionWindow.inputEvents.Count}", labelStyle);
            GUILayout.Label($"DPI: {dpi}", labelStyle);
            GUILayout.Label($"Timer: {timer:F2}s / {acquisitionWindowSize}s", labelStyle);

            GUILayout.Space(10);
            GUILayout.Label("<b>Sensors (Updates every 2s):</b>", labelStyle);

            if (Time.time - lastSensorUpdateTime >= 2f)
            {
                lastSensorUpdateTime = Time.time;

#if ENABLE_INPUT_SYSTEM
                if (Accelerometer.current != null)
                {
                    var accel = _telemetryManager.MapScreenAccelerometerWithoutOrientation(Accelerometer.current.acceleration.ReadValue());
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

            GUILayout.Label(cachedAccelText, labelStyle);
            GUILayout.Label(cachedGyroText, labelStyle);

            GUILayout.Space(10);
            GUILayout.Label("<b>NX10 Status (Updates every 10s):</b>", labelStyle);
            GUILayout.Label($"  {cachedActivityText}", labelStyle);
            GUILayout.Label($"  {cachedAffectText}", labelStyle);

            GUILayout.Space(15);
            GUILayout.Label("<b>Active Touches (Raw -> mm):</b>", labelStyle);

#if ENABLE_INPUT_SYSTEM
            foreach (var touch in UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches)
            {
                float xMm = _telemetryManager.PixelsToMillimeters(touch.screenPosition.x);
                float yMm = _telemetryManager.PixelsToMillimeters(touch.screenPosition.y);
                float touchRadius = _telemetryManager.PixelsToMillimeters(touch.radius.x);
                GUILayout.Label($"ID {touch.touchId}: {xMm:F1}mm, {yMm:F1}mm (R: {touchRadius:F1}mm) ({touch.phase})", labelStyle);
            }
#else
    foreach (var touch in Input.touches)
    {
        float xMm = _telemetryManager.PixelsToMillimeters(touch.position.x);
        float yMm = _telemetryManager.PixelsToMillimeters(touch.position.y);
        GUILayout.Label($"ID {touch.fingerId}: {xMm:F1}mm, {yMm:F1}mm ({touch.phase})", labelStyle);
    }
#endif
            GUILayout.EndArea();
        }
    }
}
