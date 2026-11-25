package com.nx10.sdk;

import android.annotation.TargetApi;
import android.app.Activity;
import android.content.Context;
import android.hardware.Sensor;
import android.hardware.SensorEvent;
import android.hardware.SensorEventListener;
import android.hardware.SensorManager;
import android.os.Build;
import android.os.Handler;
import android.os.HandlerThread;
import android.os.Process;
import android.os.SystemClock;
import android.util.Log;
import android.view.ActionMode;
import android.view.KeyEvent;
import android.view.KeyboardShortcutGroup;
import android.view.Menu;
import android.view.MenuItem;
import android.view.MotionEvent;
import android.view.SearchEvent;
import android.view.View;
import android.view.Window;
import android.view.WindowManager;
import android.view.accessibility.AccessibilityEvent;

import com.unity3d.player.UnityPlayer;

import java.lang.ref.WeakReference;
import java.util.List;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.ScheduledFuture;
import java.util.concurrent.ThreadFactory;
import java.util.concurrent.TimeUnit;

/**
 * Crash-hardened, framerate-independent Android sensor and touch collection plugin for NX10 SDK.
 * Every JNI entry point and background worker catches Throwable so no pending JNI exception or
 * background UncaughtExceptionHandler can ever terminate the host game process.
 */
public final class NX10SensorPlugin {

    private static final String TAG = "NX10SensorPlugin";
    private static final int RING_CAPACITY = 4096;
    private static final int MAX_TRACKED_TOUCHES = 16;
    private static final int MAX_ALLOWED_HZ = 1000;

    // Minimum Android 12+ (API 31) sampling period without HIGH_SAMPLING_RATE_SENSORS permission (200 Hz = 5000 us)
    private static final int MIN_SAFE_SAMPLING_PERIOD_US = 5000;

    // Prevents Android's RuntimeInit$KillApplicationHandler from killing the app if a background thread faults
    private static final Thread.UncaughtExceptionHandler SAFE_THREAD_EXCEPTION_HANDLER =
        new Thread.UncaughtExceptionHandler() {
            @Override
            public void uncaughtException(Thread t, Throwable e) {
                try {
                    Log.w(TAG, "Recovered uncaught throwable on " + (t != null ? t.getName() : "worker"), e);
                } catch (Throwable ignored) {}
            }
        };

    private static final Object sInitLock = new Object();
    private static SensorManager sSensorManager;
    private static HandlerThread sHardwareThread;
    private static Handler sHardwareHandler;
    private static ScheduledExecutorService sSamplerExecutor;

    private static final ImuChannel sGyroChannel = new ImuChannel(Sensor.TYPE_GYROSCOPE);
    private static final ImuChannel sAccelChannel = new ImuChannel(Sensor.TYPE_ACCELEROMETER);
    private static final ImuChannel sMagChannel = new ImuChannel(Sensor.TYPE_MAGNETIC_FIELD);

    // Touch tracking state
    private static final Object sTouchLock = new Object();
    private static final TrackedTouch[] sTrackedTouches = new TrackedTouch[MAX_TRACKED_TOUCHES];
    private static final TouchRingBuffer sTouchBuffer = new TouchRingBuffer(RING_CAPACITY);
    private static ScheduledFuture<?> sTouchSamplerFuture;
    private static volatile boolean sTouchSamplingActive = false;
    private static WeakReference<Window> sHookedWindowRef;

    static {
        for (int i = 0; i < MAX_TRACKED_TOUCHES; i++) {
            sTrackedTouches[i] = new TrackedTouch();
        }
    }

    private NX10SensorPlugin() {}

    private static boolean isFiniteFloat(float v) {
        return !Float.isNaN(v) && !Float.isInfinite(v);
    }

    private static boolean isFiniteDouble(double v) {
        return !Double.isNaN(v) && !Double.isInfinite(v);
    }

    private static int clampHz(int hz) {
        if (hz <= 0) return 0;
        return Math.min(hz, MAX_ALLOWED_HZ);
    }

