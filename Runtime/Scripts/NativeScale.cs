using System.Runtime.InteropServices;
using UnityEngine;

namespace NX10
{
    public class NativeScale : MonoBehaviour
    {
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern float _GetiOSNativeScale();
#endif 
        public float GetNativeScale()
        {
#if UNITY_IOS && !UNITY_EDITOR
            try
            {
                float s = _GetiOSNativeScale();
                return NativeSensorSampleValidator.IsFinite(s) && s > 0f ? s : 1.0f;
            }
            catch
            {
                return 1.0f;
            }
#else
            return 1.0f;
#endif
        }
    }
}
