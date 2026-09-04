#import <Foundation/Foundation.Foundation.h>

extern "C" {
    // Helper method to convert NSString to C string for C# interop
    char* MakeStringCopy(const char* string) {
        if (string == NULL) return NULL;
        char* res = (char*)malloc(strlen(string) + 1);
        strcpy(res, string);
        return res;
    }

    // Returns BCP-47 identifier (e.g. "en-US", "zh-Hans-CN")
    const char* _getIOSLocale() {
        NSString *localeIdentifier = [[NSLocale currentLocale] localeIdentifier];
        NSString *bcp47Identifier = [localeIdentifier stringByReplacingOccurrencesOfString:@"_" withString:@"-"];
        return MakeStringCopy([bcp47Identifier UTF8String]);
    }

    // Returns IANA TimeZone identifier (e.g. "America/Los_Angeles", "Europe/London")
    const char* _getIOSTimeZone() {
        NSString *timeZoneName = [[NSTimeZone localTimeZone] name];
        return MakeStringCopy([timeZoneName UTF8String]);
    }
}