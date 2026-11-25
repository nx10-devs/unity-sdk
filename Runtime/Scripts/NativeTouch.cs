using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
#endif

namespace NX10
{
    public class NativeTouch : MonoBehaviour
    {
        public struct TouchObject
        {
            public double x;
            public double y;
            public string touchId;
            public string touchType;
            public float touchTypeFloat;
            public double touchRadius;
        }

        public struct TimestampedTouchSample
        {
            public double timestampMs;
            public int touchIndex;
            public TouchObject touch;
        }

#if UNITY_IOS && !UNITY_EDITOR
        private sealed class IosTouchBackend : INativeTouchBackend
        {
            [DllImport("__Internal")] private static extern void _NX10StartTouch(int hz);
            [DllImport("__Internal")] private static extern void _NX10StopTouch();
            [DllImport("__Internal")] private static extern int _NX10DrainTouchSamples([Out] NativeTouchSample[] outBuffer, int maxCount);
            [DllImport("__Internal")] private static extern int _NX10GetActiveTouches([Out] NativeTouchSample[] outBuffer, int maxCount);

            public void Start(int hz) => _NX10StartTouch(hz);
            public void Stop() => _NX10StopTouch();
            public int Drain(NativeTouchSample[] batchBuffer, int maxBatchCount) => _NX10DrainTouchSamples(batchBuffer, maxBatchCount);
            public int GetActiveTouches(NativeTouchSample[] batchBuffer, int maxBatchCount) => _NX10GetActiveTouches(batchBuffer, maxBatchCount);
        }
#elif UNITY_ANDROID && !UNITY_EDITOR
        private sealed class AndroidTouchBackend : INativeTouchBackend
        {
            public void Start(int hz) => AndroidSensorBridge.StartTouch(hz);
            public void Stop() => AndroidSensorBridge.StopTouch();
            public int Drain(NativeTouchSample[] batchBuffer, int maxBatchCount) => AndroidSensorBridge.DrainTouch(batchBuffer, maxBatchCount);
            public int GetActiveTouches(NativeTouchSample[] batchBuffer, int maxBatchCount) => AndroidSensorBridge.GetActiveTouches(batchBuffer, maxBatchCount);
        }
#endif

        private readonly NativeSensorCircuitBreaker _circuitBreaker = new NativeSensorCircuitBreaker("Touch");
        private readonly NativeTouchSample[] _drainScratch = new NativeTouchSample[NativeSensorSampleValidator.DrainBatchSize];
        private readonly NativeTouchSample[] _activeScratch = new NativeTouchSample[16];
        private readonly TimestampedTouchSample[] _fallbackDrainScratch = new TimestampedTouchSample[NativeSensorSampleValidator.DrainBatchSize];
        private readonly Dictionary<int, string> _touchIdCache = new Dictionary<int, string>(32);

        private readonly object _fallbackTouchLock = new object();
        private readonly List<TouchObject> _fallbackCachedTouches = new List<TouchObject>(16);
        private readonly ManagedFixedHzRingSampler<TimestampedTouchSample> _fallbackSampler =
            new ManagedFixedHzRingSampler<TimestampedTouchSample>();

        private INativeTouchBackend _nativeBackend;
        private bool _usingFallbackSampler;
        private bool _treatRadiusAsPoints;
        private NativeScale nativeScale;
        private float dpi;

        public bool IsSampling { get; private set; }
        public int CurrentHz { get; private set; }
        public bool IsNativeFaulted => _circuitBreaker.IsTripped;

        internal NativeSensorCircuitBreaker CircuitBreaker => _circuitBreaker;

        private INativeTouchBackend ActiveBackend
        {
            get
            {
                if (_nativeBackend == null)
                {
#if UNITY_IOS && !UNITY_EDITOR
                    _nativeBackend = new IosTouchBackend();
                    _treatRadiusAsPoints = true;
#elif UNITY_ANDROID && !UNITY_EDITOR
                    _nativeBackend = new AndroidTouchBackend();
                    _treatRadiusAsPoints = false;
#endif
                }
                return _nativeBackend;
            }
        }

        internal void SetBackendForTesting(INativeTouchBackend backend, bool treatRadiusAsPoints = false)
        {
            StopSampling();
            _nativeBackend = backend;
            _treatRadiusAsPoints = treatRadiusAsPoints;
            _circuitBreaker.Reset();
        }

        public void Initialise(NativeScale nativeScale, float dpi)
        {
            this.nativeScale = nativeScale;
            this.dpi = NativeSensorSampleValidator.IsFinite(dpi) && dpi > 0f ? dpi : Screen.dpi;
            if (!NativeSensorSampleValidator.IsFinite(this.dpi) || this.dpi <= 0f)
                this.dpi = 160f;
        }

