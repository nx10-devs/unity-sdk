#import "NX10SensorCore.h"

static NX10RingBuffer<NX10Vec3Sample, 4096> g_magBuffer;
static NX10FixedHzTimer g_magTimer("com.nx10.sdk.magSampler");

extern "C" {

    bool _NX10IsMagnetometerAvailable() {
        bool result = false;
        NX10_GUARD_BEGIN
        result = [[NX10MotionHub sharedInstance] isMagnetometerAvailable];
        NX10_GUARD_END
        return result;
    }

    void _NX10StartMagnetometer(int hz) {
        NX10_GUARD_BEGIN
        g_magTimer.stop();
        g_magBuffer.clear();

        NX10MotionHub* hub = [NX10MotionHub sharedInstance];
        if (hub == nil || hz <= 0 || ![hub isMagnetometerAvailable]) {
            [hub setMagnetometerConsumerHz:0];
            return;
        }

        [hub setMagnetometerConsumerHz:hz];
        g_magTimer.start(hz, ^{
            float x = 0.0f, y = 0.0f, z = 0.0f;
            if ([[NX10MotionHub sharedInstance] getLatestMagX:&x y:&y z:&z] && NX10IsFiniteVec3(x, y, z)) {
                NX10Vec3Sample sample;
                sample.timestampMs = _NX10GetMonotonicTimeMs();
                sample.x = x;
                sample.y = y;
                sample.z = z;
                sample.reserved = 0.0f;
                g_magBuffer.push(sample);
            }
        });
        NX10_GUARD_END
    }

    void _NX10StopMagnetometer() {
        NX10_GUARD_BEGIN
        g_magTimer.stop();
        [[NX10MotionHub sharedInstance] setMagnetometerConsumerHz:0];
        NX10_GUARD_END
    }

    int _NX10DrainMagnetometerSamples(NX10Vec3Sample* outBuffer, int maxCount) {
        int drained = 0;
        NX10_GUARD_BEGIN
        drained = g_magBuffer.drain(outBuffer, maxCount);
        NX10_GUARD_END
        return drained;
    }

    bool _NX10GetLatestMagnetometer(float* x, float* y, float* z) {
        bool ok = false;
        NX10_GUARD_BEGIN
        ok = [[NX10MotionHub sharedInstance] getLatestMagX:x y:y z:z];
        NX10_GUARD_END
        return ok;
    }

    void _StartMagnetometer() {
        NX10_GUARD_BEGIN
        [[NX10MotionHub sharedInstance] setMagnetometerConsumerHz:50];
        NX10_GUARD_END
    }

    void _StopMagnetometer() {
        _NX10StopMagnetometer();
    }

    bool _IsMagnetometerAvailable() {
        return _NX10IsMagnetometerAvailable();
    }

    void _GetMagnetometerData(float* x, float* y, float* z) {
        _NX10GetLatestMagnetometer(x, y, z);
    }
}