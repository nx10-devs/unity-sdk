using UnityEngine;

namespace NX10
{
    public static class NX10RuntimeConfig
    {
        private static NX10PackageConfig _config;

        public static NX10PackageConfig Config
        {
            get
            {
                if (_config == null)
                {
                    _config = Resources.Load<NX10PackageConfig>("NX10Package_Config");
                }
                return _config;
            }
        }

        public static string ApiKey => Config != null ? Config.apiKey : string.Empty;
    }
}