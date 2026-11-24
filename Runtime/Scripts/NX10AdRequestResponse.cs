using UnityEngine;

namespace NX10
{
    public class NX10AdRequestResponse
    {
        [System.Serializable]
        public class AdRequestData
        {
            public bool shouldShowAd;
            public float confidence;
        }

        [System.Serializable]
        public class RootAdRequestResponse
        {
            public string status;
            public AdRequestData data;
        }
    }
}
