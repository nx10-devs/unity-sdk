#import <Foundation/Foundation.h>
#import <CoreMotion/CoreMotion.h>

static CMMotionManager* motionManager = nil;

extern "C" {

    void _StartMagnetometer() {
        if (motionManager == nil) {
            motionManager = [[CMMotionManager alloc] init];
        }

        if ([motionManager isMagnetometerAvailable] && ![motionManager isMagnetometerActive]) {
            motionManager.magnetometerUpdateInterval = 0.02;
            [motionManager startMagnetometerUpdates];
        }
    }

    void _StopMagnetometer() {
        if (motionManager != nil && [motionManager isMagnetometerActive]) {
            [motionManager stopMagnetometerUpdates];
        }
    }

    bool _IsMagnetometerAvailable() {
        if (motionManager == nil) {
            motionManager = [[CMMotionManager alloc] init];
        }
        return [motionManager isMagnetometerAvailable];
    }

    void _GetMagnetometerData(float* x, float* y, float* z) {
        if (motionManager != nil && [motionManager isMagnetometerActive]) {
            CMMagneticField field = motionManager.magnetometerData.magneticField;
            *x = (float)field.x;
            *y = (float)field.y;
            *z = (float)field.z;
        } else {
            *x = 0.0f;
            *y = 0.0f;
            *z = 0.0f;
        }
    }
}