#import <Foundation/Foundation.h>
#import <CoreMotion/CoreMotion.h>

static CMMotionManager* motionManager = nil;

extern "C" {

    void _StartMagnetometer() {
        if (motionManager == nil) {
            motionManager = [[CMMotionManager alloc] init];
        }

        if ([motionManager isMagnetometerAvailable] && ![motionManager isDeviceMotionActive]) {
            motionManager.magnetometerUpdateInterval = 0.02;
            [motionManager startDeviceMotionUpdatesUsingReferenceFrame:CMAttitudeReferenceFrameXMagneticNorthZVertical];
        }
    }

    void _StopMagnetometer() {
        if (motionManager != nil && [motionManager isDeviceMotionActive]) {
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
if (motionManager != nil && [motionManager isDeviceMotionActive] && motionManager.deviceMotion != nil) {
	    CMCalibratedMagneticField calField = motionManager.deviceMotion.magneticField;
            *x = (float)calField.field.x;
            *y = (float)calField.field.y;
            *z = (float)calField.field.z;
        } else {
            *x = 0.0f;
            *y = 0.0f;
            *z = 0.0f;
        }
    }
}