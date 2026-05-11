using UnityEngine;
using System.Runtime.InteropServices;

namespace NX10
{
    public static class NX10ScaleFactor 
    {
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern float _getNativeScaleFactor();
#endif

        public static float GetScaleFactor()
        {
#if UNITY_IOS && !UNITY_EDITOR
            return _getNativeScaleFactor();
#elif UNITY_ANDROID && !UNITY_EDITOR
            return GetAndroidScaleFactor();
#else
            return Screen.dpi / 160f > 0 ? Screen.dpi / 160f : 1.0f;
#endif
        }

        public static float GetAndroidScaleFactor()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        {
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                using (var resources = activity.Call<AndroidJavaObject>("getResources"))
                {
                    using (var metrics = resources.Call<AndroidJavaObject>("getDisplayMetrics"))
                    {
                        // This 'density' field is the scale factor (e.g., 2.0, 3.0)
                        return metrics.Get<float>("density");
                    }
                }
            }
        }
#else
            return 2.0f; 
#endif
        }

        public static float PixelsToPoints(float pixels)
        {
            float scale = GetScaleFactor();
            float points = pixels / scale;

            return points;
        }
    }
}
