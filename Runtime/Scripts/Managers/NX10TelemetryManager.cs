using System;
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
        private NX10TelemetryWindow currentCollectionWindow;
        public float apiIntervalSeconds;
        private float timer = 0.0f;

        public Action<string, double, List<IInputEvent>> sendTelemetryDataRequest;

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

        private void Update()
        {
            UpdateTelemetryCollection();
        }

        private void UpdateTelemetryCollection()
        {
            if (!canCollectTelemetryData || currentCollectionWindow == null)
                return;

            timer += Time.deltaTime;
            CollectTelemetryData();

            if (timer > apiIntervalSeconds)
            {
                StartTelemetryCollectionWindow();
            }
        }

        private void CollectTelemetryData()
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

            foreach (var touch in Touch.activeTouches)
            {
                currentCollectionWindow.inputEvents.Add(new TouchInputEvent
                    {
                        timestampOffsetMs = offset,
                        x = touch.screenPosition.x,
                        y = touch.screenPosition.y,
                        velocityX = touch.delta.x / Time.unscaledDeltaTime,
                        velocityY = touch.delta.y / Time.unscaledDeltaTime,
                    });
            }

            return;
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (SystemInfo.supportsGyroscope)
            {
                currentCollectionWindow.inputEvents.Add(new GyroEvent {
                    timestampOffsetMs = offset,
                    x = Input.gyro.rotationRate.x,
                    y = Input.gyro.rotationRate.y,
                    z = Input.gyro.rotationRate.z
                });

                currentCollectionWindow.inputEvents.Add(new AccelerometerEvent {
                    timestampOffsetMs = offset,
                    x = Input.gyro.userAcceleration.x,
                    y = Input.gyro.userAcceleration.y,
                    z = Input.gyro.userAcceleration.z
                });
            }

            foreach (var touch in Input.touches)
            {
                currentCollectionWindow.inputEvents.Add(new TouchInputEvent {
                    timestampOffsetMs = offset,
                    x = touch.position.x,
                    y = touch.position.y,
                    velocityX = touch.deltaPosition.x / Time.unscaledDeltaTime,
                    velocityY = touch.deltaPosition.y / Time.unscaledDeltaTime
                });
            }
#endif
        }

        public void SetTelemetryCollection(bool canCollect)
        {
            if(!NX10Manager.Instance.Initialised)
            {
                Debug.LogError("NX10 Manager not initialised, ensure it is before starting a collection window");
                return;
            }

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
        }

        private void EndTelemetryCollectionWindow()
        {
            if (currentCollectionWindow == null) return;

            SendTelemetryData(currentCollectionWindow.startTimestampISO);
            currentCollectionWindow.Dispose();
            currentCollectionWindow = null;
            timer -= apiIntervalSeconds;
        }

        private void SendTelemetryData(string timestamp)
        {
            sendTelemetryDataRequest?.Invoke(timestamp, currentCollectionWindow.Offset().TotalMilliseconds, currentCollectionWindow.inputEvents);
        }
    }
}