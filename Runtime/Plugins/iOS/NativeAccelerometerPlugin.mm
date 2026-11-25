#import "NX10SensorCore.h"

static NX10RingBuffer<NX10Vec3Sample, 4096> g_accelBuffer;
static NX10FixedHzTimer g_accelTimer("com.nx10.sdk.accelSampler");

extern "C" {

    bool _NX10IsAccelerometerAvailable() {
        bool result = false;
        NX10_GUARD_BEGIN
        result = [[NX10MotionHub sharedInstance] isAccelerometerAvailable];
        NX10_GUARD_END
        return result;
    }

    void _NX10StartAccelerometer(int hz) {
        NX10_GUARD_BEGIN
        g_accelTimer.stop();
        g_accelBuffer.clear();

        NX10MotionHub* hub = [NX10MotionHub sharedInstance];
        if (hub == nil || hz <= 0 || ![hub isAccelerometerAvailable]) {
            [hub setAccelerometerConsumerHz:0];
            return;
        }

        [hub setAccelerometerConsumerHz:hz];
        g_accelTimer.start(hz, ^{
            float x = 0.0f, y = 0.0f, z = 0.0f;
            if ([[NX10MotionHub sharedInstance] getLatestAccelX:&x y:&y z:&z] && NX10IsFiniteVec3(x, y, z)) {
                NX10Vec3Sample sample;
                sample.timestampMs = _NX10GetMonotonicTimeMs();
                sample.x = x;
                sample.y = y;
                sample.z = z;
                sample.reserved = 0.0f;
                g_accelBuffer.push(sample);
            }
        });
        NX10_GUARD_END
    }

    void _NX10StopAccelerometer() {
        NX10_GUARD_BEGIN
        g_accelTimer.stop();
        [[NX10MotionHub sharedInstance] setAccelerometerConsumerHz:0];
        NX10_GUARD_END
    }

    int _NX10DrainAccelerometerSamples(NX10Vec3Sample* outBuffer, int maxCount) {
        int drained = 0;
        NX10_GUARD_BEGIN
        drained = g_accelBuffer.drain(outBuffer, maxCount);
        NX10_GUARD_END
        return drained;
    }

    bool _NX10GetLatestAccelerometer(float* x, float* y, float* z) {
        bool ok = false;
        NX10_GUARD_BEGIN
        ok = [[NX10MotionHub sharedInstance] getLatestAccelX:x y:y z:z];
        NX10_GUARD_END
        return ok;
    }
}