    public static double getMonotonicTimeMs() {
        try {
            double ms = (double) SystemClock.elapsedRealtimeNanos() / 1_000_000.0;
            return isFiniteDouble(ms) && ms >= 0.0 ? ms : 0.0;
        } catch (Throwable ignored) {
            return (double) System.nanoTime() / 1_000_000.0;
        }
    }

    private static Activity getActivity() {
        try {
            return UnityPlayer.currentActivity;
        } catch (Throwable ignored) {
            return null;
        }
    }

    private static SensorManager ensureSensorManager() {
        synchronized (sInitLock) {
            if (sSensorManager == null) {
                Activity activity = getActivity();
                if (activity != null) {
                    Context ctx = activity.getApplicationContext();
                    if (ctx == null) ctx = activity;
                    sSensorManager = (SensorManager) ctx.getSystemService(Context.SENSOR_SERVICE);
                }
            }
            return sSensorManager;
        }
    }

    private static Handler ensureHardwareHandler() {
        synchronized (sInitLock) {
            if (sHardwareThread == null || !sHardwareThread.isAlive()) {
                sHardwareThread = new HandlerThread("NX10-SensorHardware", Process.THREAD_PRIORITY_MORE_FAVORABLE);
                sHardwareThread.setUncaughtExceptionHandler(SAFE_THREAD_EXCEPTION_HANDLER);
                sHardwareThread.start();
                sHardwareHandler = new Handler(sHardwareThread.getLooper());
            }
            return sHardwareHandler;
        }
    }

    private static ScheduledExecutorService ensureSamplerExecutor() {
        synchronized (sInitLock) {
            if (sSamplerExecutor == null || sSamplerExecutor.isShutdown()) {
                sSamplerExecutor = Executors.newScheduledThreadPool(2, new ThreadFactory() {
                    private int mIdx = 0;
                    @Override
                    public synchronized Thread newThread(Runnable r) {
                        Thread t = new Thread(r, "NX10-FixedHzSampler-" + (mIdx++));
                        t.setDaemon(true);
                        t.setPriority(Thread.MAX_PRIORITY - 1);
                        t.setUncaughtExceptionHandler(SAFE_THREAD_EXCEPTION_HANDLER);
                        return t;
                    }
                });
            }
            return sSamplerExecutor;
        }
    }

    // =========================================================================
    // IMU Channels (Gyroscope, Accelerometer, Magnetometer) — all JNI-guarded
    // =========================================================================

    public static boolean isGyroSupported() {
        try { return sGyroChannel.isSupported(); } catch (Throwable ignored) { return false; }
    }

    public static void startGyro(int hz) {
        try { sGyroChannel.start(hz); } catch (Throwable ignored) {}
    }

    public static void stopGyro() {
        try { sGyroChannel.stop(); } catch (Throwable ignored) {}
    }

    public static double[] drainGyroSamples() {
        try { return sGyroChannel.drainSamples(); } catch (Throwable ignored) { return null; }
    }

    public static double[] getLatestGyro() {
        try { return sGyroChannel.getLatest(); } catch (Throwable ignored) { return null; }
    }

    public static boolean isAccelerometerSupported() {
        try { return sAccelChannel.isSupported(); } catch (Throwable ignored) { return false; }
    }

    public static void startAccelerometer(int hz) {
        try { sAccelChannel.start(hz); } catch (Throwable ignored) {}
    }

    public static void stopAccelerometer() {
        try { sAccelChannel.stop(); } catch (Throwable ignored) {}
    }

    public static double[] drainAccelerometerSamples() {
        try { return sAccelChannel.drainSamples(); } catch (Throwable ignored) { return null; }
    }

    public static double[] getLatestAccelerometer() {
        try { return sAccelChannel.getLatest(); } catch (Throwable ignored) { return null; }
    }

    public static boolean isMagnetometerSupported() {
        try { return sMagChannel.isSupported(); } catch (Throwable ignored) { return false; }
    }

    public static void startMagnetometer(int hz) {
        try { sMagChannel.start(hz); } catch (Throwable ignored) {}
    }

