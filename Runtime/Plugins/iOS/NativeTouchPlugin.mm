#import "NX10SensorCore.h"
#import <UIKit/UIKit.h>
#import <objc/runtime.h>

static const int kMaxTrackedTouches = 16;

typedef struct NX10TrackedTouch {
    bool inUse;
    const void* touchPtr;
    int32_t touchId;
    float screenX;
    float screenY;
    float radiusPoints;
    bool hasEmittedDown;
    bool movedSinceLastSample;
    int32_t endedState; // 0=active, 3=up, 4=cancelled
} NX10TrackedTouch;

static os_unfair_lock g_touchLock = OS_UNFAIR_LOCK_INIT;
static NX10TrackedTouch g_trackedTouches[kMaxTrackedTouches];
static int32_t g_nextTouchId = 1;
static float g_cachedNativeScale = 0.0f;
static CGFloat g_lastKnownViewHeight = 0.0;
static IMP g_originalSendEvent = NULL;

static NX10RingBuffer<NX10TouchSample, 4096> g_touchBuffer;
static NX10FixedHzTimer g_touchTimer("com.nx10.sdk.touchSampler");

static float NX10ResolveNativeScale(UIWindow* window, UIView* view) {
    if (window != nil && window.screen != nil) {
        float s = (float)window.screen.nativeScale;
        if (isfinite(s) && s > 0.0f) {
            g_cachedNativeScale = s;
            return s;
        }
    }
    if (g_cachedNativeScale > 0.0f) {
        return g_cachedNativeScale;
    }
    if ([NSThread isMainThread] && [UIScreen instancesRespondToSelector:@selector(nativeScale)]) {
        float s = (float)[UIScreen mainScreen].nativeScale;
        if (isfinite(s) && s > 0.0f) {
            g_cachedNativeScale = s;
            return s;
        }
    }
    if (view != nil && view.contentScaleFactor > 0.0) {
        float s = (float)view.contentScaleFactor;
        if (isfinite(s) && s > 0.0f) {
            g_cachedNativeScale = s;
            return s;
        }
    }
    return 1.0f;
}

static int NX10FindTouchSlotLocked(const void* ptr) {
    for (int i = 0; i < kMaxTrackedTouches; ++i) {
        if (g_trackedTouches[i].inUse && g_trackedTouches[i].touchPtr == ptr) {
            return i;
        }
    }
    return -1;
}

static int NX10AllocateTouchSlotLocked(const void* ptr) {
    for (int i = 0; i < kMaxTrackedTouches; ++i) {
        if (!g_trackedTouches[i].inUse) {
            g_trackedTouches[i].inUse = true;
            g_trackedTouches[i].touchPtr = ptr;
            g_trackedTouches[i].touchId = g_nextTouchId++;
            if (g_nextTouchId > 1000000) {
                g_nextTouchId = 1;
            }
            g_trackedTouches[i].hasEmittedDown = false;
            g_trackedTouches[i].movedSinceLastSample = false;
            g_trackedTouches[i].endedState = 0;
            return i;
        }
    }
    return -1;
}

