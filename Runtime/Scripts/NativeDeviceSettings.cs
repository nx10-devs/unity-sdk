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

    private GUIStyle labelStyle;
    private GUIStyle boxStyle;

    void Awake()
    {
        Debug.Log($"[DeviceSettings] BCP-47 Locale: {Locale}");
        Debug.Log($"[DeviceSettings] IANA TimeZone: {TimeZone}");
    }

    void OnGUI()
    {
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            boxStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(12, 12, 12, 12)
            };
        }

        float targetWidth = 1080f;
        float scale = Screen.width / targetWidth;
        scale = Mathf.Clamp(scale, 1f, 3f);

        Matrix4x4 savedMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        // Draw overlay box in top-left corner
        GUILayout.BeginArea(new Rect(10, 10, 320, 90), boxStyle);
        GUILayout.Label($"Locale (BCP-47): {Locale}", labelStyle);
        GUILayout.Space(4);
        GUILayout.Label($"TimeZone (IANA): {TimeZone}", labelStyle);
        GUILayout.EndArea();

        GUI.matrix = savedMatrix;
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