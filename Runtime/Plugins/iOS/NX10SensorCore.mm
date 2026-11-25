#import "NX10SensorCore.h"
#include <time.h>

static const float kGravityMetersPerSecSq = 9.80665f;

extern "C" double _NX10GetMonotonicTimeMs(void) {
    double result = 0.0;
    NX10_GUARD_BEGIN
    uint64_t ns = clock_gettime_nsec_np(CLOCK_MONOTONIC_RAW);
    result = (double)ns / 1000000.0;
    if (!isfinite(result) || result < 0.0) {
        result = 0.0;
    }
    NX10_GUARD_END
    return result;
}

@implementation NX10MotionHub {
    CMMotionManager *_motionManager;
    NSOperationQueue *_sensorQueue;
    os_unfair_lock _stateLock;

    int _gyroHz;
    int _accelHz;
    int _magHz;
    CMAttitudeReferenceFrame _activeRefFrame;
    BOOL _deviceMotionRunning;

    BOOL _hasGyro;
    float _gyroX, _gyroY, _gyroZ;

    BOOL _hasAccel;
    float _accelX, _accelY, _accelZ;

    BOOL _hasMag;
    float _magX, _magY, _magZ;
}

+ (instancetype)sharedInstance {
    static NX10MotionHub *instance = nil;
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        NX10_GUARD_BEGIN
        instance = [[NX10MotionHub alloc] init];
        NX10_GUARD_END
    });
    return instance;
}

- (instancetype)init {
    self = [super init];
    if (self) {
        NX10_GUARD_BEGIN
        _motionManager = [[CMMotionManager alloc] init];
        _sensorQueue = [[NSOperationQueue alloc] init];
        _sensorQueue.name = @"com.nx10.sdk.motionQueue";
        _sensorQueue.maxConcurrentOperationCount = 1;
        _sensorQueue.qualityOfService = NSQualityOfServiceUserInteractive;
        _stateLock = OS_UNFAIR_LOCK_INIT;
        _activeRefFrame = CMAttitudeReferenceFrameXArbitraryZVertical;
        _deviceMotionRunning = NO;
        NX10_GUARD_END
    }
    return self;
}

- (BOOL)isGyroAvailable {
    BOOL available = NO;
    NX10_GUARD_BEGIN
    available = _motionManager != nil && (_motionManager.isDeviceMotionAvailable || _motionManager.isGyroAvailable);
    NX10_GUARD_END
    return available;
}

- (BOOL)isAccelerometerAvailable {
    BOOL available = NO;
    NX10_GUARD_BEGIN
    available = _motionManager != nil && _motionManager.isAccelerometerAvailable;
    NX10_GUARD_END
    return available;
}

- (BOOL)isMagnetometerAvailable {
    BOOL available = NO;
    NX10_GUARD_BEGIN
    available = _motionManager != nil && _motionManager.isMagnetometerAvailable;
    NX10_GUARD_END
    return available;
}

static NSTimeInterval NX10HardwareIntervalForHz(int requestedHz) {
    if (requestedHz <= 0) requestedHz = 1;
    if (requestedHz > 1000) requestedHz = 1000;
    int targetHz = requestedHz * 2;
    if (targetHz < 60) targetHz = 60;
    if (targetHz > 200) targetHz = 200;
    return 1.0 / (double)targetHz;
}

- (void)setAccelerometerConsumerHz:(int)hz {
    NX10_GUARD_BEGIN
    if (_motionManager == nil || _sensorQueue == nil) return;

    @synchronized (self) {
        _accelHz = hz > 0 ? (hz > 1000 ? 1000 : hz) : 0;
        if (_accelHz > 0 && _motionManager.isAccelerometerAvailable) {
            _motionManager.accelerometerUpdateInterval = NX10HardwareIntervalForHz(_accelHz);
            if (!_motionManager.isAccelerometerActive) {
                __weak __typeof__(self) weakSelf = self;
                [_motionManager startAccelerometerUpdatesToQueue:_sensorQueue
                                                     withHandler:^(CMAccelerometerData *data, NSError *error) {
                    NX10_GUARD_BEGIN
                    __strong __typeof__(weakSelf) strongSelf = weakSelf;
                    if (!strongSelf || !data || error) return;
                    CMAcceleration acc = data.acceleration;
                    float ax = (float)acc.x * -kGravityMetersPerSecSq;
                    float ay = (float)acc.y * -kGravityMetersPerSecSq;
                    float az = (float)acc.z * -kGravityMetersPerSecSq;
                    if (!NX10IsFiniteVec3(ax, ay, az)) return;

                    os_unfair_lock_lock(&strongSelf->_stateLock);
                    strongSelf->_accelX = ax;
                    strongSelf->_accelY = ay;
                    strongSelf->_accelZ = az;
                    strongSelf->_hasAccel = YES;
                    os_unfair_lock_unlock(&strongSelf->_stateLock);
                    NX10_GUARD_END
                }];
            }
        } else {
            if (_motionManager.isAccelerometerActive) {
                [_motionManager stopAccelerometerUpdates];
            }
            os_unfair_lock_lock(&_stateLock);
            _hasAccel = NO;
            os_unfair_lock_unlock(&_stateLock);
        }
    }
    NX10_GUARD_END
}

