#import <Foundation/Foundation.h>
#import <CoreMotion/CoreMotion.h>

// A simple structure to pass the 3D vector back to C#
struct NativeVector3 {
    float x;
    float y;
    float z;
};

@interface NativeGyro : NSObject
@property (nonatomic, strong) CMMotionManager *motionManager;
+ (instancetype)sharedInstance;
- (void)startGyro;
- (void)stopGyro;
- (struct NativeVector3)getUnbiasedRotationRate;
@end

@implementation NativeGyro

+ (instancetype)sharedInstance {
    static NativeGyro *sharedInstance = nil;
    static dispatch_once_onceToken;
    dispatch_once(&onceToken, ^{
        sharedInstance = [[self alloc] init];
    });
    return sharedInstance;
}

- (instancetype)init {
    self = [super init];
    if (self) {
        self.motionManager = [[CMMotionManager alloc] init];
        // 60 Hz update rate matching standard target frame rates
        self.motionManager.deviceMotionUpdateInterval = 1.0 / 60.0; 
    }
    return self;
}

- (void)startGyro {
    if (self.motionManager.isDeviceMotionAvailable) {
        [self.motionManager startDeviceMotionUpdates];
    }
}

- (void)stopGyro {
    if (self.motionManager.isDeviceMotionActive) {
        [self.motionManager stopDeviceMotionUpdates];
    }
}

- (struct NativeVector3)getUnbiasedRotationRate {
    struct NativeVector3 result = {0.0f, 0.0f, 0.0f};
    
    if (self.motionManager.isDeviceMotionActive) {
        CMDeviceMotion *deviceMotion = self.motionManager.deviceMotion;
        if (deviceMotion != nil) {
            CMRotationRate rotationRate = deviceMotion.rotationRate;
            
            // Apple's CoreMotion uses double precision; cast to float for Unity Vector3
            result.x = (float)rotationRate.x;
            result.y = (float)rotationRate.y;
            result.z = (float)rotationRate.z;
        }
    }
    return result;
}
@end

// --- C Linkage (The Bridge Unity Talks To) ---
extern "C" {
    void _StartNativeGyro() {
        [[NativeGyro sharedInstance] startGyro];
    }

    void _StopNativeGyro() {
        [[NativeGyro sharedInstance] stopGyro];
    }

    struct NativeVector3 _GetNativeRotationRateUnbiased() {
        return [[NativeGyro sharedInstance] getUnbiasedRotationRate];
    }
}