static void NX10ObserveTouchEvent(UIEvent* event) {
    NSSet<UITouch*>* allTouches = [event allTouches];
    if (allTouches == nil || allTouches.count == 0) {
        return;
    }

    bool samplingActive = g_touchTimer.isRunning();

    for (UITouch* touch in allTouches) {
        if (touch == nil) {
            continue;
        }

        UITouchPhase phase = touch.phase;
        if (phase != UITouchPhaseBegan &&
            phase != UITouchPhaseMoved &&
            phase != UITouchPhaseStationary &&
            phase != UITouchPhaseEnded &&
            phase != UITouchPhaseCancelled) {
            continue;
        }

        UIWindow* window = touch.window;
        UIView* refView = window != nil ? (window.rootViewController.view ?: window) : nil;
        CGFloat viewHeight = refView != nil ? refView.bounds.size.height : g_lastKnownViewHeight;
        if (viewHeight > 0.0 && isfinite(viewHeight)) {
            g_lastKnownViewHeight = viewHeight;
        } else if ([NSThread isMainThread]) {
            viewHeight = [UIScreen mainScreen].bounds.size.height;
        }

        const void* ptr = (__bridge const void*)touch;

        // If a window was torn down mid-touch (e.g. closing an overlay ad window), touch.window can be nil on Cancelled/Ended
        if (refView == nil && (phase == UITouchPhaseEnded || phase == UITouchPhaseCancelled)) {
            os_unfair_lock_lock(&g_touchLock);
            int slot = NX10FindTouchSlotLocked(ptr);
            if (slot >= 0) {
                if (samplingActive) {
                    g_trackedTouches[slot].endedState = (phase == UITouchPhaseEnded) ? 3 : 4;
                } else {
                    g_trackedTouches[slot].inUse = false;
                    g_trackedTouches[slot].touchPtr = nullptr;
                }
            }
            os_unfair_lock_unlock(&g_touchLock);
            continue;
        }

        CGPoint pt = [touch locationInView:refView];
        float scale = NX10ResolveNativeScale(window, refView);
        float sx = (float)(pt.x * scale);
        float sy = (float)((viewHeight - pt.y) * scale);
        float radPts = (float)touch.majorRadius;
        if (!isfinite(radPts) || radPts < 0.0f) {
            radPts = 0.0f;
        }
        if (!isfinite(sx) || !isfinite(sy)) {
            continue;
        }

        os_unfair_lock_lock(&g_touchLock);
        int slot = NX10FindTouchSlotLocked(ptr);

        if (phase == UITouchPhaseBegan) {
            if (slot < 0) {
                slot = NX10AllocateTouchSlotLocked(ptr);
            } else {
                g_trackedTouches[slot].hasEmittedDown = false;
                g_trackedTouches[slot].movedSinceLastSample = false;
                g_trackedTouches[slot].endedState = 0;
            }
            if (slot >= 0) {
                g_trackedTouches[slot].screenX = sx;
                g_trackedTouches[slot].screenY = sy;
                g_trackedTouches[slot].radiusPoints = radPts;
            }
        } else if (phase == UITouchPhaseMoved || phase == UITouchPhaseStationary) {
            if (slot < 0) {
                slot = NX10AllocateTouchSlotLocked(ptr);
            }
            if (slot >= 0) {
                float dx = fabsf(sx - g_trackedTouches[slot].screenX);
                float dy = fabsf(sy - g_trackedTouches[slot].screenY);
                if (phase == UITouchPhaseMoved && (dx > 0.01f || dy > 0.01f)) {
                    g_trackedTouches[slot].movedSinceLastSample = true;
                }
                g_trackedTouches[slot].screenX = sx;
                g_trackedTouches[slot].screenY = sy;
                g_trackedTouches[slot].radiusPoints = radPts;
            }
        } else if (phase == UITouchPhaseEnded || phase == UITouchPhaseCancelled) {
            if (slot >= 0) {
                g_trackedTouches[slot].screenX = sx;
                g_trackedTouches[slot].screenY = sy;
                g_trackedTouches[slot].radiusPoints = radPts;
                if (samplingActive) {
                    g_trackedTouches[slot].endedState = (phase == UITouchPhaseEnded) ? 3 : 4;
                } else {
                    g_trackedTouches[slot].inUse = false;
                    g_trackedTouches[slot].touchPtr = nullptr;
                }
            }
        }
        os_unfair_lock_unlock(&g_touchLock);
    }
}

static void NX10SwizzledSendEvent(id self, SEL _cmd, UIEvent* event) {
    // Isolate NX10 touch observation completely so g_originalSendEvent is ALWAYS called unconditionally
    NX10_GUARD_BEGIN
    if (event != nil && event.type == UIEventTypeTouches) {
        NX10ObserveTouchEvent(event);
    }
    NX10_GUARD_END

    if (g_originalSendEvent != NULL) {
        ((void(*)(id, SEL, UIEvent*))g_originalSendEvent)(self, _cmd, event);
    }
}

@interface NX10TouchEventHook : NSObject
@end

@implementation NX10TouchEventHook
+ (void)load {
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        NX10_GUARD_BEGIN
        Class appClass = [UIApplication class];
        SEL sel = @selector(sendEvent:);
        Method method = class_getInstanceMethod(appClass, sel);
        if (method != NULL) {
            g_originalSendEvent = method_setImplementation(method, (IMP)NX10SwizzledSendEvent);
        }
        NX10_GUARD_END
    });
}
@end

