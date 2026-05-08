// iOSScaleBridge.mm
#import <UIKit/UIKit.h>

extern "C" {
    float _getNativeScaleFactor() {
        // This returns 2.0 for standard Retina and 3.0 for Plus/Max/Pro models
        // It is the most accurate way to get the Pixel-to-Point ratio
        return (float)[UIScreen mainScreen].scale;
    }
}