    public static void stopMagnetometer() {
        try { sMagChannel.stop(); } catch (Throwable ignored) {}
    }

    public static double[] drainMagnetometerSamples() {
        try { return sMagChannel.drainSamples(); } catch (Throwable ignored) { return null; }
    }

    public static double[] getLatestMagnetometer() {
        try { return sMagChannel.getLatest(); } catch (Throwable ignored) { return null; }
    }

    private static final class ImuChannel implements SensorEventListener, Runnable {
        private final int mSensorType;
        private final Object mLock = new Object();
        private final double[] mRing = new double[RING_CAPACITY * 4];
        private int mReadIdx = 0;
        private int mWriteIdx = 0;
        private int mCount = 0;

        private boolean mHasLatest = false;
        private float mLatestX = 0f;
        private float mLatestY = 0f;
        private float mLatestZ = 0f;

        private boolean mListenerRegistered = false;
        private ScheduledFuture<?> mSamplerFuture;

        ImuChannel(int sensorType) {
            mSensorType = sensorType;
        }

        boolean isSupported() {
            SensorManager sm = ensureSensorManager();
            return sm != null && sm.getDefaultSensor(mSensorType) != null;
        }

        synchronized void start(int rawHz) {
            stop();
            int hz = clampHz(rawHz);
            if (hz <= 0) {
                return;
            }

            SensorManager sm = ensureSensorManager();
            if (sm == null) {
                return;
            }
            Sensor sensor = sm.getDefaultSensor(mSensorType);
            if (sensor == null) {
                return;
            }

            synchronized (mLock) {
                mReadIdx = 0;
                mWriteIdx = 0;
                mCount = 0;
                mHasLatest = false;
            }

            int desiredHardwareHz = Math.max(hz * 2, 60);
            int samplingPeriodUs = Math.max(MIN_SAFE_SAMPLING_PERIOD_US, (int) Math.round(1_000_000.0 / (double) desiredHardwareHz));
            Handler handler = ensureHardwareHandler();

            boolean registered = false;
            try {
                registered = sm.registerListener(this, sensor, samplingPeriodUs, handler);
            } catch (Throwable firstError) {
                try {
                    registered = sm.registerListener(this, sensor, SensorManager.SENSOR_DELAY_GAME, handler);
                } catch (Throwable ignoredFallback) {
                    registered = false;
                }
            }

            mListenerRegistered = registered;
            if (!registered) {
                return;
            }

            long intervalNs = Math.max(1_000_000L, Math.round(1_000_000_000.0 / (double) hz));
            mSamplerFuture = ensureSamplerExecutor().scheduleAtFixedRate(this, 0L, intervalNs, TimeUnit.NANOSECONDS);
        }

        synchronized void stop() {
            if (mSamplerFuture != null) {
                try {
                    mSamplerFuture.cancel(false);
                } catch (Throwable ignored) {}
                mSamplerFuture = null;
            }
            if (mListenerRegistered) {
                SensorManager sm = ensureSensorManager();
                if (sm != null) {
                    try {
                        sm.unregisterListener(this);
                    } catch (Throwable ignored) {}
                }
                mListenerRegistered = false;
            }
            synchronized (mLock) {
                mHasLatest = false;
            }
        }

        @Override
        public void onSensorChanged(SensorEvent event) {
            try {
                if (event == null || event.values == null || event.values.length < 3) {
                    return;
                }
                float x = event.values[0];
                float y = event.values[1];
                float z = event.values[2];
                if (!isFiniteFloat(x) || !isFiniteFloat(y) || !isFiniteFloat(z)) {
                    return;
                }
                synchronized (mLock) {
                    mLatestX = x;
                    mLatestY = y;
                    mLatestZ = z;
                    mHasLatest = true;
                }
            } catch (Throwable ignored) {}
        }

        @Override
        public void onAccuracyChanged(Sensor sensor, int accuracy) {}

