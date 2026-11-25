using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace NX10
{
    public class NativeMagnetometer : MonoBehaviour
    {
#if UNITY_IOS && !UNITY_EDITOR
        private sealed class IosMagnetometerBackend : INativeImuBackend
        {
            [DllImport("__Internal")] private static extern bool _NX10IsMagnetometerAvailable();
            [DllImport("__Internal")] private static extern void _NX10StartMagnetometer(int hz);
            [DllImport("__Internal")] private static extern void _NX10StopMagnetometer();
            [DllImport("__Internal")] private static extern int _NX10DrainMagnetometerSamples([Out] NativeSensorSample[] outBuffer, int maxCount);
            [DllImport("__Internal")] private static extern bool _NX10GetLatestMagnetometer(out float x, out float y, out float z);

            public bool IsSupported() => _NX10IsMagnetometerAvailable();
            public void Start(int hz) => _NX10StartMagnetometer(hz);
            public void Stop() => _NX10StopMagnetometer();
            public int Drain(NativeSensorSample[] batchBuffer, int maxBatchCount) => _NX10DrainMagnetometerSamples(batchBuffer, maxBatchCount);
            public bool TryGetLatest(out Vector3 value)
            {
                if (_NX10GetLatestMagnetometer(out float x, out float y, out float z))
                {
                    value = new Vector3(x, y, z);
                    return NativeSensorSampleValidator.IsValidVector(in value);
                }
                value = Vector3.zero;
                return false;
            }
        }
#elif UNITY_ANDROID && !UNITY_EDITOR
        private sealed class AndroidMagnetometerBackend : INativeImuBackend
        {
            public bool IsSupported() => AndroidSensorBridge.IsMagSupported();
            public void Start(int hz) => AndroidSensorBridge.StartMag(hz);
            public void Stop() => AndroidSensorBridge.StopMag();
            public int Drain(NativeSensorSample[] batchBuffer, int maxBatchCount) => AndroidSensorBridge.DrainMag(batchBuffer, maxBatchCount);
            public bool TryGetLatest(out Vector3 value) => AndroidSensorBridge.TryGetLatestMag(out value);
        }
#endif

        private readonly NativeSensorCircuitBreaker _circuitBreaker = new NativeSensorCircuitBreaker("Magnetometer");
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
                    _nativeBackend = new IosMagnetometerBackend();
#elif UNITY_ANDROID && !UNITY_EDITOR
                    _nativeBackend = new AndroidMagnetometerBackend();
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

        public bool MagSupported
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
                        _circuitBreaker.RecordFailure(ex, nameof(MagSupported));
                    }
                }

                return IsFallbackMagSupported();
            }
        }

        public Vector3 RawMag
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
                            return latest.Round(1, MidpointRounding.AwayFromZero);
                        }
                        _circuitBreaker.RecordSuccess();
                        return _lastSampledValue;
                    }
                    catch (Exception ex)
                    {
                        _circuitBreaker.RecordFailure(ex, nameof(RawMag));
                    }
                }

                Vector3 fallback = ReadFallbackMagnetometer();
                return NativeSensorSampleValidator.IsValidVector(in fallback)
                    ? fallback.Round(1, MidpointRounding.AwayFromZero)
                    : _lastSampledValue;
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
            if (!IsFallbackMagSupported())
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

                                Vector3 rounded = new Vector3(s.x, s.y, s.z).Round(1, MidpointRounding.AwayFromZero);
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

                        Vector3 rounded = new Vector3(s.x, s.y, s.z).Round(1, MidpointRounding.AwayFromZero);
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

        private bool IsFallbackMagSupported()
        {
            try
            {
#if ENABLE_INPUT_SYSTEM
                return MagneticFieldSensor.current != null;
#elif ENABLE_LEGACY_INPUT_MANAGER
                return true;
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
            if (!IsFallbackMagSupported())
                return;

            Vector3 val = ReadFallbackMagnetometer();
            if (!NativeSensorSampleValidator.IsValidVector(in val))
                return;

            lock (_fallbackLock)
            {
                _fallbackLatest = val;
                _fallbackHasValue = true;
            }
        }

        private Vector3 ReadFallbackMagnetometer()
        {
            try
            {
#if ENABLE_INPUT_SYSTEM
                if (MagneticFieldSensor.current != null)
                {
                    return MagneticFieldSensor.current.magneticField.ReadUnprocessedValue();
                }
                return Vector3.zero;
#elif ENABLE_LEGACY_INPUT_MANAGER
                return Input.compass.rawVector;
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

        private void OnDestroy()
        {
            StopSampling();
        }
    }
}