static void NX10SampleTouchesOnTimerTick(void) {
    NX10TouchSample localSamples[kMaxTrackedTouches];
    int emittedCount = 0;
    double nowMs = _NX10GetMonotonicTimeMs();

    os_unfair_lock_lock(&g_touchLock);
    for (int i = 0; i < kMaxTrackedTouches; ++i) {
        if (!g_trackedTouches[i].inUse) {
            continue;
        }

        NX10TrackedTouch& t = g_trackedTouches[i];
        int32_t phaseCode = 2; // stationary

        if (!t.hasEmittedDown) {
            phaseCode = 0; // down
            t.hasEmittedDown = true;
            t.movedSinceLastSample = false;
        } else if (t.endedState != 0) {
            phaseCode = t.endedState; // 3=up, 4=cancelled
            t.inUse = false;
            t.touchPtr = nullptr;
        } else if (t.movedSinceLastSample) {
            phaseCode = 1; // move
            t.movedSinceLastSample = false;
        } else {
            phaseCode = 2; // stationary
        }

        NX10TouchSample& s = localSamples[emittedCount];
        s.timestampMs = nowMs;
        s.screenX = t.screenX;
        s.screenY = t.screenY;
        s.radius = t.radiusPoints;
        s.touchId = t.touchId;
        s.phaseCode = phaseCode;
        s.touchIndex = emittedCount;
        emittedCount++;
    }
    os_unfair_lock_unlock(&g_touchLock);

    for (int i = 0; i < emittedCount; ++i) {
        g_touchBuffer.push(localSamples[i]);
    }
}

extern "C" {

    void _NX10StartTouch(int hz) {
        NX10_GUARD_BEGIN
        g_touchTimer.stop();
        g_touchBuffer.clear();

        if (hz <= 0) {
            return;
        }

        g_touchTimer.start(hz, ^{
            NX10SampleTouchesOnTimerTick();
        });
        NX10_GUARD_END
    }

    void _NX10StopTouch() {
        NX10_GUARD_BEGIN
        g_touchTimer.stop();
        os_unfair_lock_lock(&g_touchLock);
        for (int i = 0; i < kMaxTrackedTouches; ++i) {
            if (g_trackedTouches[i].inUse && g_trackedTouches[i].endedState != 0) {
                g_trackedTouches[i].inUse = false;
                g_trackedTouches[i].touchPtr = nullptr;
            }
        }
        os_unfair_lock_unlock(&g_touchLock);
        NX10_GUARD_END
    }

    int _NX10DrainTouchSamples(NX10TouchSample* outBuffer, int maxCount) {
        int drained = 0;
        NX10_GUARD_BEGIN
        drained = g_touchBuffer.drain(outBuffer, maxCount);
        NX10_GUARD_END
        return drained;
    }

    int _NX10GetActiveTouches(NX10TouchSample* outBuffer, int maxCount) {
        int count = 0;
        NX10_GUARD_BEGIN
        if (outBuffer == nullptr || maxCount <= 0) {
            return 0;
        }
        if (maxCount > kMaxTrackedTouches) {
            maxCount = kMaxTrackedTouches;
        }
        double nowMs = _NX10GetMonotonicTimeMs();
        os_unfair_lock_lock(&g_touchLock);
        for (int i = 0; i < kMaxTrackedTouches && count < maxCount; ++i) {
            if (!g_trackedTouches[i].inUse) {
                continue;
            }
            const NX10TrackedTouch& t = g_trackedTouches[i];
            int32_t phaseCode = 2;
            if (!t.hasEmittedDown) phaseCode = 0;
            else if (t.endedState != 0) phaseCode = t.endedState;
            else if (t.movedSinceLastSample) phaseCode = 1;

            outBuffer[count].timestampMs = nowMs;
            outBuffer[count].screenX = t.screenX;
            outBuffer[count].screenY = t.screenY;
            outBuffer[count].radius = t.radiusPoints;
            outBuffer[count].touchId = t.touchId;
            outBuffer[count].phaseCode = phaseCode;
            outBuffer[count].touchIndex = count;
            count++;
        }
        os_unfair_lock_unlock(&g_touchLock);
        NX10_GUARD_END
        return count;
    }
}