        @Override
        public void run() {
            try {
                double nowMs = getMonotonicTimeMs();
                synchronized (mLock) {
                    if (!mHasLatest) {
                        return;
                    }
                    int base = mWriteIdx * 4;
                    mRing[base] = nowMs;
                    mRing[base + 1] = mLatestX;
                    mRing[base + 2] = mLatestY;
                    mRing[base + 3] = mLatestZ;
                    mWriteIdx = (mWriteIdx + 1) % RING_CAPACITY;
                    if (mCount == RING_CAPACITY) {
                        mReadIdx = (mReadIdx + 1) % RING_CAPACITY;
                    } else {
                        mCount++;
                    }
                }
            } catch (Throwable ignored) {}
        }

        double[] drainSamples() {
            synchronized (mLock) {
                if (mCount == 0) {
                    return null;
                }
                double[] out = new double[mCount * 4];
                for (int i = 0; i < mCount; i++) {
                    int src = mReadIdx * 4;
                    int dst = i * 4;
                    out[dst] = mRing[src];
                    out[dst + 1] = mRing[src + 1];
                    out[dst + 2] = mRing[src + 2];
                    out[dst + 3] = mRing[src + 3];
                    mReadIdx = (mReadIdx + 1) % RING_CAPACITY;
                }
                mCount = 0;
                return out;
            }
        }

        double[] getLatest() {
            synchronized (mLock) {
                if (!mHasLatest) {
                    return null;
                }
                return new double[] { mLatestX, mLatestY, mLatestZ };
            }
        }
    }

    // =========================================================================
    // Touch Interception & Fixed-Hz Sampling — all JNI-guarded
    // =========================================================================

    private static final class TrackedTouch {
        boolean inUse;
        int pointerId;
        float screenX;
        float screenY;
        float radiusPx;
        boolean hasEmittedDown;
        boolean movedSinceLastSample;
        int endedState; // 0=active, 3=up, 4=cancelled
    }

    private static final class TouchRingBuffer {
        private final double[] mData;
        private final int mCapacity;
        private int mReadIdx = 0;
        private int mWriteIdx = 0;
        private int mCount = 0;

        TouchRingBuffer(int capacity) {
            mCapacity = capacity;
            mData = new double[capacity * 7];
        }

        void push(double timestampMs, float screenX, float screenY, float radiusPx, int touchId, int phaseCode, int touchIndex) {
            int base = mWriteIdx * 7;
            mData[base] = timestampMs;
            mData[base + 1] = screenX;
            mData[base + 2] = screenY;
            mData[base + 3] = radiusPx;
            mData[base + 4] = touchId;
            mData[base + 5] = phaseCode;
            mData[base + 6] = touchIndex;
            mWriteIdx = (mWriteIdx + 1) % mCapacity;
            if (mCount == mCapacity) {
                mReadIdx = (mReadIdx + 1) % mCapacity;
            } else {
                mCount++;
            }
        }

        double[] drain() {
            if (mCount == 0) {
                return null;
            }
            double[] out = new double[mCount * 7];
            for (int i = 0; i < mCount; i++) {
                int src = mReadIdx * 7;
                int dst = i * 7;
                System.arraycopy(mData, src, out, dst, 7);
                mReadIdx = (mReadIdx + 1) % mCapacity;
            }
            mCount = 0;
            return out;
        }

        void clear() {
            mReadIdx = 0;
            mWriteIdx = 0;
            mCount = 0;
        }
    }

    public static synchronized void startTouch(int rawHz) {
        try {
            stopTouch();
            int hz = clampHz(rawHz);
            if (hz <= 0) {
                return;
            }

            ensureTouchHookInstalled();

            synchronized (sTouchLock) {
                sTouchBuffer.clear();
                sTouchSamplingActive = true;
            }

            long intervalNs = Math.max(1_000_000L, Math.round(1_000_000_000.0 / (double) hz));
            sTouchSamplerFuture = ensureSamplerExecutor().scheduleAtFixedRate(
                NX10SensorPlugin::sampleTouchesOnTimerTick,
                0L,
                intervalNs,
                TimeUnit.NANOSECONDS
            );
        } catch (Throwable ignored) {}
    }

