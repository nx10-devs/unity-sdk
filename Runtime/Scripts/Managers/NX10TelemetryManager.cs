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

        private void Awake()
        {
#if ENABLE_INPUT_SYSTEM
            if (Gyroscope.current != null)
                InputSystem.EnableDevice(Gyroscope.current);

            if (LinearAccelerationSensor.current != null)
                InputSystem.EnableDevice(LinearAccelerationSensor.current);

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
            if (dpi <= 0) dpi = 160f;

            return (pixels / dpi) * 25.4f;
        }
    }
}