        public void StartSampling(int hz)
        {
            StopSampling();
            int clampedHz = NativeSensorSampleValidator.ClampHz(hz);
            if (clampedHz <= 0)
                return;

            INativeTouchBackend backend = ActiveBackend;
            if (backend != null && !_circuitBreaker.IsTripped)
            {
                try
                {
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
            RefreshFallbackTouchCache();
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

            INativeTouchBackend backend = ActiveBackend;
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

        public int DrainSamples(List<TimestampedTouchSample> destination)
        {
            if (destination == null)
                return 0;

            int startCount = destination.Count;

            if (!_usingFallbackSampler)
            {
                INativeTouchBackend backend = ActiveBackend;
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
                                NativeTouchSample s = _drainScratch[i];
                                if (!NativeSensorSampleValidator.TrySanitizeTouchSample(ref s))
                                    continue;

                                destination.Add(ConvertNativeTouchSample(s));
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
                    int count = _fallbackSampler.DrainToArray(_fallbackDrainScratch, _fallbackDrainScratch.Length);
                    if (count <= 0)
                        break;

                    for (int i = 0; i < count; i++)
                    {
                        destination.Add(_fallbackDrainScratch[i]);
                    }

                    if (count < _fallbackDrainScratch.Length)
                        break;
                }
            }

            return destination.Count - startCount;
        }

        public List<TouchObject> Touches
        {
            get
            {
                List<TouchObject> touches = new List<TouchObject>();
                INativeTouchBackend backend = ActiveBackend;
                if (backend != null && !_circuitBreaker.IsTripped)
                {
                    try
                    {
                        int rawCount = backend.GetActiveTouches(_activeScratch, _activeScratch.Length);
                        int safeCount = Math.Min(Math.Max(0, rawCount), _activeScratch.Length);
                        for (int i = 0; i < safeCount; i++)
                        {
                            NativeTouchSample s = _activeScratch[i];
                            if (!NativeSensorSampleValidator.TrySanitizeTouchSample(ref s))
                                continue;

                            touches.Add(ConvertNativeTouchSample(s).touch);
                        }
                        _circuitBreaker.RecordSuccess();
                        return touches;
                    }
                    catch (Exception ex)
                    {
                        _circuitBreaker.RecordFailure(ex, nameof(Touches));
                    }
                }

                PopulateFallbackTouches(touches);
                return touches;
            }
        }

        private TimestampedTouchSample ConvertNativeTouchSample(NativeTouchSample raw)
        {
            string phaseName = MapPhaseCode(raw.phaseCode, out float phaseFloat);
            double radiusMm = _treatRadiusAsPoints
                ? ConvertPixelRadToMM(raw.radius)
                : Math.Round(raw.radius, 3, MidpointRounding.AwayFromZero);

            return new TimestampedTouchSample
            {
                timestampMs = raw.timestampMs,
                touchIndex = raw.touchIndex,
                touch = new TouchObject
                {
                    x = PixelsToMillimeters(raw.screenX),
                    y = PixelsToMillimeters(raw.screenY),
                    touchId = GetCachedTouchIdString(raw.touchId),
                    touchType = phaseName,
                    touchTypeFloat = phaseFloat,
                    touchRadius = radiusMm
                }
            };
        }

        private string GetCachedTouchIdString(int id)
        {
            if (!_touchIdCache.TryGetValue(id, out string str))
            {
                if (_touchIdCache.Count >= 128)
                    _touchIdCache.Clear();

                str = id.ToString();
                _touchIdCache[id] = str;
            }
            return str;
        }

        private static string MapPhaseCode(int phaseCode, out float typeFloat)
        {
            switch (phaseCode)
            {
                case 0:
                    typeFloat = 0f;
                    return "down";
                case 1:
                    typeFloat = 1f;
                    return "move";
                case 2:
                    typeFloat = 2f;
                    return "stationary";
                case 3:
                    typeFloat = 3f;
                    return "up";
                case 4:
                    typeFloat = 3f;
                    return "cancelled";
                default:
                    typeFloat = 2f;
                    return "stationary";
            }
        }

        private float GetSafeDpi()
        {
            if (NativeSensorSampleValidator.IsFinite(dpi) && dpi > 0f)
                return dpi;
            if (NativeSensorSampleValidator.IsFinite(Screen.dpi) && Screen.dpi > 0f)
                return Screen.dpi;
            return 160f;
        }

        public double ConvertPixelRadToMM(Vector2 radius)
        {
            float majorRadius = Mathf.Max(radius.x, radius.y);
            return ConvertPixelRadToMM(majorRadius);
        }

        public double ConvertPixelRadToMM(float radius)
        {
            if (!NativeSensorSampleValidator.IsFinite(radius) || radius < 0f)
                return 0.0;

            double radiusMm = MmPerPoint() * radius;
            return NativeSensorSampleValidator.IsFinite(radiusMm)
                ? Math.Round(radiusMm, 3, MidpointRounding.AwayFromZero)
                : 0.0;
        }

        public double MmPerPoint()
        {
            float safeDpi = GetSafeDpi();
            double nativeIOSScale = 1.0;
            if (nativeScale != null)
            {
                try
                {
                    float s = nativeScale.GetNativeScale();
                    if (NativeSensorSampleValidator.IsFinite(s) && s > 0f)
                        nativeIOSScale = s;
                }
                catch
                {
                    nativeIOSScale = 1.0;
                }
            }
            return (nativeIOSScale / safeDpi) * 25.4;
        }

        public double PixelsToMillimeters(double pixels)
        {
            if (!NativeSensorSampleValidator.IsFinite(pixels))
                return 0.0;

            float safeDpi = GetSafeDpi();
            double inches = pixels / safeDpi;
            double millimeters = inches * 25.4f;
            return NativeSensorSampleValidator.IsFinite(millimeters)
                ? Math.Round(millimeters, 3, MidpointRounding.AwayFromZero)
                : 0.0;
        }

        private void Update()
        {
            if (IsSampling && _usingFallbackSampler)
            {
                RefreshFallbackTouchCache();
            }
        }

        private void RefreshFallbackTouchCache()
        {
            lock (_fallbackTouchLock)
            {
                _fallbackCachedTouches.Clear();
                PopulateFallbackTouches(_fallbackCachedTouches);
            }
        }

        private void PopulateFallbackTouches(List<TouchObject> touches)
        {
            try
            {
#if ENABLE_INPUT_SYSTEM
                if (!UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.enabled)
                    return;

                foreach (var touch in Touch.activeTouches)
                {
                    TouchObject obj = new TouchObject
                    {
                        x = PixelsToMillimeters(touch.screenPosition.x),
                        y = PixelsToMillimeters(touch.screenPosition.y),
                        touchId = GetCachedTouchIdString(touch.touchId),
                        touchType = ConvertTouchPhaseToTouchType(touch.phase, out float typeFloat),
                        touchTypeFloat = typeFloat,
#if UNITY_IOS
                        touchRadius = ConvertPixelRadToMM(touch.radius)
#else
                        touchRadius = Math.Round(Mathf.Max(0f, Mathf.Max(touch.radius.x, touch.radius.y)), 3, MidpointRounding.AwayFromZero)
#endif
                    };
                    touches.Add(obj);
                }
#else
                foreach (var touch in Input.touches)
                {
                    TouchObject obj = new TouchObject
                    {
                        x = PixelsToMillimeters(touch.position.x),
                        y = PixelsToMillimeters(touch.position.y),
                        touchId = GetCachedTouchIdString(touch.fingerId),
                        touchType = ConvertTouchPhaseToTouchType(touch.phase, out float typeFloat),
                        touchTypeFloat = typeFloat,
#if UNITY_IOS
                        touchRadius = ConvertPixelRadToMM(touch.radius)
#else
                        touchRadius = Math.Round(Mathf.Max(0f, touch.radius), 3, MidpointRounding.AwayFromZero)
#endif
                    };
                    touches.Add(obj);
                }
#endif
            }
            catch
            {
                // Ignore Unity InputSystem uninitialized state in headless/EditMode environments
            }
        }

        private void OnFallbackTimerTick()
        {
            double nowMs = NativeSensorTime.NowMs();
            lock (_fallbackTouchLock)
            {
                for (int i = 0; i < _fallbackCachedTouches.Count; i++)
                {
                    _fallbackSampler.Push(new TimestampedTouchSample
                    {
                        timestampMs = nowMs,
                        touchIndex = i,
                        touch = _fallbackCachedTouches[i]
                    });
                }
            }
        }

#if ENABLE_INPUT_SYSTEM
        private string ConvertTouchPhaseToTouchType(UnityEngine.InputSystem.TouchPhase touchPhase, out float typeFloat)
        {
            typeFloat = 0;
            switch (touchPhase)
            {
                case UnityEngine.InputSystem.TouchPhase.Began:
                    return "down";
                case UnityEngine.InputSystem.TouchPhase.Ended:
                    typeFloat = 3;
                    return "up";
                case UnityEngine.InputSystem.TouchPhase.Moved:
                    typeFloat = 1;
                    return "move";
                case UnityEngine.InputSystem.TouchPhase.Stationary:
                    typeFloat = 2;
                    return "stationary";
                case UnityEngine.InputSystem.TouchPhase.Canceled:
                    typeFloat = 3;
                    return "cancelled";
                default:
                    typeFloat = 2;
                    return "stationary";
            }
        }
#endif

        private string ConvertTouchPhaseToTouchType(UnityEngine.TouchPhase touchPhase, out float typeFloat)
        {
            typeFloat = 0;
            switch (touchPhase)
            {
                case UnityEngine.TouchPhase.Began:
                    return "down";
                case UnityEngine.TouchPhase.Ended:
                    typeFloat = 3;
                    return "up";
                case UnityEngine.TouchPhase.Moved:
                    typeFloat = 1;
                    return "move";
                case UnityEngine.TouchPhase.Stationary:
                    typeFloat = 2;
                    return "stationary";
                case UnityEngine.TouchPhase.Canceled:
                    typeFloat = 3;
                    return "cancelled";
                default:
                    typeFloat = 2;
                    return "stationary";
            }
        }

        private void OnDestroy()
        {
            StopSampling();
        }
    }
}