    public static synchronized void stopTouch() {
        try {
            if (sTouchSamplerFuture != null) {
                try {
                    sTouchSamplerFuture.cancel(false);
                } catch (Throwable ignored) {}
                sTouchSamplerFuture = null;
            }
            synchronized (sTouchLock) {
                sTouchSamplingActive = false;
                for (int i = 0; i < MAX_TRACKED_TOUCHES; i++) {
                    if (sTrackedTouches[i].inUse && sTrackedTouches[i].endedState != 0) {
                        sTrackedTouches[i].inUse = false;
                    }
                }
            }
        } catch (Throwable ignored) {}
    }

    public static double[] drainTouchSamples() {
        try {
            ensureTouchHookInstalled();
            synchronized (sTouchLock) {
                return sTouchBuffer.drain();
            }
        } catch (Throwable ignored) {
            return null;
        }
    }

    public static double[] getActiveTouches() {
        try {
            ensureTouchHookInstalled();
            double nowMs = getMonotonicTimeMs();
            synchronized (sTouchLock) {
                int activeCount = 0;
                for (int i = 0; i < MAX_TRACKED_TOUCHES; i++) {
                    if (sTrackedTouches[i].inUse) {
                        activeCount++;
                    }
                }
                if (activeCount == 0) {
                    return null;
                }
                double[] out = new double[activeCount * 7];
                int idx = 0;
                for (int i = 0; i < MAX_TRACKED_TOUCHES; i++) {
                    TrackedTouch t = sTrackedTouches[i];
                    if (!t.inUse) {
                        continue;
                    }
                    int phaseCode = 2;
                    if (!t.hasEmittedDown) phaseCode = 0;
                    else if (t.endedState != 0) phaseCode = t.endedState;
                    else if (t.movedSinceLastSample) phaseCode = 1;

                    int base = idx * 7;
                    out[base] = nowMs;
                    out[base + 1] = t.screenX;
                    out[base + 2] = t.screenY;
                    out[base + 3] = t.radiusPx;
                    out[base + 4] = t.pointerId;
                    out[base + 5] = phaseCode;
                    out[base + 6] = idx;
                    idx++;
                }
                return out;
            }
        } catch (Throwable ignored) {
            return null;
        }
    }

    private static void ensureTouchHookInstalled() {
        try {
            final Activity activity = getActivity();
            if (activity == null) {
                return;
            }

            Window currentHooked = sHookedWindowRef != null ? sHookedWindowRef.get() : null;
            final Window activityWindow = activity.getWindow();
            if (activityWindow == null || currentHooked == activityWindow) {
                return;
            }

            activity.runOnUiThread(new Runnable() {
                @Override
                public void run() {
                    try {
                        Window window = activity.getWindow();
                        if (window == null) {
                            return;
                        }
                        Window.Callback existing = window.getCallback();
                        if (!(existing instanceof NX10WindowCallback)) {
                            window.setCallback(new NX10WindowCallback(window, existing));
                        }
                        sHookedWindowRef = new WeakReference<>(window);
                    } catch (Throwable ignored) {}
                }
            });
        } catch (Throwable ignored) {}
    }

    private static int findTouchSlotLocked(int pointerId) {
        for (int i = 0; i < MAX_TRACKED_TOUCHES; i++) {
            if (sTrackedTouches[i].inUse && sTrackedTouches[i].pointerId == pointerId) {
                return i;
            }
        }
        return -1;
    }

    private static int allocateTouchSlotLocked(int pointerId) {
        for (int i = 0; i < MAX_TRACKED_TOUCHES; i++) {
            if (!sTrackedTouches[i].inUse) {
                TrackedTouch t = sTrackedTouches[i];
                t.inUse = true;
                t.pointerId = pointerId;
                t.hasEmittedDown = false;
                t.movedSinceLastSample = false;
                t.endedState = 0;
                return i;
            }
        }
        return -1;
    }

