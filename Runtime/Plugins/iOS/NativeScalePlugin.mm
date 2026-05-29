#import <UIKit/UIKit.h>

extern "C" {
    // Returns the logical layout scale factor (e.g., 2.0 for Retina, 3.0 for Super Retina)
    float _GetiOSScreenScale() {
        if ([UIScreen instancesRespondToSelector:@selector(scale)]) {
            return (float)[UIScreen mainScreen].scale;
        }
        return 1.0f;
    }

    // Returns the physical hardware scale factor (accounts for display zoom adjustments)
    float _GetiOSNativeScale() {
        if ([UIScreen instancesRespondToSelector:@selector(nativeScale)]) {
            return (float)[UIScreen mainScreen].nativeScale;
        }
        return 1.0f;
    }
}