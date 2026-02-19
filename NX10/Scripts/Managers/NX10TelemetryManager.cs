using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace NX10
{
    public class NX10TelemetryManager : MonoBehaviour
    {
        private bool canCollectTelemetryData;
        private NX10TelemetryWindow currentCollectionWindow;
        public float apiIntervalSeconds;
        private float timer = 0.0f;

        void Update()
        {
            UpdateTelemetryCollection();
        }

        private void UpdateTelemetryCollection()
        {
            if (!canCollectTelemetryData)
                return;

            timer += Time.deltaTime;
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            CollectTelemetryData(timestamp);

            if (timer > apiIntervalSeconds)
            {
                EndTelemetryCollectionWindow();
                StartTelemetryCollectionWindow();
            }
        }

        private void CollectTelemetryData(string timestamp)
        {
            if (SystemInfo.supportsGyroscope)
            {
                GyroEvent gyroEvent = new GyroEvent();
                gyroEvent.timestampOffsetMs = currentCollectionWindow.Offset().TotalMilliseconds;
                gyroEvent.x = Input.gyro.rotationRate.x;
                gyroEvent.y = Input.gyro.rotationRate.y;
                gyroEvent.z = Input.gyro.rotationRate.z;
                currentCollectionWindow.inputEvents.Add(gyroEvent);
            }

            if (SystemInfo.supportsAccelerometer)
            {
                AccelerometerEvent accelEvent = new AccelerometerEvent();
                accelEvent.timestampOffsetMs = currentCollectionWindow.Offset().TotalMilliseconds;
                accelEvent.x = Input.gyro.userAcceleration.x;
                accelEvent.y = Input.gyro.userAcceleration.y;
                accelEvent.z = Input.gyro.userAcceleration.z;
                currentCollectionWindow.inputEvents.Add(accelEvent);
            }

            foreach (var touch in Input.touches)
            {
                TouchInputEvent touchInputEvent = new TouchInputEvent();
                touchInputEvent.timestampOffsetMs = currentCollectionWindow.Offset().TotalMilliseconds;
                touchInputEvent.x = touch.position.x;
                touchInputEvent.y = touch.position.y;
                touchInputEvent.velocityX = touch.deltaPosition.x / Time.unscaledDeltaTime;
                touchInputEvent.velocityY = touch.deltaPosition.y / Time.unscaledDeltaTime;
                currentCollectionWindow.inputEvents.Add(touchInputEvent);
            }
        }

        public void SetTelemetryCollection(bool canCollect)
        {
            canCollectTelemetryData = canCollect;

            if (canCollect)
            {
                StartTelemetryCollectionWindow();
            }
            else
            {
                EndTelemetryCollectionWindow();
            }
        }

        private void StartTelemetryCollectionWindow()
        {
            currentCollectionWindow = new NX10TelemetryWindow()
            {
                startTimestamp = DateTime.UtcNow,
                inputEvents = new List<IInputEvent>()
            };
        }

        private void EndTelemetryCollectionWindow()
        {
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            SendTelemetryData(currentCollectionWindow.startTimestampISO);

            currentCollectionWindow.EndWindow();
            currentCollectionWindow = null;

            timer = timer - apiIntervalSeconds;
        }

        private void SendTelemetryData(string timestamp)
        {
            NX10Manager.Instance.SendTelemetryData(timestamp, currentCollectionWindow.Offset().TotalMilliseconds, currentCollectionWindow.inputEvents);
        }
    }
}