    static void observeMotionEvent(MotionEvent event, Window window) {
        try {
            if (event == null) {
                return;
            }

            View decorView = window != null ? window.getDecorView() : null;
            float viewHeight = decorView != null ? (float) decorView.getHeight() : 0f;
            if (!isFiniteFloat(viewHeight) || viewHeight < 0f) {
                viewHeight = 0f;
            }

            int actionMasked = event.getActionMasked();
            int actionIndex = event.getActionIndex();
            int pointerCount = event.getPointerCount();

            synchronized (sTouchLock) {
                switch (actionMasked) {
                    case MotionEvent.ACTION_DOWN:
                    case MotionEvent.ACTION_POINTER_DOWN: {
                        if (actionIndex >= 0 && actionIndex < pointerCount) {
                            float sx = event.getX(actionIndex);
                            float sy = viewHeight - event.getY(actionIndex);
                            float rad = event.getTouchMajor(actionIndex) * 0.5f;
                            if (!isFiniteFloat(sx) || !isFiniteFloat(sy)) break;
                            if (!isFiniteFloat(rad) || rad < 0f) rad = 0f;

                            int pointerId = event.getPointerId(actionIndex);
                            int slot = findTouchSlotLocked(pointerId);
                            if (slot < 0) {
                                slot = allocateTouchSlotLocked(pointerId);
                            } else {
                                sTrackedTouches[slot].hasEmittedDown = false;
                                sTrackedTouches[slot].movedSinceLastSample = false;
                                sTrackedTouches[slot].endedState = 0;
                            }
                            if (slot >= 0) {
                                sTrackedTouches[slot].screenX = sx;
                                sTrackedTouches[slot].screenY = sy;
                                sTrackedTouches[slot].radiusPx = rad;
                            }
                        }
                        break;
                    }

                    case MotionEvent.ACTION_MOVE: {
                        for (int i = 0; i < pointerCount; i++) {
                            float sx = event.getX(i);
                            float sy = viewHeight - event.getY(i);
                            float rad = event.getTouchMajor(i) * 0.5f;
                            if (!isFiniteFloat(sx) || !isFiniteFloat(sy)) continue;
                            if (!isFiniteFloat(rad) || rad < 0f) rad = 0f;

                            int pointerId = event.getPointerId(i);
                            int slot = findTouchSlotLocked(pointerId);
                            if (slot < 0) {
                                slot = allocateTouchSlotLocked(pointerId);
                            }
                            if (slot >= 0) {
                                float dx = Math.abs(sx - sTrackedTouches[slot].screenX);
                                float dy = Math.abs(sy - sTrackedTouches[slot].screenY);
                                if (dx > 0.01f || dy > 0.01f) {
                                    sTrackedTouches[slot].movedSinceLastSample = true;
                                }
                                sTrackedTouches[slot].screenX = sx;
                                sTrackedTouches[slot].screenY = sy;
                                sTrackedTouches[slot].radiusPx = rad;
                            }
                        }
                        break;
                    }

                    case MotionEvent.ACTION_POINTER_UP: {
                        if (actionIndex >= 0 && actionIndex < pointerCount) {
                            int pointerId = event.getPointerId(actionIndex);
                            int slot = findTouchSlotLocked(pointerId);
                            if (slot >= 0) {
                                float sx = event.getX(actionIndex);
                                float sy = viewHeight - event.getY(actionIndex);
                                float rad = event.getTouchMajor(actionIndex) * 0.5f;
                                if (isFiniteFloat(sx) && isFiniteFloat(sy)) {
                                    sTrackedTouches[slot].screenX = sx;
                                    sTrackedTouches[slot].screenY = sy;
                                }
                                if (isFiniteFloat(rad) && rad >= 0f) {
                                    sTrackedTouches[slot].radiusPx = rad;
                                }
                                if (sTouchSamplingActive) {
                                    sTrackedTouches[slot].endedState = 3; // up
                                } else {
                                    sTrackedTouches[slot].inUse = false;
                                }
                            }
                        }
                        break;
                    }

                    case MotionEvent.ACTION_UP: {
                        if (actionIndex >= 0 && actionIndex < pointerCount) {
                            int pointerId = event.getPointerId(actionIndex);
                            int slot = findTouchSlotLocked(pointerId);
                            if (slot >= 0) {
                                float sx = event.getX(actionIndex);
                                float sy = viewHeight - event.getY(actionIndex);
                                float rad = event.getTouchMajor(actionIndex) * 0.5f;
                                if (isFiniteFloat(sx) && isFiniteFloat(sy)) {
                                    sTrackedTouches[slot].screenX = sx;
                                    sTrackedTouches[slot].screenY = sy;
                                }
                                if (isFiniteFloat(rad) && rad >= 0f) {
                                    sTrackedTouches[slot].radiusPx = rad;
                                }
                            }
                        }
                        for (int i = 0; i < MAX_TRACKED_TOUCHES; i++) {
                            if (sTrackedTouches[i].inUse) {
                                if (sTouchSamplingActive) {
                                    if (sTrackedTouches[i].endedState == 0) {
                                        sTrackedTouches[i].endedState = 3;
                                    }
                                } else {
                                    sTrackedTouches[i].inUse = false;
                                }
                            }
                        }
                        break;
                    }

                    case MotionEvent.ACTION_CANCEL: {
                        for (int i = 0; i < MAX_TRACKED_TOUCHES; i++) {
                            if (sTrackedTouches[i].inUse) {
                                if (sTouchSamplingActive) {
                                    sTrackedTouches[i].endedState = 4; // cancelled
                                } else {
                                    sTrackedTouches[i].inUse = false;
                                }
                            }
                        }
                        break;
                    }

                    default:
                        break;
                }
            }
        } catch (Throwable ignored) {}
    }