- (void)setGyroConsumerHz:(int)hz {
    NX10_GUARD_BEGIN
    if (_motionManager == nil || _sensorQueue == nil) return;

    @synchronized (self) {
        _gyroHz = hz > 0 ? (hz > 1000 ? 1000 : hz) : 0;
        if (_gyroHz == 0) {
            os_unfair_lock_lock(&_stateLock);
            _hasGyro = NO;
            os_unfair_lock_unlock(&_stateLock);
        }
        [self reconfigureDeviceMotionAndMagnetometerLocked];
    }
    NX10_GUARD_END
}

- (void)setMagnetometerConsumerHz:(int)hz {
    NX10_GUARD_BEGIN
    if (_motionManager == nil || _sensorQueue == nil) return;

    @synchronized (self) {
        _magHz = hz > 0 ? (hz > 1000 ? 1000 : hz) : 0;
        if (_magHz == 0) {
            os_unfair_lock_lock(&_stateLock);
            _hasMag = NO;
            os_unfair_lock_unlock(&_stateLock);
        }
        [self reconfigureDeviceMotionAndMagnetometerLocked];
    }
    NX10_GUARD_END
}

- (void)reconfigureDeviceMotionAndMagnetometerLocked {
    int maxHz = _gyroHz > _magHz ? _gyroHz : _magHz;
    if (maxHz <= 0) {
        if (_motionManager.isDeviceMotionActive) {
            [_motionManager stopDeviceMotionUpdates];
        }
        if (_motionManager.isGyroActive) {
            [_motionManager stopGyroUpdates];
        }
        if (_motionManager.isMagnetometerActive) {
            [_motionManager stopMagnetometerUpdates];
        }
        _deviceMotionRunning = NO;
        return;
    }

    if (_motionManager.isDeviceMotionAvailable) {
        BOOL wantMagInDeviceMotion = (_magHz > 0 && _motionManager.isMagnetometerAvailable);
        CMAttitudeReferenceFrame desiredFrame = wantMagInDeviceMotion
            ? CMAttitudeReferenceFrameXMagneticNorthZVertical
            : CMAttitudeReferenceFrameXArbitraryZVertical;

        NSUInteger availableFrames = [CMMotionManager availableAttitudeReferenceFrames];
        if ((availableFrames & desiredFrame) == 0) {
            desiredFrame = CMAttitudeReferenceFrameXArbitraryZVertical;
        }

        _motionManager.deviceMotionUpdateInterval = NX10HardwareIntervalForHz(maxHz);

        if (!_deviceMotionRunning || _activeRefFrame != desiredFrame || !_motionManager.isDeviceMotionActive) {
            if (_motionManager.isDeviceMotionActive) {
                [_motionManager stopDeviceMotionUpdates];
            }
            _activeRefFrame = desiredFrame;
            _deviceMotionRunning = YES;

            __weak __typeof__(self) weakSelf = self;
            [_motionManager startDeviceMotionUpdatesUsingReferenceFrame:desiredFrame
                                                                toQueue:_sensorQueue
                                                            withHandler:^(CMDeviceMotion *motion, NSError *error) {
                NX10_GUARD_BEGIN
                __strong __typeof__(weakSelf) strongSelf = weakSelf;
                if (!strongSelf || !motion || error) return;

                CMRotationRate rot = motion.rotationRate;
                CMCalibratedMagneticField calMag = motion.magneticField;

                float gx = (float)rot.x;
                float gy = (float)rot.y;
                float gz = (float)rot.z;
                float mx = (float)calMag.field.x;
                float my = (float)calMag.field.y;
                float mz = (float)calMag.field.z;

                os_unfair_lock_lock(&strongSelf->_stateLock);
                if (NX10IsFiniteVec3(gx, gy, gz)) {
                    strongSelf->_gyroX = gx;
                    strongSelf->_gyroY = gy;
                    strongSelf->_gyroZ = gz;
                    strongSelf->_hasGyro = YES;
                }

                if (strongSelf->_magHz > 0 && NX10IsFiniteVec3(mx, my, mz)) {
                    strongSelf->_magX = mx;
                    strongSelf->_magY = my;
                    strongSelf->_magZ = mz;
                    strongSelf->_hasMag = YES;
                }
                os_unfair_lock_unlock(&strongSelf->_stateLock);
                NX10_GUARD_END
            }];
        }
    } else {
        if (_gyroHz > 0 && _motionManager.isGyroAvailable) {
            _motionManager.gyroUpdateInterval = NX10HardwareIntervalForHz(_gyroHz);
            if (!_motionManager.isGyroActive) {
                __weak __typeof__(self) weakSelf = self;
                [_motionManager startGyroUpdatesToQueue:_sensorQueue
                                            withHandler:^(CMGyroData *data, NSError *error) {
                    NX10_GUARD_BEGIN
                    __strong __typeof__(weakSelf) strongSelf = weakSelf;
                    if (!strongSelf || !data || error) return;
                    CMRotationRate rot = data.rotationRate;
                    float gx = (float)rot.x, gy = (float)rot.y, gz = (float)rot.z;
                    if (!NX10IsFiniteVec3(gx, gy, gz)) return;

                    os_unfair_lock_lock(&strongSelf->_stateLock);
                    strongSelf->_gyroX = gx;
                    strongSelf->_gyroY = gy;
                    strongSelf->_gyroZ = gz;
                    strongSelf->_hasGyro = YES;
                    os_unfair_lock_unlock(&strongSelf->_stateLock);
                    NX10_GUARD_END
                }];
            }
        } else if (_motionManager.isGyroActive) {
            [_motionManager stopGyroUpdates];
        }

        if (_magHz > 0 && _motionManager.isMagnetometerAvailable) {
            _motionManager.magnetometerUpdateInterval = NX10HardwareIntervalForHz(_magHz);
            if (!_motionManager.isMagnetometerActive) {
                __weak __typeof__(self) weakSelf = self;
                [_motionManager startMagnetometerUpdatesToQueue:_sensorQueue
                                                    withHandler:^(CMMagnetometerData *data, NSError *error) {
                    NX10_GUARD_BEGIN
                    __strong __typeof__(weakSelf) strongSelf = weakSelf;
                    if (!strongSelf || !data || error) return;
                    CMMagneticField field = data.magneticField;
                    float mx = (float)field.x, my = (float)field.y, mz = (float)field.z;
                    if (!NX10IsFiniteVec3(mx, my, mz)) return;

                    os_unfair_lock_lock(&strongSelf->_stateLock);
                    strongSelf->_magX = mx;
                    strongSelf->_magY = my;
                    strongSelf->_magZ = mz;
                    strongSelf->_hasMag = YES;
                    os_unfair_lock_unlock(&strongSelf->_stateLock);
                    NX10_GUARD_END
                }];
            }
        } else if (_motionManager.isMagnetometerActive) {
            [_motionManager stopMagnetometerUpdates];
        }
    }
}

