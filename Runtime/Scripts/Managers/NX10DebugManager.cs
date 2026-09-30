//#if UNITY_EDITOR || DEBUG

using System;
using System.Collections.Generic;
using UnityEngine;


//using UnityEngine.InputSystem;
//using UnityEngine.InputSystem.EnhancedTouch;

namespace NX10
{
    public class NX10DebugManager : MonoBehaviour
    {
        private float lastSensorUpdateTime = -999f;
        private string cachedAccelText = "Loading...";
        private string cachedGyroText = "Loading...";
        private string cachedMagText = "Loading...";
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
            //UpdateApiCalls();
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
            int activeTouches = 0;

#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current == null) return;

            foreach (var touch in Touchscreen.current.touches)
            {
                if (touch.press.isPressed)
                {
                    activeTouches++;
                }
            }
#else
            foreach(var touch in Input.touches)
            {
                activeTouches++;
            }
#endif

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
            boxStyle.fontSize = 25;
            boxStyle.fontStyle = FontStyle.Bold;

            if (!canCollectTelemetryData || currentCollectionWindow == null)
            {
                GUI.Box(new Rect(padding, padding, boxWidth, 50), "Telemetry: Not Collecting", boxStyle);
                return;
            }

            GUI.Box(new Rect(padding, padding, boxWidth, boxHeight), $"Telemetry Active ({touchHZ}Hz)", boxStyle);

            GUILayout.BeginArea(new Rect(padding + 15, padding + 45, boxWidth - 30, boxHeight - 60));

            GUILayout.Label($"DPI: {dpi}", labelStyle);
#if UNITY_IOS && !UNITY_EDITOR
            GUILayout.Label($"NativeScale: {_telemetryManager.nativeScale.GetNativeScale()}", labelStyle);
#endif
            GUILayout.Label($"Timer: {timer:F2}s / {acquisitionWindowSize}s", labelStyle);

            GUILayout.Space(10);
            GUILayout.Label("<b>Sensors (Updates every 2s):</b>", labelStyle);

            if (Time.time - lastSensorUpdateTime >= 2f)
            {
                lastSensorUpdateTime = Time.time;

#if ENABLE_INPUT_SYSTEM
                if (Accelerometer.current != null)
                {
                    var accel = _telemetryManager.ConvertAccelerometerData(Accelerometer.current.acceleration.ReadValue());
                    cachedAccelText = $"  Accel: {accel.x}, {accel.y}, {accel.z} m/s²";
                }
                else
                {
                    cachedAccelText = "  Accel: Not Detected";
                }

                if (UnityEngine.InputSystem.Gyroscope.current != null)
                {
                    cachedGyroText = $"  Gyro:  {_telemetryManager.nativeGyro.rotationRateUnbiased.x}, {_telemetryManager.nativeGyro.rotationRateUnbiased.y}, {_telemetryManager.nativeGyro.rotationRateUnbiased.z} rad/s";
                }
                else
                {
                    cachedGyroText = "  Gyro: Not Detected";
                }

               
                    var rawMag = Vector3.zero;
#if UNITY_IOS && !UNITY_EDITOR
    if (IOSMagnetometer.IsAvailable())
    {
        IOSMagnetometer.Start(); 
        rawMag = IOSMagnetometer.GetRawData();
    }
#elif ENABLE_INPUT_SYSTEM
                    if (MagneticFieldSensor.current != null)
                    {
                        rawMag = MagneticFieldSensor.current.magneticField.ReadValue();
                    }
#elif ENABLE_LEGACY_INPUT_MANAGER
            rawMag = Input.compass.rawVector;
#endif
                    cachedMagText = $"  Mag:  {rawMag.x}, {rawMag.y}, {rawMag.z} rad/s";
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

                GUILayout.Label(cachedAccelText, labelStyle);
                GUILayout.Label(cachedGyroText, labelStyle);
                GUILayout.Label(cachedMagText, labelStyle);

                GUILayout.Space(15);
                GUILayout.Label("<b>Active Touches (Raw -> mm):</b>", labelStyle);

#if ENABLE_INPUT_SYSTEM
            foreach (var touch in UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches)
            {
                double xMm = _telemetryManager.PixelsToMillimeters(touch.screenPosition.x);
                double yMm = _telemetryManager.PixelsToMillimeters(touch.screenPosition.y);
                double majorRadius = Mathf.Max(touch.radius.x, touch.radius.y);
                double radiusMm = _telemetryManager.PixelsToMillimeters(majorRadius);
#if UNITY_IOS && !UNITY_EDITOR
                radiusMm = _telemetryManager.MmPerPoint() * majorRadius;
                radiusMm = Math.Round(radiusMm, 3, MidpointRounding.AwayFromZero);

#elif UNITY_ANDROID// && !UNITY_EDITOR
                if(majorRadius <= 1)
                majorRadius *= Mathf.Min(Screen.width, Screen.height);
                radiusMm = majorRadius;
                radiusMm = Math.Round(radiusMm, 4, MidpointRounding.AwayFromZero);
#endif
                GUILayout.Label($"ID {touch.touchId}: {xMm}mm, {yMm}mm  (R: {touch.radius.x + "," + touch.radius.y} RAW ScreenSpace) (R: {radiusMm}mm) ({touch.phase})", labelStyle);
            }
#else
                foreach (var touch in Input.touches)
                {
                    double xMm = _telemetryManager.PixelsToMillimeters(touch.position.x);
                    double yMm = _telemetryManager.PixelsToMillimeters(touch.position.y);
                    GUILayout.Label($"ID {touch.fingerId}: {xMm:F1}mm, {yMm:F1}mm ({touch.phase})", labelStyle);
                }
#endif
                GUILayout.EndArea();
            }
        }
    }
}
//#endif