    private static void sampleTouchesOnTimerTick() {
        try {
            double nowMs = getMonotonicTimeMs();
            synchronized (sTouchLock) {
                if (!sTouchSamplingActive) {
                    return;
                }
                int emittedCount = 0;
                for (int i = 0; i < MAX_TRACKED_TOUCHES; i++) {
                    TrackedTouch t = sTrackedTouches[i];
                    if (!t.inUse) {
                        continue;
                    }

                    int phaseCode;
                    if (!t.hasEmittedDown) {
                        phaseCode = 0; // down
                        t.hasEmittedDown = true;
                        t.movedSinceLastSample = false;
                    } else if (t.endedState != 0) {
                        phaseCode = t.endedState; // 3=up, 4=cancelled
                        t.inUse = false;
                    } else if (t.movedSinceLastSample) {
                        phaseCode = 1; // move
                        t.movedSinceLastSample = false;
                    } else {
                        phaseCode = 2; // stationary
                    }

                    sTouchBuffer.push(nowMs, t.screenX, t.screenY, t.radiusPx, t.pointerId, phaseCode, emittedCount);
                    emittedCount++;
                }
            }
        } catch (Throwable ignored) {}
    }

    private static final class NX10WindowCallback implements Window.Callback {
        private final Window mWindow;
        private final Window.Callback mDelegate;

        NX10WindowCallback(Window window, Window.Callback delegate) {
            mWindow = window;
            mDelegate = delegate;
        }

        @Override
        public boolean dispatchTouchEvent(MotionEvent event) {
            try {
                observeMotionEvent(event, mWindow);
            } catch (Throwable ignored) {}
            return mDelegate != null ? mDelegate.dispatchTouchEvent(event) : mWindow.superDispatchTouchEvent(event);
        }

        @Override
        public boolean dispatchKeyEvent(KeyEvent event) {
            return mDelegate != null ? mDelegate.dispatchKeyEvent(event) : mWindow.superDispatchKeyEvent(event);
        }

        @Override
        public boolean dispatchKeyShortcutEvent(KeyEvent event) {
            return mDelegate != null ? mDelegate.dispatchKeyShortcutEvent(event) : mWindow.superDispatchKeyShortcutEvent(event);
        }

        @Override
        public boolean dispatchTrackballEvent(MotionEvent event) {
            return mDelegate != null ? mDelegate.dispatchTrackballEvent(event) : mWindow.superDispatchTrackballEvent(event);
        }

