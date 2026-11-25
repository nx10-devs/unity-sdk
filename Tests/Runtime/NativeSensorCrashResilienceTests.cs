using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NX10.Tests
{
    public class NativeSensorCrashResilienceTests
    {
        #region Fault-Injecting Mock Backends

        private sealed class FaultInjectingImuBackend : INativeImuBackend
        {
            public readonly Queue<NativeSensorSample> QueuedSamples = new Queue<NativeSensorSample>();
            public bool Supported = true;
            public Vector3 LatestValue = new Vector3(0.1f, 0.2f, 0.3f);

            public Exception ThrowOnIsSupported;
            public Exception ThrowOnStart;
            public Exception ThrowOnStop;
            public Exception ThrowOnDrain;
            public Exception ThrowOnGetLatest;

            public int SucceedDrainsBeforeThrowing = -1;
            public int? ForcedDrainReturnCount;
            public bool InfiniteStreamMode;

            public int IsSupportedCalls { get; private set; }
            public int StartCalls { get; private set; }
            public int StopCalls { get; private set; }
            public int DrainCalls { get; private set; }
            public int GetLatestCalls { get; private set; }
            public int LastStartedHz { get; private set; }

            public bool IsSupported()
            {
                IsSupportedCalls++;
                if (ThrowOnIsSupported != null)
                    throw ThrowOnIsSupported;
                return Supported;
            }

            public void Start(int hz)
            {
                StartCalls++;
                LastStartedHz = hz;
                if (ThrowOnStart != null)
                    throw ThrowOnStart;
            }

            public void Stop()
            {
                StopCalls++;
                if (ThrowOnStop != null)
                    throw ThrowOnStop;
            }

            public int Drain(NativeSensorSample[] batchBuffer, int maxBatchCount)
            {
                DrainCalls++;
                if (SucceedDrainsBeforeThrowing >= 0)
                {
                    if (DrainCalls > SucceedDrainsBeforeThrowing && ThrowOnDrain != null)
                        throw ThrowOnDrain;
                }
                else if (ThrowOnDrain != null)
                {
                    throw ThrowOnDrain;
                }

                if (InfiniteStreamMode)
                {
                    int fill = Math.Min(batchBuffer.Length, maxBatchCount);
                    for (int i = 0; i < fill; i++)
                    {
                        batchBuffer[i] = new NativeSensorSample
                        {
                            timestampMs = 100.0 + i,
                            x = 1f,
                            y = 2f,
                            z = 3f
                        };
                    }
                    return maxBatchCount;
                }

                if (ForcedDrainReturnCount.HasValue)
                {
                    int n = Math.Min(batchBuffer.Length, Math.Max(0, ForcedDrainReturnCount.Value));
                    for (int i = 0; i < n; i++)
                    {
                        batchBuffer[i] = new NativeSensorSample
                        {
                            timestampMs = 50.0 + i,
                            x = 0.5f,
                            y = 0.6f,
                            z = 0.7f
                        };
                    }
                    return ForcedDrainReturnCount.Value;
                }

                int count = 0;
                int cap = Math.Min(batchBuffer.Length, maxBatchCount);
                while (count < cap && QueuedSamples.Count > 0)
                {
                    batchBuffer[count++] = QueuedSamples.Dequeue();
                }
                return count;
            }

            public bool TryGetLatest(out Vector3 value)
            {
                GetLatestCalls++;
                if (ThrowOnGetLatest != null)
                    throw ThrowOnGetLatest;
                value = LatestValue;
                return true;
            }
        }

        private sealed class FaultInjectingTouchBackend : INativeTouchBackend
        {
            public readonly Queue<NativeTouchSample> QueuedSamples = new Queue<NativeTouchSample>();
            public readonly List<NativeTouchSample> ActiveSamples = new List<NativeTouchSample>();

            public Exception ThrowOnStart;
            public Exception ThrowOnStop;
            public Exception ThrowOnDrain;
            public Exception ThrowOnGetActive;
            public int? ForcedDrainReturnCount;
            public bool InfiniteStreamMode;

            public int StartCalls { get; private set; }
            public int StopCalls { get; private set; }
            public int DrainCalls { get; private set; }
            public int GetActiveCalls { get; private set; }

            public void Start(int hz)
            {
                StartCalls++;
                if (ThrowOnStart != null)
                    throw ThrowOnStart;
            }

            public void Stop()
            {
                StopCalls++;
                if (ThrowOnStop != null)
                    throw ThrowOnStop;
            }

            public int Drain(NativeTouchSample[] batchBuffer, int maxBatchCount)
            {
                DrainCalls++;
                if (ThrowOnDrain != null)
                    throw ThrowOnDrain;

                if (InfiniteStreamMode)
                {
                    int fill = Math.Min(batchBuffer.Length, maxBatchCount);
                    for (int i = 0; i < fill; i++)
                    {
                        batchBuffer[i] = new NativeTouchSample
                        {
                            timestampMs = 100.0 + i,
                            screenX = 100f,
                            screenY = 200f,
                            radius = 5f,
                            touchId = 1,
                            phaseCode = 1,
                            touchIndex = 0
                        };
                    }
                    return maxBatchCount;
                }

                if (ForcedDrainReturnCount.HasValue)
                {
                    int n = Math.Min(batchBuffer.Length, Math.Max(0, ForcedDrainReturnCount.Value));
                    for (int i = 0; i < n; i++)
                    {
                        batchBuffer[i] = new NativeTouchSample
                        {
                            timestampMs = 50.0 + i,
                            screenX = 50f,
                            screenY = 60f,
                            radius = 4f,
                            touchId = 1,
                            phaseCode = 0,
                            touchIndex = 0
                        };
                    }
                    return ForcedDrainReturnCount.Value;
                }

                int count = 0;
                int cap = Math.Min(batchBuffer.Length, maxBatchCount);
                while (count < cap && QueuedSamples.Count > 0)
                {
                    batchBuffer[count++] = QueuedSamples.Dequeue();
                }
                return count;
            }

            public int GetActiveTouches(NativeTouchSample[] batchBuffer, int maxBatchCount)
            {
                GetActiveCalls++;
                if (ThrowOnGetActive != null)
                    throw ThrowOnGetActive;

                int count = Math.Min(Math.Min(batchBuffer.Length, maxBatchCount), ActiveSamples.Count);
                for (int i = 0; i < count; i++)
                {
                    batchBuffer[i] = ActiveSamples[i];
                }
                return count;
            }
        }

        private sealed class FaultInjectingClockBackend : INativeClockBackend
        {
            public double CurrentMs = 1000.0;
            public Exception ThrowOnRead;
            public int Calls { get; private set; }

            public double GetMonotonicTimeMs()
            {
                Calls++;
                if (ThrowOnRead != null)
                    throw ThrowOnRead;
                return CurrentMs;
            }
        }

        #endregion

        private GameObject _testRoot;
        private NativeGyro _gyro;
        private NativeAccelerometer _accel;
        private NativeMagnetometer _mag;
        private NativeTouch _touch;
        private NativeScale _scale;
        private NX10TelemetryManager _telemetry;

        [SetUp]
        public void SetUp()
        {
            NativeSensorTime.SetBackendForTesting(null);

            _testRoot = new GameObject("NX10_CrashResilienceTestRig");
            _gyro = _testRoot.AddComponent<NativeGyro>();
            _accel = _testRoot.AddComponent<NativeAccelerometer>();
            _mag = _testRoot.AddComponent<NativeMagnetometer>();
            _scale = _testRoot.AddComponent<NativeScale>();
            _touch = _testRoot.AddComponent<NativeTouch>();
            _telemetry = _testRoot.AddComponent<NX10TelemetryManager>();

            // Ensure Awake wiring runs even in EditMode tests where Unity doesn't auto-invoke Awake
            InvokePrivateIfNeeded(_telemetry, "Awake");
            _touch.Initialise(_scale, 326f);
        }

        [TearDown]
        public void TearDown()
        {
            NativeSensorTime.SetBackendForTesting(null);
            if (_testRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(_testRoot);
            }
        }

        private static void InvokePrivateIfNeeded(object target, string methodName, params object[] args)
        {
            MethodInfo mi = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            mi?.Invoke(target, args);
        }

        private static void SetPrivateField<T>(object target, string fieldName, T value)
        {
            FieldInfo fi = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(fi, $"Expected field '{fieldName}' on {target.GetType().Name}");
            fi.SetValue(target, value);
        }

        private void ActivateTelemetryWindowsForTesting(double startMonoMs)
        {
            SetPrivateField(_telemetry, "canCollectTelemetryData", true);
            SetPrivateField(_telemetry, "isRunning", true);
            SetPrivateField(_telemetry, "currentCollectionWindow", new NX10TelemetryCollectionWindow(startMonoMs)
            {
                startTimestamp = DateTime.UtcNow
            });
            SetPrivateField(_telemetry, "currentInferenceWindow", new NX10TelemetryInferenceWindow(startMonoMs)
            {
                startTimestamp = DateTime.UtcNow
            });
        }

        [Test]
        public void DllNotFound_TripsCircuitBreakerImmediately_WithoutSpammingSubsequentFrames()
        {
            LogAssert.ignoreFailingMessages = false;

            var brokenGyro = new FaultInjectingImuBackend { ThrowOnStart = new DllNotFoundException("__Internal missing") };
            var brokenAccel = new FaultInjectingImuBackend { ThrowOnIsSupported = new DllNotFoundException("libnx10 missing") };
            var brokenMag = new FaultInjectingImuBackend { ThrowOnDrain = new DllNotFoundException("mag plugin missing") };
            var brokenTouch = new FaultInjectingTouchBackend { ThrowOnStart = new DllNotFoundException("touch plugin missing") };

            _gyro.SetBackendForTesting(brokenGyro);
            _accel.SetBackendForTesting(brokenAccel);
            _mag.SetBackendForTesting(brokenMag);
            _touch.SetBackendForTesting(brokenTouch);

            Assert.DoesNotThrow(() =>
            {
                _gyro.StartSampling(100);
                _accel.StartSampling(100);
                _mag.StartSampling(50);
                _touch.StartSampling(120);
            });

            var sensorScratch = new List<NativeSensorSample>();
            var touchScratch = new List<NativeTouch.TimestampedTouchSample>();

            // Simulate 60 consecutive game frames — circuit breakers must stay tripped and NOT call broken native methods 60 times
            for (int frame = 0; frame < 60; frame++)
            {
                Assert.DoesNotThrow(() =>
                {
                    _gyro.DrainSamples(sensorScratch);
                    _accel.DrainSamples(sensorScratch);
                    _mag.DrainSamples(sensorScratch);
                    _touch.DrainSamples(touchScratch);
                    _ = _gyro.GyroRotationRate;
                    _ = _accel.AccAcceleration;
                    _ = _mag.RawMag;
                    _ = _touch.Touches;
                });
            }

            Assert.IsTrue(_gyro.IsNativeFaulted, "Gyro circuit breaker should be tripped");
            Assert.IsTrue(_accel.IsNativeFaulted, "Accel circuit breaker should be tripped");
            Assert.IsTrue(_mag.IsNativeFaulted, "Mag circuit breaker should be tripped");
            Assert.IsTrue(_touch.IsNativeFaulted, "Touch circuit breaker should be tripped");

            Assert.AreEqual(1, brokenGyro.StartCalls, "Broken native Gyro Start must only be attempted once");
            Assert.AreEqual(1, brokenAccel.IsSupportedCalls, "Broken native Accel IsSupported must only be attempted once");
            Assert.AreEqual(1, brokenMag.DrainCalls, "Broken native Mag Drain must only be attempted once");
            Assert.AreEqual(1, brokenTouch.StartCalls, "Broken native Touch Start must only be attempted once");

            Assert.DoesNotThrow(() =>
            {
                _gyro.StopSampling();
                _accel.StopSampling();
                _mag.StopSampling();
                _touch.StopSampling();
            });
        }

        [Test]
        public void EntryPointNotFound_OnStartStopAndGetters_NeverThrowsToCaller()
        {
            var brokenGyro = new FaultInjectingImuBackend
            {
                ThrowOnGetLatest = new EntryPointNotFoundException("_NX10GetLatestGyro missing"),
                ThrowOnStop = new EntryPointNotFoundException("_NX10StopGyro missing")
            };
            var brokenTouch = new FaultInjectingTouchBackend
            {
                ThrowOnGetActive = new EntryPointNotFoundException("_NX10GetActiveTouches missing"),
                ThrowOnStop = new EntryPointNotFoundException("_NX10StopTouch missing")
            };

            _gyro.SetBackendForTesting(brokenGyro);
            _touch.SetBackendForTesting(brokenTouch);

            Assert.DoesNotThrow(() =>
            {
                _gyro.StartSampling(60);
                _touch.StartSampling(60);

                Vector3 rot = _gyro.GyroRotationRate;
                Assert.AreEqual(Vector3.zero, rot);

                List<NativeTouch.TouchObject> active = _touch.Touches;
                Assert.IsNotNull(active);

                _gyro.StopSampling();
                _touch.StopSampling();
            });

            Assert.IsTrue(_gyro.IsNativeFaulted);
            Assert.IsTrue(_touch.IsNativeFaulted);
        }

        [Test]
        public void MidSessionNativeCrash_PreservesPreCrashSamples_AndIsolatesFaultySensorFromHealthySensors()
        {
            var clock = new FaultInjectingClockBackend { CurrentMs = 1000.0 };
            NativeSensorTime.SetBackendForTesting(clock);

            var crashingGyro = new FaultInjectingImuBackend
            {
                SucceedDrainsBeforeThrowing = 1,
                ThrowOnDrain = new SEHException("Simulated native access violation / JNI exception")
            };
            var healthyAccel = new FaultInjectingImuBackend();
            var healthyMag = new FaultInjectingImuBackend();
            var healthyTouch = new FaultInjectingTouchBackend();

            // Enqueue 10 pre-crash samples at 100Hz (10ms apart)
            for (int i = 1; i <= 10; i++)
            {
                double t = 1000.0 + i * 10.0;
                crashingGyro.QueuedSamples.Enqueue(new NativeSensorSample { timestampMs = t, x = 0.1f * i, y = 0.2f, z = 0.3f });
                healthyAccel.QueuedSamples.Enqueue(new NativeSensorSample { timestampMs = t, x = 0f, y = 0f, z = 9.81f });
                healthyMag.QueuedSamples.Enqueue(new NativeSensorSample { timestampMs = t, x = 25.4f, y = -12.1f, z = 41.0f });
            }

            _gyro.SetBackendForTesting(crashingGyro);
            _accel.SetBackendForTesting(healthyAccel);
            _mag.SetBackendForTesting(healthyMag);
            _touch.SetBackendForTesting(healthyTouch);

            _telemetry.SetTelemetryVariables(100, 100, 100, 50, 0.1f, 10, false, 326f);
            ActivateTelemetryWindowsForTesting(1000.0);
            InvokePrivateIfNeeded(_telemetry, "StartSensorWorkers");

            // Frame 1: All 10 pre-crash samples drain cleanly
            clock.CurrentMs = 1100.0;
            Assert.DoesNotThrow(() => _telemetry.FlushPendingSensorSamples());
            Assert.AreEqual(10, _telemetry.CurrentInferenceWindow.gyroSamples.Count);
            Assert.AreEqual(10, _telemetry.CurrentInferenceWindow.accelSamples.Count);
            Assert.AreEqual(10, _telemetry.CurrentInferenceWindow.magSamples.Count);

            // Frames 2..6: Gyro crashes on every drain; Accel, Mag, and Touch keep producing new data!
            for (int frame = 2; frame <= 6; frame++)
            {
                double t = 1000.0 + frame * 100.0;
                clock.CurrentMs = t;
                healthyAccel.QueuedSamples.Enqueue(new NativeSensorSample { timestampMs = t, x = 0.1f, y = 0.2f, z = 9.81f });
                healthyMag.QueuedSamples.Enqueue(new NativeSensorSample { timestampMs = t, x = 26.0f, y = -12.0f, z = 40.5f });
                healthyTouch.QueuedSamples.Enqueue(new NativeTouchSample
                {
                    timestampMs = t,
                    screenX = 163f,
                    screenY = 326f,
                    radius = 6f,
                    touchId = 1,
                    phaseCode = frame == 2 ? 0 : 1,
                    touchIndex = 0
                });

                Assert.DoesNotThrow(() => _telemetry.FlushPendingSensorSamples());
            }

            // Gyro tripped after 3 consecutive failures, preserving its original 10 samples!
            Assert.IsTrue(_gyro.IsNativeFaulted, "Crashing Gyro should trip its circuit breaker");
            Assert.AreEqual(3, crashingGyro.CircuitBreakerTotalFailuresOrDefault(_gyro), "Gyro should stop calling native backend after 3 consecutive failures");
            Assert.AreEqual(10, _telemetry.CurrentInferenceWindow.gyroSamples.Count, "Pre-crash Gyro samples must remain intact");

            // Healthy sensors were 100% unaffected!
            Assert.IsFalse(_accel.IsNativeFaulted);
            Assert.IsFalse(_mag.IsNativeFaulted);
            Assert.IsFalse(_touch.IsNativeFaulted);
            Assert.AreEqual(15, _telemetry.CurrentInferenceWindow.accelSamples.Count);
            Assert.AreEqual(15, _telemetry.CurrentInferenceWindow.magSamples.Count);
            Assert.AreEqual(5, _telemetry.CurrentInferenceWindow.touchSamples.Count);
        }

        [Test]
        public void TransientSingleFailure_ResetsOnNextSuccess_WithoutTrippingBreaker()
        {
            var backend = new FaultInjectingImuBackend
            {
                ThrowOnDrain = new InvalidOperationException("One-off transient hiccup")
            };
            _gyro.SetBackendForTesting(backend);
            _gyro.StartSampling(50);

            var dst = new List<NativeSensorSample>();

            // Failure #1 (threshold is 3)
            Assert.AreEqual(0, _gyro.DrainSamples(dst));
            Assert.IsFalse(_gyro.IsNativeFaulted);
            Assert.AreEqual(1, _gyro.CircuitBreaker.ConsecutiveFailures);

            // Recovery on next frame resets consecutive failures to 0
            backend.ThrowOnDrain = null;
            backend.QueuedSamples.Enqueue(new NativeSensorSample { timestampMs = 120.0, x = 1f, y = 2f, z = 3f });
            Assert.AreEqual(1, _gyro.DrainSamples(dst));
            Assert.IsFalse(_gyro.IsNativeFaulted);
            Assert.AreEqual(0, _gyro.CircuitBreaker.ConsecutiveFailures);
        }

        [Test]
        public void CorruptedSamples_NaN_Infinity_NegativeTimestamps_AreSanitized_AndNeverPoisonInferenceTensors()
        {
            var clock = new FaultInjectingClockBackend { CurrentMs = 5000.0 };
            NativeSensorTime.SetBackendForTesting(clock);

            var gyroBackend = new FaultInjectingImuBackend();
            var accelBackend = new FaultInjectingImuBackend();
            var magBackend = new FaultInjectingImuBackend();
            var touchBackend = new FaultInjectingTouchBackend();

            // Inject toxic NaN / Infinity / negative-timestamp samples alongside 1 valid sample per channel
            gyroBackend.QueuedSamples.Enqueue(new NativeSensorSample { timestampMs = double.NaN, x = 1f, y = 1f, z = 1f });
            gyroBackend.QueuedSamples.Enqueue(new NativeSensorSample { timestampMs = -50.0, x = 1f, y = 1f, z = 1f });
            gyroBackend.QueuedSamples.Enqueue(new NativeSensorSample { timestampMs = 5010.0, x = float.NaN, y = 0f, z = 0f });
            gyroBackend.QueuedSamples.Enqueue(new NativeSensorSample { timestampMs = 5020.0, x = 0f, y = float.PositiveInfinity, z = 0f });
            gyroBackend.QueuedSamples.Enqueue(new NativeSensorSample { timestampMs = 5030.0, x = 0.25f, y = -0.5f, z = 1.125f }); // Valid!

            accelBackend.QueuedSamples.Enqueue(new NativeSensorSample { timestampMs = 5010.0, x = float.NegativeInfinity, y = 0f, z = 9.8f });
            accelBackend.QueuedSamples.Enqueue(new NativeSensorSample { timestampMs = 5030.0, x = 0.0f, y = 0.0f, z = 9.80665f }); // Valid!

            magBackend.QueuedSamples.Enqueue(new NativeSensorSample { timestampMs = double.PositiveInfinity, x = 10f, y = 20f, z = 30f });
            magBackend.QueuedSamples.Enqueue(new NativeSensorSample { timestampMs = 5030.0, x = 31.24f, y = -14.88f, z = 45.01f }); // Valid!

            // Touch: 2 invalid (NaN screenX, negative timestamp) + 1 edge-case valid (negative radius, out-of-range phaseCode & touchIndex)
            touchBackend.QueuedSamples.Enqueue(new NativeTouchSample { timestampMs = -1.0, screenX = 10f, screenY = 10f, radius = 5f });
            touchBackend.QueuedSamples.Enqueue(new NativeTouchSample { timestampMs = 5010.0, screenX = float.NaN, screenY = 10f, radius = 5f });
            touchBackend.QueuedSamples.Enqueue(new NativeTouchSample
            {
                timestampMs = 5030.0,
                screenX = 326f,
                screenY = 652f,
                radius = float.NaN, // Should sanitize to 0
                touchId = 7,
                phaseCode = 999,    // Should sanitize to 2 ("stationary")
                touchIndex = -4     // Should sanitize to 0
            });

            _gyro.SetBackendForTesting(gyroBackend);
            _accel.SetBackendForTesting(accelBackend);
            _mag.SetBackendForTesting(magBackend);
            _touch.SetBackendForTesting(touchBackend);

            _telemetry.SetTelemetryVariables(100, 100, 100, 50, 0.1f, 10, false, 326f);
            ActivateTelemetryWindowsForTesting(5000.0);
            InvokePrivateIfNeeded(_telemetry, "StartSensorWorkers");

            clock.CurrentMs = 5050.0;
            _telemetry.FlushPendingSensorSamples();

            NX10TelemetryInferenceWindow inf = _telemetry.CurrentInferenceWindow;
            Assert.AreEqual(1, inf.gyroSamples.Count, "Only the 1 valid Gyro sample should pass sanitization");
            Assert.AreEqual(1, inf.accelSamples.Count, "Only the 1 valid Accel sample should pass sanitization");
            Assert.AreEqual(1, inf.magSamples.Count, "Only the 1 valid Mag sample should pass sanitization");
            Assert.AreEqual(1, inf.touchSamples.Count, "Only the 1 sanitizable Touch sample should pass");

            Assert.AreEqual(0f, inf.touchSamples[0].radius, 0.0001f);
            Assert.AreEqual(2f, inf.touchSamples[0].touchType, 0.0001f);
            Assert.AreEqual(0f, inf.touchSamples[0].index, 0.0001f);

            double nowWindowMs = inf.PruneAndCaptureTimestampMs();
            AssertAllFinite(inf.BuildGyroTensorData(nowWindowMs), "gyro_raw");
            AssertAllFinite(inf.BuildAccelTensorData(nowWindowMs), "acc_raw");
            AssertAllFinite(inf.BuildMagTensorData(nowWindowMs), "mag_raw");
            AssertAllFinite(inf.BuildTouchTensorData(nowWindowMs), "touch_raw");
        }

        [Test]
        public void PathologicalDrainCounts_Negative_Oversized_AndInfiniteStream_AreBoundedAndNeverHang()
        {
            var gyroBackend = new FaultInjectingImuBackend();
            var touchBackend = new FaultInjectingTouchBackend();
            _gyro.SetBackendForTesting(gyroBackend);
            _touch.SetBackendForTesting(touchBackend);

            _gyro.StartSampling(100);
            _touch.StartSampling(100);

            var sensorDst = new List<NativeSensorSample>();
            var touchDst = new List<NativeTouch.TimestampedTouchSample>();

            // 1. Negative counts (-1, int.MinValue)
            gyroBackend.ForcedDrainReturnCount = -1;
            touchBackend.ForcedDrainReturnCount = int.MinValue;
            Assert.AreEqual(0, _gyro.DrainSamples(sensorDst));
            Assert.AreEqual(0, _touch.DrainSamples(touchDst));

            // 2. Oversized count (> 512 batch buffer) — must NOT throw IndexOutOfRangeException
            gyroBackend.ForcedDrainReturnCount = 999999;
            touchBackend.ForcedDrainReturnCount = 999999;
            sensorDst.Clear();
            touchDst.Clear();

            int expectedCap = NativeSensorSampleValidator.DrainBatchSize * NativeSensorSampleValidator.MaxBatchesPerDrain;
            Assert.AreEqual(expectedCap, _gyro.DrainSamples(sensorDst));
            Assert.AreEqual(expectedCap, _touch.DrainSamples(touchDst));

            // 3. Infinite stream mode (native always returns full batch of 512) — must cap at MaxBatchesPerDrain and never hang
            gyroBackend.ForcedDrainReturnCount = null;
            touchBackend.ForcedDrainReturnCount = null;
            gyroBackend.InfiniteStreamMode = true;
            touchBackend.InfiniteStreamMode = true;
            sensorDst.Clear();
            touchDst.Clear();

            Stopwatch sw = Stopwatch.StartNew();
            int drainedGyro = _gyro.DrainSamples(sensorDst);
            int drainedTouch = _touch.DrainSamples(touchDst);
            sw.Stop();

            Assert.AreEqual(expectedCap, drainedGyro, "Infinite native gyro stream must be strictly capped at 4096 samples per frame");
            Assert.AreEqual(expectedCap, drainedTouch, "Infinite native touch stream must be strictly capped at 4096 samples per frame");
            Assert.Less(sw.ElapsedMilliseconds, 500, "Bounded drain loop must complete in milliseconds without hanging main thread");

            // 4. Touch backend throwing mid-session on Drain
            touchBackend.InfiniteStreamMode = false;
            touchBackend.ThrowOnDrain = new SEHException("Simulated touch JNI fault");
            for (int i = 0; i < 4; i++)
            {
                Assert.DoesNotThrow(() => _touch.DrainSamples(touchDst));
            }
            Assert.IsTrue(_touch.IsNativeFaulted, "Touch circuit breaker should trip after 3 consecutive drain faults");
        }

        [Test]
        public void NativeClockFault_NaN_Negative_OrException_SeamlesslyFallsBackToStopwatch()
        {
            var faultyClock = new FaultInjectingClockBackend { CurrentMs = double.NaN };
            NativeSensorTime.SetBackendForTesting(faultyClock);

            double t1 = NativeSensorTime.NowMs();
            Assert.IsTrue(NativeSensorSampleValidator.IsFinite(t1) && t1 >= 0.0);
            Assert.IsTrue(NativeSensorTime.CircuitBreaker.IsTripped);

            double t2 = NativeSensorTime.NowMs();
            Assert.GreaterOrEqual(t2, t1);
            Assert.AreEqual(1, faultyClock.Calls, "Faulted native clock should not be called again once breaker trips");

            // Also test exception on clock read
            var throwingClock = new FaultInjectingClockBackend { ThrowOnRead = new DllNotFoundException("clock missing") };
            NativeSensorTime.SetBackendForTesting(throwingClock);
            double t3 = NativeSensorTime.NowMs();
            Assert.IsTrue(NativeSensorSampleValidator.IsFinite(t3) && t3 >= 0.0);
            Assert.IsTrue(NativeSensorTime.CircuitBreaker.IsTripped);
        }

        [Test]
        public void FramerateIndependence_100HzSamplesDuring10FpsHitch_PreserveExact10msIntervals()
        {
            var clock = new FaultInjectingClockBackend { CurrentMs = 2000.0 };
            NativeSensorTime.SetBackendForTesting(clock);

            var gyroBackend = new FaultInjectingImuBackend();
            _gyro.SetBackendForTesting(gyroBackend);

            _telemetry.SetTelemetryVariables(100, null, null, null, null, 10, false, 326f);
            ActivateTelemetryWindowsForTesting(2000.0);
            InvokePrivateIfNeeded(_telemetry, "StartSensorWorkers");

            // Simulate a 100ms frame stall (10 FPS) while the native background timer collected 10 samples at 100Hz (every 10ms)
            for (int i = 1; i <= 10; i++)
            {
                gyroBackend.QueuedSamples.Enqueue(new NativeSensorSample
                {
                    timestampMs = 2000.0 + (i * 10.0),
                    x = i * 0.01f,
                    y = -i * 0.01f,
                    z = 0.5f
                });
            }

            clock.CurrentMs = 2100.0;
            _telemetry.FlushPendingSensorSamples();

            Assert.AreEqual(10, _telemetry.CurrentCollectionWindow.inputEvents.Count);
            for (int i = 0; i < 10; i++)
            {
                Assert.AreEqual((i + 1) * 10.0, _telemetry.CurrentCollectionWindow.inputEvents[i].timestampOffsetMs, 0.001);
            }

            float[] gyroTensor = _telemetry.CurrentInferenceWindow.BuildGyroTensorData(100.0);
            // 10 samples * 4 + 1 trailing dt = 41 floats
            Assert.AreEqual(41, gyroTensor.Length);
            for (int i = 0; i < 10; i++)
            {
                Assert.AreEqual(10f, gyroTensor[i * 4], 0.001f, $"Sample {i} delta-time must be 10ms (100Hz), not 100ms (10 FPS)");
            }
            Assert.AreEqual(0f, gyroTensor[40], 0.001f);
        }

        [Test]
        public void ManagedRingSampler_SurvivesThrowingTickCallback_RingOverflow_AndRapidStartStop()
        {
            using var sampler = new ManagedFixedHzRingSampler<NativeSensorSample>();
            int ticks = 0;

            // Callback throws on odd ticks and pushes on even ticks — background thread must NOT crash!
            sampler.Start(500, () =>
            {
                int n = Interlocked.Increment(ref ticks);
                if ((n & 1) == 1)
                    throw new InvalidOperationException("Simulated tick callback fault");

                sampler.Push(new NativeSensorSample { timestampMs = n, x = 1f, y = 2f, z = 3f });
            });

            Thread.Sleep(35);
            sampler.Stop();
            Assert.Greater(ticks, 2, "Sampler should keep ticking even after callback exceptions");

            // Overflow test: push 5000 items into 4096-capacity ring buffer — must keep newest 4096 without crashing
            sampler.Start(10, () => { });
            sampler.Stop();
            for (int i = 1; i <= 5000; i++)
            {
                sampler.Push(new NativeSensorSample { timestampMs = i, x = i, y = 0f, z = 0f });
            }

            var batch = new NativeSensorSample[5000];
            int drained = sampler.DrainToArray(batch, batch.Length);
            Assert.AreEqual(4096, drained);
            Assert.AreEqual(5000 - 4096 + 1, batch[0].timestampMs, 0.001, "Ring buffer overflow should overwrite oldest samples and retain newest 4096");
            Assert.AreEqual(5000, batch[4095].timestampMs, 0.001);

            // Extreme Hz & null buffer safety
            Assert.AreEqual(0, _gyro.DrainSamples(null));
            Assert.AreEqual(0, _touch.DrainSamples(null));

            var mockGyro = new FaultInjectingImuBackend();
            _gyro.SetBackendForTesting(mockGyro);
            _gyro.StartSampling(-50);
            Assert.IsFalse(_gyro.IsSampling);

            _gyro.StartSampling(int.MaxValue);
            Assert.IsTrue(_gyro.IsSampling);
            Assert.AreEqual(NativeSensorSampleValidator.MaxAllowedHz, _gyro.CurrentHz);
            _gyro.StopSampling();
        }

        private static void AssertAllFinite(float[] tensorValues, string name)
        {
            Assert.IsNotNull(tensorValues, $"{name} tensor array was null");
            Assert.Greater(tensorValues.Length, 0, $"{name} tensor array was empty");
            for (int i = 0; i < tensorValues.Length; i++)
            {
                Assert.IsFalse(float.IsNaN(tensorValues[i]), $"{name}[{i}] was NaN");
                Assert.IsFalse(float.IsInfinity(tensorValues[i]), $"{name}[{i}] was Infinity");
            }
        }
    }

    internal static class CircuitBreakerTestExtensions
    {
        public static int CircuitBreakerTotalFailuresOrDefault(this object _, NativeGyro gyro)
        {
            return gyro.CircuitBreaker.TotalFailures;
        }
    }
}
