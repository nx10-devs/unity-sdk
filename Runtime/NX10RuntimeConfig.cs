using UnityEngine;

namespace NX10
{
    public static class NX10RuntimeConfig
    {
        private static NX10Config _config;

        public static NX10Config Config
        {
            get
            {
                if (_config == null)
                {
                    _config = Resources.Load<NX10Config>("NX10Package_Config");
                }
                return _config;
            }
        }

        public static string ApiKey => Config != null ? Config.GetActiveKey() : string.Empty;
        public static string SessionStartEndpoint => Config != null ? Config.GetSessionStartEndPoint() : string.Empty;
    }
}