        @Override
        public boolean dispatchGenericMotionEvent(MotionEvent event) {
            return mDelegate != null ? mDelegate.dispatchGenericMotionEvent(event) : mWindow.superDispatchGenericMotionEvent(event);
        }

        @Override
        public boolean dispatchPopulateAccessibilityEvent(AccessibilityEvent event) {
            return mDelegate != null && mDelegate.dispatchPopulateAccessibilityEvent(event);
        }

        @Override
        public View onCreatePanelView(int featureId) {
            return mDelegate != null ? mDelegate.onCreatePanelView(featureId) : null;
        }

        @Override
        public boolean onCreatePanelMenu(int featureId, Menu menu) {
            return mDelegate != null && mDelegate.onCreatePanelMenu(featureId, menu);
        }

        @Override
        public boolean onPreparePanel(int featureId, View view, Menu menu) {
            return mDelegate != null && mDelegate.onPreparePanel(featureId, view, menu);
        }

        @Override
        public boolean onMenuOpened(int featureId, Menu menu) {
            return mDelegate != null && mDelegate.onMenuOpened(featureId, menu);
        }

        @Override
        public boolean onMenuItemSelected(int featureId, MenuItem item) {
            return mDelegate != null && mDelegate.onMenuItemSelected(featureId, item);
        }

        @Override
        public void onWindowAttributesChanged(WindowManager.LayoutParams attrs) {
            if (mDelegate != null) mDelegate.onWindowAttributesChanged(attrs);
        }

        @Override
        public void onContentChanged() {
            if (mDelegate != null) mDelegate.onContentChanged();
        }

        @Override
        public void onWindowFocusChanged(boolean hasFocus) {
            if (mDelegate != null) mDelegate.onWindowFocusChanged(hasFocus);
        }

        @Override
        public void onAttachedToWindow() {
            if (mDelegate != null) mDelegate.onAttachedToWindow();
        }

        @Override
        public void onDetachedFromWindow() {
            if (mDelegate != null) mDelegate.onDetachedFromWindow();
        }

        @Override
        public void onPanelClosed(int featureId, Menu menu) {
            if (mDelegate != null) mDelegate.onPanelClosed(featureId, menu);
        }

        @Override
        public boolean onSearchRequested() {
            return mDelegate != null && mDelegate.onSearchRequested();
        }

        @TargetApi(Build.VERSION_CODES.M)
        @Override
        public boolean onSearchRequested(SearchEvent searchEvent) {
            return mDelegate != null && Build.VERSION.SDK_INT >= Build.VERSION_CODES.M && mDelegate.onSearchRequested(searchEvent);
        }

        @Override
        public ActionMode onWindowStartingActionMode(ActionMode.Callback callback) {
            return mDelegate != null ? mDelegate.onWindowStartingActionMode(callback) : null;
        }

        @TargetApi(Build.VERSION_CODES.M)
        @Override
        public ActionMode onWindowStartingActionMode(ActionMode.Callback callback, int type) {
            return (mDelegate != null && Build.VERSION.SDK_INT >= Build.VERSION_CODES.M)
                ? mDelegate.onWindowStartingActionMode(callback, type)
                : null;
        }

        @Override
        public void onActionModeStarted(ActionMode mode) {
            if (mDelegate != null) mDelegate.onActionModeStarted(mode);
        }

        @Override
        public void onActionModeFinished(ActionMode mode) {
            if (mDelegate != null) mDelegate.onActionModeFinished(mode);
        }

        @TargetApi(Build.VERSION_CODES.N)
        @Override
        public void onProvideKeyboardShortcuts(List<KeyboardShortcutGroup> data, Menu menu, int deviceId) {
            if (mDelegate != null && Build.VERSION.SDK_INT >= Build.VERSION_CODES.N) {
                mDelegate.onProvideKeyboardShortcuts(data, menu, deviceId);
            }
        }

        @TargetApi(Build.VERSION_CODES.O)
        @Override
        public void onPointerCaptureChanged(boolean hasCapture) {
            if (mDelegate != null && Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                mDelegate.onPointerCaptureChanged(hasCapture);
            }
        }
    }
}
