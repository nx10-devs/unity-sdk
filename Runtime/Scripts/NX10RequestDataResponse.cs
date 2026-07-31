using UnityEngine;

namespace NX10
{
    [System.Serializable]
    public class DataRequestData
    {
        public string requestUrl;
    }

    [System.Serializable]
    public class RootDataRequestResponse
    {
        public string status;
        public DataRequestData data;
    }
}
