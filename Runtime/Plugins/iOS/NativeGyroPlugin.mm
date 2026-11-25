#import "NX10SensorCore.h"

struct NativeVector3 {
    float x;
    float y;
    float z;
};

static NX10RingBuffer<NX10Vec3Sample, 4096> g_gyroBuffer;
static NX10FixedHzTimer g_gyroTimer("com.nx10.sdk.gyroSampler");

extern "C" {

    bool _NX10IsGyroAvailable() {
        bool result = false;
        NX10_GUARD_BEGIN
        result = [[NX10MotionHub sharedInstance] isGyroAvailable];
        NX10_GUARD_END
        return result;
    }

    void _NX10StartGyro(int hz) {
        NX10_GUARD_BEGIN
        g_gyroTimer.stop();
        g_gyroBuffer.clear();

        NX10MotionHub* hub = [NX10MotionHub sharedInstance];
        if (hub == nil || hz <= 0 || ![hub isGyroAvailable]) {
            [hub setGyroConsumerHz:0];
            return;
        }

        [hub setGyroConsumerHz:hz];
        g_gyroTimer.start(hz, ^{
            float x = 0.0f, y = 0.0f, z = 0.0f;
            if ([[NX10MotionHub sharedInstance] getLatestGyroX:&x y:&y z:&z] && NX10IsFiniteVec3(x, y, z)) {
                NX10Vec3Sample sample;
                sample.timestampMs = _NX10GetMonotonicTimeMs();
                sample.x = x;
                sample.y = y;
                sample.z = z;
                sample.reserved = 0.0f;
                g_gyroBuffer.push(sample);
            }
        });
        NX10_GUARD_END
    }

    void _NX10StopGyro() {
        NX10_GUARD_BEGIN
        g_gyroTimer.stop();
        [[NX10MotionHub sharedInstance] setGyroConsumerHz:0];
        NX10_GUARD_END
    }

    int _NX10DrainGyroSamples(NX10Vec3Sample* outBuffer, int maxCount) {
        int drained = 0;
        NX10_GUARD_BEGIN
        drained = g_gyroBuffer.drain(outBuffer, maxCount);
        NX10_GUARD_END
        return drained;
    }

    bool _NX10GetLatestGyro(float* x, float* y, float* z) {
        bool ok = false;
        NX10_GUARD_BEGIN
        ok = [[NX10MotionHub sharedInstance] getLatestGyroX:x y:y z:z];
        NX10_GUARD_END
        return ok;
    }

    void _StartNativeGyro() {
        _NX10StartGyro(60);
    }

    void _StopNativeGyro() {
        _NX10StopGyro();
    }

    struct NativeVector3 _GetNativeRotationRateUnbiased() {
        struct NativeVector3 result = {0.0f, 0.0f, 0.0f};
        _NX10GetLatestGyro(&result.x, &result.y, &result.z);
        return result;
    }
}