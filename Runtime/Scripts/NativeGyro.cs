using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace NX10
{
    public class NativeGyro : MonoBehaviour
    {
#if UNITY_IOS && !UNITY_EDITOR
        private sealed class IosGyroBackend : INativeImuBackend
        {
            [DllImport("__Internal")] private static extern bool _NX10IsGyroAvailable();
            [DllImport("__Internal")] private static extern void _NX10StartGyro(int hz);
            [DllImport("__Internal")] private static extern void _NX10StopGyro();
            [DllImport("__Internal")] private static extern int _NX10DrainGyroSamples([Out] NativeSensorSample[] outBuffer, int maxCount);
            [DllImport("__Internal")] private static extern bool _NX10GetLatestGyro(out float x, out float y, out float z);

            public bool IsSupported() => _NX10IsGyroAvailable();
            public void Start(int hz) => _NX10StartGyro(hz);
            public void Stop() => _NX10StopGyro();
            public int Drain(NativeSensorSample[] batchBuffer, int maxBatchCount) => _NX10DrainGyroSamples(batchBuffer, maxBatchCount);
            public bool TryGetLatest(out Vector3 value)
            {
                if (_NX10GetLatestGyro(out float x, out float y, out float z))
                {
                    value = new Vector3(x, y, z);
                    return NativeSensorSampleValidator.IsValidVector(in value);
                }
                value = Vector3.zero;
                return false;
            }
        }
#elif UNITY_ANDROID && !UNITY_EDITOR
        private sealed class AndroidGyroBackend : INativeImuBackend
        {
            public bool IsSupported() => AndroidSensorBridge.IsGyroSupported();
            public void Start(int hz) => AndroidSensorBridge.StartGyro(hz);
            public void Stop() => AndroidSensorBridge.StopGyro();
            public int Drain(NativeSensorSample[] batchBuffer, int maxBatchCount) => AndroidSensorBridge.DrainGyro(batchBuffer, maxBatchCount);
            public bool TryGetLatest(out Vector3 value) => AndroidSensorBridge.TryGetLatestGyro(out value);
        }
#endif

        private readonly NativeSensorCircuitBreaker _circuitBreaker = new NativeSensorCircuitBreaker("Gyro");
        private readonly NativeSensorSample[] _drainScratch = new NativeSensorSample[NativeSensorSampleValidator.DrainBatchSize];
        private readonly object _fallbackLock = new object();
        private readonly ManagedFixedHzRingSampler<NativeSensorSample> _fallbackSampler = new ManagedFixedHzRingSampler<NativeSensorSample>();

        private INativeImuBackend _nativeBackend;
        private bool _usingFallbackSampler;
        private Vector3 _fallbackLatest;
        private bool _fallbackHasValue;
        private Vector3 _lastSampledValue;

        public bool IsSampling { get; private set; }
        public int CurrentHz { get; private set; }
        public bool IsNativeFaulted => _circuitBreaker.IsTripped;

        internal NativeSensorCircuitBreaker CircuitBreaker => _circuitBreaker;

        private INativeImuBackend ActiveBackend
        {
            get
            {
                if (_nativeBackend == null)
                {
#if UNITY_IOS && !UNITY_EDITOR
                    _nativeBackend = new IosGyroBackend();
#elif UNITY_ANDROID && !UNITY_EDITOR
                    _nativeBackend = new AndroidGyroBackend();
#endif
                }
                return _nativeBackend;
            }
        }

        internal void SetBackendForTesting(INativeImuBackend backend)
        {
            StopSampling();
            _nativeBackend = backend;
            _circuitBreaker.Reset();
        }

        public bool GyroSupported
        {
            get
            {
                INativeImuBackend backend = ActiveBackend;
                if (backend != null && !_circuitBreaker.IsTripped)
                {
                    try
                    {
                        bool supported = backend.IsSupported();
                        _circuitBreaker.RecordSuccess();
                        return supported;
                    }
                    catch (Exception ex)
                    {
                        _circuitBreaker.RecordFailure(ex, nameof(GyroSupported));
                    }
                }

                return IsFallbackGyroSupported();
            }
        }

        public Vector3 GyroRotationRate
        {
            get
            {
                INativeImuBackend backend = ActiveBackend;
                if (backend != null && !_circuitBreaker.IsTripped)
                {
                    try
                    {
                        if (backend.TryGetLatest(out Vector3 latest) && NativeSensorSampleValidator.IsValidVector(in latest))
                        {
                            _circuitBreaker.RecordSuccess();
                            return latest.RoundToFivePlaces();
                        }
                        _circuitBreaker.RecordSuccess();
                        return _lastSampledValue;
                    }
                    catch (Exception ex)
                    {
                        _circuitBreaker.RecordFailure(ex, nameof(GyroRotationRate));
                    }
                }

                Vector3 fallback = ReadFallbackGyro();
                return NativeSensorSampleValidator.IsValidVector(in fallback) ? fallback.RoundToFivePlaces() : _lastSampledValue;
            }
        }

        public void StartSampling(int hz)
        {
            StopSampling();
            int clampedHz = NativeSensorSampleValidator.ClampHz(hz);
            if (clampedHz <= 0)
                return;

            INativeImuBackend backend = ActiveBackend;
            if (backend != null && !_circuitBreaker.IsTripped)
            {
                try
                {
                    if (!backend.IsSupported())
                        return;

                    backend.Start(clampedHz);
                    _circuitBreaker.RecordSuccess();
                    CurrentHz = clampedHz;
                    IsSampling = true;
                    _usingFallbackSampler = false;
                    return;
                }
                catch (Exception ex)
                {
                    _circuitBreaker.RecordFailure(ex, nameof(StartSampling));
                }
            }

            StartFallbackSampling(clampedHz);
        }

        private void StartFallbackSampling(int clampedHz)
        {
            if (!IsFallbackGyroSupported())
                return;

            RefreshFallbackCache();
            _fallbackSampler.Start(clampedHz, OnFallbackTimerTick);
            CurrentHz = clampedHz;
            IsSampling = true;
            _usingFallbackSampler = true;
        }

        public void StopSampling()
        {
            if (!IsSampling)
                return;

            IsSampling = false;
            int prevHz = CurrentHz;
            CurrentHz = 0;

            if (_usingFallbackSampler)
            {
                _usingFallbackSampler = false;
                _fallbackSampler.Stop();
                return;
            }

            INativeImuBackend backend = ActiveBackend;
            if (backend != null)
            {
                try
                {
                    backend.Stop();
                }
                catch (Exception ex)
                {
                    _circuitBreaker.RecordFailure(ex, $"{nameof(StopSampling)}@{prevHz}Hz");
                }
            }
        }

        public int DrainSamples(List<NativeSensorSample> destination)
        {
            if (destination == null)
                return 0;

            int startCount = destination.Count;

            if (!_usingFallbackSampler)
            {
                INativeImuBackend backend = ActiveBackend;
                if (backend != null && !_circuitBreaker.IsTripped)
                {
                    try
                    {
                        for (int batch = 0; batch < NativeSensorSampleValidator.MaxBatchesPerDrain; batch++)
                        {
                            int rawCount = backend.Drain(_drainScratch, _drainScratch.Length);
                            if (rawCount <= 0)
                                break;

                            int safeCount = Math.Min(rawCount, _drainScratch.Length);
                            for (int i = 0; i < safeCount; i++)
                            {
                                NativeSensorSample s = _drainScratch[i];
                                if (!NativeSensorSampleValidator.IsValidSample(in s))
                                    continue;

                                Vector3 rounded = new Vector3(s.x, s.y, s.z).RoundToFivePlaces();
                                s.x = rounded.x;
                                s.y = rounded.y;
                                s.z = rounded.z;
                                _lastSampledValue = rounded;
                                destination.Add(s);
                            }

                            if (rawCount < _drainScratch.Length)
                                break;
                        }

                        _circuitBreaker.RecordSuccess();
                        return destination.Count - startCount;
                    }
                    catch (Exception ex)
                    {
                        _circuitBreaker.RecordFailure(ex, nameof(DrainSamples));
                        if (_circuitBreaker.IsTripped && IsSampling && CurrentHz > 0)
                        {
                            StartFallbackSampling(CurrentHz);
                        }
                        return destination.Count - startCount;
                    }
                }
            }

            if (_usingFallbackSampler)
            {
                for (int batch = 0; batch < NativeSensorSampleValidator.MaxBatchesPerDrain; batch++)
                {
                    int count = _fallbackSampler.DrainToArray(_drainScratch, _drainScratch.Length);
                    if (count <= 0)
                        break;

                    for (int i = 0; i < count; i++)
                    {
                        NativeSensorSample s = _drainScratch[i];
                        if (!NativeSensorSampleValidator.IsValidSample(in s))
                            continue;

                        Vector3 rounded = new Vector3(s.x, s.y, s.z).RoundToFivePlaces();
                        s.x = rounded.x;
                        s.y = rounded.y;
                        s.z = rounded.z;
                        _lastSampledValue = rounded;
                        destination.Add(s);
                    }

                    if (count < _drainScratch.Length)
                        break;
                }
            }

            return destination.Count - startCount;
        }

        private void Update()
        {
            if (IsSampling && _usingFallbackSampler)
            {
                RefreshFallbackCache();
            }
        }

        private bool IsFallbackGyroSupported()
        {
            try
            {
#if ENABLE_INPUT_SYSTEM
                return UnityEngine.InputSystem.Gyroscope.current != null;
#elif ENABLE_LEGACY_INPUT_MANAGER
                return SystemInfo.supportsGyroscope;
#else
                return false;
#endif
            }
            catch
            {
                return false;
            }
        }

        private void RefreshFallbackCache()
        {
            if (!IsFallbackGyroSupported())
                return;

            Vector3 val = ReadFallbackGyro();
            if (!NativeSensorSampleValidator.IsValidVector(in val))
                return;

            lock (_fallbackLock)
            {
                _fallbackLatest = val;
                _fallbackHasValue = true;
            }
        }

        private Vector3 ReadFallbackGyro()
        {
            try
            {
#if ENABLE_INPUT_SYSTEM
                if (UnityEngine.InputSystem.Gyroscope.current != null)
                {
                    return UnityEngine.InputSystem.Gyroscope.current.angularVelocity.ReadUnprocessedValue();
                }
                return Vector3.zero;
#elif ENABLE_LEGACY_INPUT_MANAGER
                return ConvertGyroData(Input.gyro.rotationRateUnbiased);
#else
                return Vector3.zero;
#endif
            }
            catch
            {
                return Vector3.zero;
            }
        }

        private void OnFallbackTimerTick()
        {
            Vector3 val;
            bool hasVal;
            lock (_fallbackLock)
            {
                val = _fallbackLatest;
                hasVal = _fallbackHasValue;
            }

            if (!hasVal)
                return;

            _fallbackSampler.Push(new NativeSensorSample
            {
                timestampMs = NativeSensorTime.NowMs(),
                x = val.x,
                y = val.y,
                z = val.z
            });
        }

        public Vector3 ConvertGyroData(Vector3 screenGyro)
        {
            if (!Input.compensateSensors) return screenGyro.RoundToFivePlaces();

            Vector3 convertedVector;
            switch (Screen.orientation)
            {
                case UnityEngine.ScreenOrientation.LandscapeLeft:
                    convertedVector = new Vector3(screenGyro.y, -screenGyro.x, screenGyro.z);
                    break;
                case UnityEngine.ScreenOrientation.LandscapeRight:
                    convertedVector = new Vector3(-screenGyro.y, screenGyro.x, screenGyro.z);
                    break;
                case UnityEngine.ScreenOrientation.PortraitUpsideDown:
                    convertedVector = new Vector3(-screenGyro.x, -screenGyro.y, screenGyro.z);
                    break;
                case UnityEngine.ScreenOrientation.Portrait:
                default:
                    convertedVector = screenGyro;
                    break;
            }

            return convertedVector.RoundToFivePlaces();
        }

        private void OnDestroy()
        {
            StopSampling();
        }
    }
}
