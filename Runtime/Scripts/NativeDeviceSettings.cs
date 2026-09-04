using System;
using System.Globalization;
using UnityEngine;

#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

public class NativeDeviceSettings : MonoBehaviour
{
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern string _getIOSLocale();

    [DllImport("__Internal")]
    private static extern string _getIOSTimeZone();
#endif

    public string Locale
    {
        get
        {
            return GetNativeLocale();
        }
    }
    public string TimeZone
    {
        get
        {
            return GetNativeTimeZone();
        }
    }

    void Awake()
    {
        Debug.Log($"[DeviceSettings] BCP-47 Locale: {Locale}");
        Debug.Log($"[DeviceSettings] IANA TimeZone: {TimeZone}");
    }

    private string GetNativeLocale()
    {
#if UNITY_EDITOR
        return CultureInfo.CurrentCulture.Name;
#elif UNITY_ANDROID
        try
        {
            using (AndroidJavaClass localeClass = new AndroidJavaClass("java.util.Locale"))
            using (AndroidJavaObject defaultLocale = localeClass.CallStatic<AndroidJavaObject>("getDefault"))
            {
                return defaultLocale.Call<string>("toLanguageTag"); // Standard BCP-47 identifier
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to fetch Android Locale: {e.Message}");
            return CultureInfo.CurrentCulture.Name;
        }
#elif UNITY_IOS
        try
        {
            return _getIOSLocale();
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to fetch iOS Locale: {e.Message}");
            return CultureInfo.CurrentCulture.Name;
        }
#else
        return CultureInfo.CurrentCulture.Name;
#endif
    }

    private string GetNativeTimeZone()
    {
#if UNITY_EDITOR
        return TimeZoneInfo.Local.Id;
#elif UNITY_ANDROID
        try
        {
            using (AndroidJavaClass tzClass = new AndroidJavaClass("java.util.TimeZone"))
            using (AndroidJavaObject defaultTZ = tzClass.CallStatic<AndroidJavaObject>("getDefault"))
            {
                return defaultTZ.Call<string>("getID"); // Standard IANA identifier
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to fetch Android TimeZone: {e.Message}");
            return TimeZoneInfo.Local.Id;
        }
#elif UNITY_IOS
        try
        {
            return _getIOSTimeZone();
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to fetch iOS TimeZone: {e.Message}");
            return TimeZoneInfo.Local.Id;
        }
#else
        return TimeZoneInfo.Local.Id;
#endif
    }
}