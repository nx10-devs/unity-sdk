#pragma once

#include <stdint.h>
#include <stdbool.h>
#include <math.h>
#include <os/lock.h>
#include <dispatch/dispatch.h>

#ifdef __OBJC__
#import <Foundation/Foundation.h>
#import <CoreMotion/CoreMotion.h>
#endif

// Catches both Objective-C NSExceptions and C++ exceptions so nothing ever aborts across extern "C" or GCD blocks
#ifdef __OBJC__
#define NX10_GUARD_BEGIN \
    try { \
        @try {
#define NX10_GUARD_END \
        } @catch (id _nx10ObjcEx) { \
            (void)_nx10ObjcEx; \
        } \
    } catch (...) { \
    }
#else
#define NX10_GUARD_BEGIN try {
#define NX10_GUARD_END } catch (...) {}
#endif

// 24-byte naturally aligned sample struct (matches C# NativeSensorSample)
typedef struct NX10Vec3Sample {
    double timestampMs;
    float x;
    float y;
    float z;
    float reserved;
} NX10Vec3Sample;

// 32-byte naturally aligned touch sample struct (matches C# NativeTouchSample)
typedef struct NX10TouchSample {
    double timestampMs;
    float screenX;
    float screenY;
    float radius;
    int32_t touchId;
    int32_t phaseCode;  // 0=down, 1=move, 2=stationary, 3=up, 4=cancelled
    int32_t touchIndex; // 0-based index within the tick snapshot
} NX10TouchSample;

static inline bool NX10IsFiniteVec3(float x, float y, float z) {
    return isfinite(x) && isfinite(y) && isfinite(z);
}

#ifdef __cplusplus
extern "C" {
#endif

double _NX10GetMonotonicTimeMs(void);

#ifdef __cplusplus
}

template <typename T, int Capacity>
class NX10RingBuffer {
public:
    NX10RingBuffer()
        : lock_(OS_UNFAIR_LOCK_INIT), readIdx_(0), writeIdx_(0), count_(0) {}

    void push(const T& item) {
        os_unfair_lock_lock(&lock_);
        buffer_[writeIdx_] = item;
        writeIdx_ = (writeIdx_ + 1) % Capacity;
        if (count_ == Capacity) {
            readIdx_ = (readIdx_ + 1) % Capacity;
        } else {
            count_++;
        }
        os_unfair_lock_unlock(&lock_);
    }

    int drain(T* outBuffer, int maxCount) {
        if (outBuffer == nullptr || maxCount <= 0) {
            return 0;
        }
        if (maxCount > Capacity) {
            maxCount = Capacity;
        }
        os_unfair_lock_lock(&lock_);
        int toCopy = count_ < maxCount ? count_ : maxCount;
        for (int i = 0; i < toCopy; ++i) {
            outBuffer[i] = buffer_[readIdx_];
            readIdx_ = (readIdx_ + 1) % Capacity;
        }
        count_ -= toCopy;
        os_unfair_lock_unlock(&lock_);
        return toCopy;
    }

    void clear() {
        os_unfair_lock_lock(&lock_);
        readIdx_ = 0;
        writeIdx_ = 0;
        count_ = 0;
        os_unfair_lock_unlock(&lock_);
    }

private:
    os_unfair_lock lock_;
    T buffer_[Capacity];
    int readIdx_;
    int writeIdx_;
    int count_;
};

class NX10FixedHzTimer {
public:
    NX10FixedHzTimer(const char* queueLabel)
        : lock_(OS_UNFAIR_LOCK_INIT), timer_(nil), isRunning_(false) {
        dispatch_queue_attr_t attr = dispatch_queue_attr_make_with_qos_class(
            DISPATCH_QUEUE_SERIAL, QOS_CLASS_USER_INTERACTIVE, 0);
        queue_ = dispatch_queue_create(queueLabel, attr);
    }

    ~NX10FixedHzTimer() {
        stop();
    }

    void start(int hz, dispatch_block_t tickBlock) {
        stop();
        if (hz <= 0 || tickBlock == nil || queue_ == nil) {
            return;
        }
        if (hz > 1000) {
            hz = 1000;
        }

        uint64_t intervalNs = (uint64_t)(1000000000.0 / (double)hz + 0.5);
        if (intervalNs < 1000000ULL) {
            intervalNs = 1000000ULL;
        }

        os_unfair_lock_lock(&lock_);
        dispatch_source_t newTimer = dispatch_source_create(DISPATCH_SOURCE_TYPE_TIMER, 0, DISPATCH_TIMER_STRICT, queue_);
        if (newTimer != nil) {
            dispatch_block_t guardedBlock = [tickBlock copy];
            dispatch_source_set_timer(newTimer, dispatch_time(DISPATCH_TIME_NOW, 0), intervalNs, 0);
            dispatch_source_set_event_handler(newTimer, ^{
                NX10_GUARD_BEGIN
                guardedBlock();
                NX10_GUARD_END
            });
            timer_ = newTimer;
            isRunning_ = true;
            dispatch_resume(newTimer);
        }
        os_unfair_lock_unlock(&lock_);
    }

    void stop() {
        dispatch_source_t oldTimer = nil;
        os_unfair_lock_lock(&lock_);
        oldTimer = timer_;
        timer_ = nil;
        isRunning_ = false;
        os_unfair_lock_unlock(&lock_);

        if (oldTimer != nil) {
            dispatch_source_cancel(oldTimer);
        }
    }

    bool isRunning() {
        os_unfair_lock_lock(&lock_);
        bool running = isRunning_;
        os_unfair_lock_unlock(&lock_);
        return running;
    }

private:
    os_unfair_lock lock_;
    dispatch_queue_t queue_;
    dispatch_source_t timer_;
    bool isRunning_;
};
#endif

#ifdef __OBJC__
@interface NX10MotionHub : NSObject

+ (instancetype)sharedInstance;

- (BOOL)isGyroAvailable;
- (BOOL)isAccelerometerAvailable;
- (BOOL)isMagnetometerAvailable;

- (void)setGyroConsumerHz:(int)hz;
- (void)setAccelerometerConsumerHz:(int)hz;
- (void)setMagnetometerConsumerHz:(int)hz;

- (BOOL)getLatestGyroX:(float *)x y:(float *)y z:(float *)z;
- (BOOL)getLatestAccelX:(float *)x y:(float *)y z:(float *)z;
- (BOOL)getLatestMagX:(float *)x y:(float *)y z:(float *)z;

@end
#endif