- (BOOL)getLatestGyroX:(float *)x y:(float *)y z:(float *)z {
    if (x == nullptr || y == nullptr || z == nullptr) return NO;
    os_unfair_lock_lock(&_stateLock);
    BOOL valid = _hasGyro;
    if (valid) {
        *x = _gyroX; *y = _gyroY; *z = _gyroZ;
    } else {
        *x = 0.0f; *y = 0.0f; *z = 0.0f;
    }
    os_unfair_lock_unlock(&_stateLock);
    return valid;
}

- (BOOL)getLatestAccelX:(float *)x y:(float *)y z:(float *)z {
    if (x == nullptr || y == nullptr || z == nullptr) return NO;
    os_unfair_lock_lock(&_stateLock);
    BOOL valid = _hasAccel;
    if (valid) {
        *x = _accelX; *y = _accelY; *z = _accelZ;
    } else {
        *x = 0.0f; *y = 0.0f; *z = 0.0f;
    }
    os_unfair_lock_unlock(&_stateLock);
    return valid;
}

- (BOOL)getLatestMagX:(float *)x y:(float *)y z:(float *)z {
    if (x == nullptr || y == nullptr || z == nullptr) return NO;
    os_unfair_lock_lock(&_stateLock);
    BOOL valid = _hasMag;
    if (valid) {
        *x = _magX; *y = _magY; *z = _magZ;
    } else {
        *x = 0.0f; *y = 0.0f; *z = 0.0f;
    }
    os_unfair_lock_unlock(&_stateLock);
    return valid;
}

@end
