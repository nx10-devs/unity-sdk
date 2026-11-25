using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

[assembly: InternalsVisibleTo("NX10.Tests")]

namespace NX10
{
    /// <summary>
    /// Blittable 24-byte timestamped 3-axis sample matching native iOS/Android layout.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct NativeSensorSample
    {
        public double timestampMs;
        public float x;
        public float y;
        public float z;
        public float reserved;
    }

    /// <summary>
    /// Blittable 32-byte timestamped touch sample matching native iOS/Android layout.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct NativeTouchSample
    {
        public double timestampMs;
        public float screenX;
        public float screenY;
        public float radius;
        public int touchId;
        public int phaseCode;  // 0=down, 1=move, 2=stationary, 3=up, 4=cancelled
        public int touchIndex; // 0-based index within the tick snapshot
    }

    internal interface INativeClockBackend
    {
        double GetMonotonicTimeMs();
    }

    internal interface INativeImuBackend
    {
        bool IsSupported();
        void Start(int hz);
        void Stop();
        int Drain(NativeSensorSample[] batchBuffer, int maxBatchCount);
        bool TryGetLatest(out Vector3 value);
    }

    internal interface INativeTouchBackend
    {
        void Start(int hz);
        void Stop();
        int Drain(NativeTouchSample[] batchBuffer, int maxBatchCount);
        int GetActiveTouches(NativeTouchSample[] batchBuffer, int maxBatchCount);
    }

    /// <summary>
    /// Validates and sanitizes raw native samples so corrupted driver values (NaN, Infinity, negative timestamps)
    /// can never poison telemetry JSON payloads or ONNX inference tensors.
    /// </summary>
    internal static class NativeSensorSampleValidator
    {
        public const int DrainBatchSize = 512;
        public const int MaxBatchesPerDrain = 8; // Hard cap: 4096 samples per frame (prevents infinite drain loops)
        public const int MaxAllowedHz = 1000;

        public static int ClampHz(int hz)
        {
            if (hz <= 0) return 0;
            return hz > MaxAllowedHz ? MaxAllowedHz : hz;
        }

        public static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        public static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        public static bool IsValidSample(in NativeSensorSample s)
        {
            return IsFinite(s.timestampMs) &&
                   s.timestampMs >= 0.0 &&
                   IsFinite(s.x) &&
                   IsFinite(s.y) &&
                   IsFinite(s.z);
        }

        public static bool IsValidVector(in Vector3 v)
        {
            return IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
        }

        public static bool TrySanitizeTouchSample(ref NativeTouchSample s)
        {
            if (!IsFinite(s.timestampMs) || s.timestampMs < 0.0)
                return false;

            if (!IsFinite(s.screenX) || !IsFinite(s.screenY))
                return false;

            if (!IsFinite(s.radius) || s.radius < 0f)
                s.radius = 0f;

            if (s.phaseCode < 0 || s.phaseCode > 4)
                s.phaseCode = 2; // stationary

            if (s.touchIndex < 0)
                s.touchIndex = 0;

            return true;
        }
    }

    /// <summary>
    /// Isolates native plugin failures per sensor channel. Trips immediately on missing symbols/binaries
    /// or after consecutive runtime errors, preventing per-frame exception storms and game crashes.
    /// </summary>
    internal sealed class NativeSensorCircuitBreaker
    {
        private readonly string _channelName;
        private readonly int _maxConsecutiveFailures;

        public int ConsecutiveFailures { get; private set; }
        public int TotalFailures { get; private set; }
        public bool IsTripped { get; private set; }
        public string LastError { get; private set; }

        public NativeSensorCircuitBreaker(string channelName, int maxConsecutiveFailures = 3)
        {
            _channelName = channelName;
            _maxConsecutiveFailures = Math.Max(1, maxConsecutiveFailures);
        }

        public void RecordSuccess()
        {
            ConsecutiveFailures = 0;
        }

        public void RecordFailure(Exception ex, string operation)
        {
            TotalFailures++;
            ConsecutiveFailures++;
            LastError = ex != null ? $"{ex.GetType().Name} in {operation}: {ex.Message}" : $"Unknown error in {operation}";

            bool fatalInteropError =
                ex is DllNotFoundException ||
                ex is EntryPointNotFoundException ||
                ex is BadImageFormatException ||
                ex is MarshalDirectiveException ||
                ex is TypeLoadException;

            if (!IsTripped && (fatalInteropError || ConsecutiveFailures >= _maxConsecutiveFailures))
            {
                IsTripped = true;
                UnityEngine.Debug.LogWarning(
                    $"[NX10] Native sensor '{_channelName}' faulted ({LastError}). " +
                    "Circuit breaker tripped; isolating native channel to protect gameplay.");
            }
        }

        public void Reset()
        {
            ConsecutiveFailures = 0;
            TotalFailures = 0;
            IsTripped = false;
            LastError = null;
        }
    }

    /// <summary>
    /// Unified high-resolution monotonic clock in milliseconds shared across C# windows and native sensor buffers.
    /// Automatically falls back to System.Diagnostics.Stopwatch if the native clock faults.
    /// </summary>
    public static class NativeSensorTime
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern double _NX10GetMonotonicTimeMs();
#endif

        private static readonly NativeSensorCircuitBreaker _clockBreaker = new NativeSensorCircuitBreaker("Clock", 1);
        private static INativeClockBackend _overrideBackend;

        internal static NativeSensorCircuitBreaker CircuitBreaker => _clockBreaker;

        internal static void SetBackendForTesting(INativeClockBackend backend)
        {
            _overrideBackend = backend;
            _clockBreaker.Reset();
        }

        public static double FallbackStopwatchMs()
        {
            return (double)Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;
        }

        public static double NowMs()
        {
            if (!_clockBreaker.IsTripped)
            {
                try
                {
                    if (_overrideBackend != null)
                    {
                        double t = _overrideBackend.GetMonotonicTimeMs();
                        if (NativeSensorSampleValidator.IsFinite(t) && t >= 0.0)
                        {
                            _clockBreaker.RecordSuccess();
                            return t;
                        }
                        throw new InvalidOperationException($"Invalid monotonic timestamp: {t}");
                    }

#if UNITY_IOS && !UNITY_EDITOR
                    double iosMs = _NX10GetMonotonicTimeMs();
                    if (NativeSensorSampleValidator.IsFinite(iosMs) && iosMs >= 0.0)
                    {
                        _clockBreaker.RecordSuccess();
                        return iosMs;
                    }
                    throw new InvalidOperationException($"Invalid iOS monotonic timestamp: {iosMs}");
#elif UNITY_ANDROID && !UNITY_EDITOR
                    double androidMs = AndroidSensorBridge.GetMonotonicTimeMs();
                    if (NativeSensorSampleValidator.IsFinite(androidMs) && androidMs >= 0.0)
                    {
                        _clockBreaker.RecordSuccess();
                        return androidMs;
                    }
                    throw new InvalidOperationException($"Invalid Android monotonic timestamp: {androidMs}");
#endif
                }
                catch (Exception ex)
                {
                    _clockBreaker.RecordFailure(ex, nameof(NowMs));
                }
            }

            return FallbackStopwatchMs();
        }
    }

    /// <summary>
    /// Background fixed-Hz ring-buffered sampler used in the Unity Editor, standalone builds, and graceful fallback.
    /// </summary>
    internal sealed class ManagedFixedHzRingSampler<T> : IDisposable where T : struct
    {
        private const int Capacity = 4096;
        private readonly object _lock = new object();
        private readonly T[] _ring = new T[Capacity];
        private int _readIdx;
        private int _writeIdx;
        private int _count;

        private Thread _workerThread;
        private volatile bool _running;
        private int _intervalNs;
        private Action _onTick;

        public bool IsRunning => _running;

        public void Start(int rawHz, Action onTick)
        {
            Stop();
            int hz = NativeSensorSampleValidator.ClampHz(rawHz);
            if (hz <= 0 || onTick == null)
                return;

            lock (_lock)
            {
                _readIdx = 0;
                _writeIdx = 0;
                _count = 0;
            }

            _onTick = onTick;
            _intervalNs = Math.Max(1_000_000, (int)Math.Round(1_000_000_000.0 / hz));
            _running = true;

            _workerThread = new Thread(RunLoop)
            {
                Name = "NX10-ManagedSensorSampler",
                IsBackground = true
            };
            _workerThread.Start();
        }

        public void Stop()
        {
            _running = false;
            Thread thread = _workerThread;
            _workerThread = null;
            if (thread != null && thread.IsAlive && Thread.CurrentThread != thread)
            {
                try
                {
                    thread.Join(50);
                }
                catch
                {
                    // Ignore thread join interruption during domain reload/shutdown
                }
            }
        }

        public void Push(T sample)
        {
            lock (_lock)
            {
                _ring[_writeIdx] = sample;
                _writeIdx = (_writeIdx + 1) % Capacity;
                if (_count == Capacity)
                {
                    _readIdx = (_readIdx + 1) % Capacity;
                }
                else
                {
                    _count++;
                }
            }
        }

        public int DrainToArray(T[] batchBuffer, int maxBatchCount)
        {
            if (batchBuffer == null || maxBatchCount <= 0)
                return 0;

            int cap = Math.Min(maxBatchCount, batchBuffer.Length);
            lock (_lock)
            {
                int toCopy = Math.Min(_count, cap);
                for (int i = 0; i < toCopy; i++)
                {
                    batchBuffer[i] = _ring[_readIdx];
                    _readIdx = (_readIdx + 1) % Capacity;
                }
                _count -= toCopy;
                return toCopy;
            }
        }

        private void RunLoop()
        {
            long freq = Stopwatch.Frequency;
            double ticksPerNs = freq / 1_000_000_000.0;
            long stepTicks = Math.Max(1L, (long)Math.Round(_intervalNs * ticksPerNs));
            long nextTick = Stopwatch.GetTimestamp();

            while (_running)
            {
                long now = Stopwatch.GetTimestamp();
                if (now >= nextTick)
                {
                    try
                    {
                        _onTick?.Invoke();
                    }
                    catch
                    {
                        // Never allow background sampler thread to crash process
                    }

                    nextTick += stepTicks;
                    if (now - nextTick > stepTicks * 4)
                    {
                        nextTick = now + stepTicks;
                    }
                }
                else
                {
                    double remainingMs = (nextTick - now) * 1000.0 / freq;
                    if (remainingMs > 2.0)
                    {
                        Thread.Sleep(1);
                    }
                    else
                    {
                        Thread.SpinWait(64);
                    }
                }
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>
    /// Crash-hardened JNI bridge to com.nx10.sdk.NX10SensorPlugin.
    /// Explicitly checks and clears pending JNI exceptions after every JNI call so libart never aborts Unity.
    /// </summary>
    internal static class AndroidSensorBridge
    {
        private const string PluginClassName = "com/nx10/sdk/NX10SensorPlugin";
        private static readonly jvalue[] EmptyArgs = Array.Empty<jvalue>();

        private static bool _initAttempted;
        private static IntPtr _pluginClass;

        private static IntPtr _getMonotonicTimeMs;

        private static IntPtr _isGyroSupported;
        private static IntPtr _startGyro;
        private static IntPtr _stopGyro;
        private static IntPtr _drainGyroSamples;
        private static IntPtr _getLatestGyro;

        private static IntPtr _isAccelSupported;
        private static IntPtr _startAccel;
        private static IntPtr _stopAccel;
        private static IntPtr _drainAccelSamples;
        private static IntPtr _getLatestAccel;

        private static IntPtr _isMagSupported;
        private static IntPtr _startMag;
        private static IntPtr _stopMag;
        private static IntPtr _drainMagSamples;
        private static IntPtr _getLatestMag;

        private static IntPtr _startTouch;
        private static IntPtr _stopTouch;
        private static IntPtr _drainTouchSamples;
        private static IntPtr _getActiveTouches;

        private static void ThrowIfPendingJniException(string context)
        {
            IntPtr pending = AndroidJNI.ExceptionOccurred();
            if (pending != IntPtr.Zero)
            {
                AndroidJNI.ExceptionClear();
                AndroidJNI.DeleteLocalRef(pending);
                throw new InvalidOperationException($"Pending JNI exception cleared during {context}");
            }
        }

        private static bool EnsureInitialized()
        {
            if (_initAttempted)
            {
                if (_pluginClass == IntPtr.Zero)
                    throw new DllNotFoundException("Android class com.nx10.sdk.NX10SensorPlugin unavailable.");
                return true;
            }

            _initAttempted = true;
            IntPtr localClass = AndroidJNI.FindClass(PluginClassName);
            ThrowIfPendingJniException("FindClass(NX10SensorPlugin)");
            if (localClass == IntPtr.Zero)
            {
                throw new DllNotFoundException("Could not find Android class com.nx10.sdk.NX10SensorPlugin");
            }

            _pluginClass = AndroidJNI.NewGlobalRef(localClass);
            AndroidJNI.DeleteLocalRef(localClass);
            ThrowIfPendingJniException("NewGlobalRef(NX10SensorPlugin)");

            _getMonotonicTimeMs = GetMethod("getMonotonicTimeMs", "()D");

            _isGyroSupported = GetMethod("isGyroSupported", "()Z");
            _startGyro = GetMethod("startGyro", "(I)V");
            _stopGyro = GetMethod("stopGyro", "()V");
            _drainGyroSamples = GetMethod("drainGyroSamples", "()[D");
            _getLatestGyro = GetMethod("getLatestGyro", "()[D");

            _isAccelSupported = GetMethod("isAccelerometerSupported", "()Z");
            _startAccel = GetMethod("startAccelerometer", "(I)V");
            _stopAccel = GetMethod("stopAccelerometer", "()V");
            _drainAccelSamples = GetMethod("drainAccelerometerSamples", "()[D");
            _getLatestAccel = GetMethod("getLatestAccelerometer", "()[D");

            _isMagSupported = GetMethod("isMagnetometerSupported", "()Z");
            _startMag = GetMethod("startMagnetometer", "(I)V");
            _stopMag = GetMethod("stopMagnetometer", "()V");
            _drainMagSamples = GetMethod("drainMagnetometerSamples", "()[D");
            _getLatestMag = GetMethod("getLatestMagnetometer", "()[D");

            _startTouch = GetMethod("startTouch", "(I)V");
            _stopTouch = GetMethod("stopTouch", "()V");
            _drainTouchSamples = GetMethod("drainTouchSamples", "()[D");
            _getActiveTouches = GetMethod("getActiveTouches", "()[D");

            return true;
        }

        private static IntPtr GetMethod(string name, string sig)
        {
            IntPtr id = AndroidJNI.GetStaticMethodID(_pluginClass, name, sig);
            ThrowIfPendingJniException($"GetStaticMethodID({name})");
            if (id == IntPtr.Zero)
            {
                throw new EntryPointNotFoundException($"Missing method {name}{sig} on NX10SensorPlugin");
            }
            return id;
        }

        public static double GetMonotonicTimeMs()
        {
            EnsureInitialized();
            double res = AndroidJNI.CallStaticDoubleMethod(_pluginClass, _getMonotonicTimeMs, EmptyArgs);
            ThrowIfPendingJniException("getMonotonicTimeMs");
            return res;
        }

        public static bool IsGyroSupported() => CallBool(_isGyroSupported, "isGyroSupported");
        public static void StartGyro(int hz) => CallVoidInt(_startGyro, hz, "startGyro");
        public static void StopGyro() => CallVoid(_stopGyro, "stopGyro");
        public static int DrainGyro(NativeSensorSample[] dst, int maxCount) => DrainVec3(_drainGyroSamples, dst, maxCount, "drainGyroSamples");
        public static bool TryGetLatestGyro(out Vector3 value) => TryGetLatestVec3(_getLatestGyro, "getLatestGyro", out value);

        public static bool IsAccelSupported() => CallBool(_isAccelSupported, "isAccelerometerSupported");
        public static void StartAccel(int hz) => CallVoidInt(_startAccel, hz, "startAccelerometer");
        public static void StopAccel() => CallVoid(_stopAccel, "stopAccelerometer");
        public static int DrainAccel(NativeSensorSample[] dst, int maxCount) => DrainVec3(_drainAccelSamples, dst, maxCount, "drainAccelerometerSamples");
        public static bool TryGetLatestAccel(out Vector3 value) => TryGetLatestVec3(_getLatestAccel, "getLatestAccelerometer", out value);

        public static bool IsMagSupported() => CallBool(_isMagSupported, "isMagnetometerSupported");
        public static void StartMag(int hz) => CallVoidInt(_startMag, hz, "startMagnetometer");
        public static void StopMag() => CallVoid(_stopMag, "stopMagnetometer");
        public static int DrainMag(NativeSensorSample[] dst, int maxCount) => DrainVec3(_drainMagSamples, dst, maxCount, "drainMagnetometerSamples");
        public static bool TryGetLatestMag(out Vector3 value) => TryGetLatestVec3(_getLatestMag, "getLatestMagnetometer", out value);

        public static void StartTouch(int hz) => CallVoidInt(_startTouch, hz, "startTouch");
        public static void StopTouch() => CallVoid(_stopTouch, "stopTouch");
        public static int DrainTouch(NativeTouchSample[] dst, int maxCount) => DrainTouchInternal(_drainTouchSamples, dst, maxCount, "drainTouchSamples");
        public static int GetActiveTouches(NativeTouchSample[] dst, int maxCount) => DrainTouchInternal(_getActiveTouches, dst, maxCount, "getActiveTouches");

        private static bool CallBool(IntPtr methodId, string name)
        {
            EnsureInitialized();
            bool res = AndroidJNI.CallStaticBooleanMethod(_pluginClass, methodId, EmptyArgs);
            ThrowIfPendingJniException(name);
            return res;
        }

        private static void CallVoid(IntPtr methodId, string name)
        {
            EnsureInitialized();
            AndroidJNI.CallStaticVoidMethod(_pluginClass, methodId, EmptyArgs);
            ThrowIfPendingJniException(name);
        }

        private static void CallVoidInt(IntPtr methodId, int arg, string name)
        {
            EnsureInitialized();
            jvalue[] args = new jvalue[1];
            args[0].i = arg;
            AndroidJNI.CallStaticVoidMethod(_pluginClass, methodId, args);
            ThrowIfPendingJniException(name);
        }

        private static int DrainVec3(IntPtr methodId, NativeSensorSample[] dst, int maxCount, string name)
        {
            EnsureInitialized();
            if (dst == null || maxCount <= 0)
                return 0;

            IntPtr jArr = AndroidJNI.CallStaticObjectMethod(_pluginClass, methodId, EmptyArgs);
            ThrowIfPendingJniException(name);
            if (jArr == IntPtr.Zero)
                return 0;

            try
            {
                double[] raw = AndroidJNI.FromDoubleArray(jArr);
                ThrowIfPendingJniException($"{name}.FromDoubleArray");
                if (raw == null || raw.Length < 4)
                    return 0;

                int totalSamples = raw.Length / 4;
                int cap = Math.Min(Math.Min(maxCount, dst.Length), totalSamples);
                int startSample = totalSamples - cap; // Keep freshest if larger than batch buffer
                for (int i = 0; i < cap; i++)
                {
                    int b = (startSample + i) * 4;
                    dst[i] = new NativeSensorSample
                    {
                        timestampMs = raw[b],
                        x = (float)raw[b + 1],
                        y = (float)raw[b + 2],
                        z = (float)raw[b + 3]
                    };
                }
                return cap;
            }
            finally
            {
                AndroidJNI.DeleteLocalRef(jArr);
            }
        }

        private static bool TryGetLatestVec3(IntPtr methodId, string name, out Vector3 value)
        {
            value = Vector3.zero;
            EnsureInitialized();

            IntPtr jArr = AndroidJNI.CallStaticObjectMethod(_pluginClass, methodId, EmptyArgs);
            ThrowIfPendingJniException(name);
            if (jArr == IntPtr.Zero)
                return false;

            try
            {
                double[] raw = AndroidJNI.FromDoubleArray(jArr);
                ThrowIfPendingJniException($"{name}.FromDoubleArray");
                if (raw == null || raw.Length < 3)
                    return false;

                value = new Vector3((float)raw[0], (float)raw[1], (float)raw[2]);
                return NativeSensorSampleValidator.IsValidVector(in value);
            }
            finally
            {
                AndroidJNI.DeleteLocalRef(jArr);
            }
        }

        private static int DrainTouchInternal(IntPtr methodId, NativeTouchSample[] dst, int maxCount, string name)
        {
            EnsureInitialized();
            if (dst == null || maxCount <= 0)
                return 0;

            IntPtr jArr = AndroidJNI.CallStaticObjectMethod(_pluginClass, methodId, EmptyArgs);
            ThrowIfPendingJniException(name);
            if (jArr == IntPtr.Zero)
                return 0;

            try
            {
                double[] raw = AndroidJNI.FromDoubleArray(jArr);
                ThrowIfPendingJniException($"{name}.FromDoubleArray");
                if (raw == null || raw.Length < 7)
                    return 0;

                int totalSamples = raw.Length / 7;
                int cap = Math.Min(Math.Min(maxCount, dst.Length), totalSamples);
                int startSample = totalSamples - cap;
                for (int i = 0; i < cap; i++)
                {
                    int b = (startSample + i) * 7;
                    dst[i] = new NativeTouchSample
                    {
                        timestampMs = raw[b],
                        screenX = (float)raw[b + 1],
                        screenY = (float)raw[b + 2],
                        radius = (float)raw[b + 3],
                        touchId = (int)raw[b + 4],
                        phaseCode = (int)raw[b + 5],
                        touchIndex = (int)raw[b + 6]
                    };
                }
                return cap;
            }
            finally
            {
                AndroidJNI.DeleteLocalRef(jArr);
            }
        }
    }
#endif
}
