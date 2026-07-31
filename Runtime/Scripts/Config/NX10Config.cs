using TMPro;
using UnityEngine;

namespace NX10
{
    public enum KeyType { Staging, Production }
    public class NX10Config : ScriptableObject
    {
        public string stagingApiKey;
        public string productionApiKey;

        public KeyType editorTarget = KeyType.Staging;
        public KeyType devBuildTarget = KeyType.Staging;
        public KeyType releaseBuildTarget = KeyType.Production;

        private const string stagingEndpoint = "https://control-plane.affectstack-stage.com/routes/sessions/start";
        private const string productionEndpoint = "https://control-plane.affectstack.com/routes/sessions/start";

        private const string stagingIngestion = "https://stage-ingestion-telemetry-354585352596.europe-west1.run.app/";
        private const string productionIngestion = "https://prod-ingestion-telemetry-1056827188100.europe-west1.run.app/";

        public string GetIngestionPrefix()
        {
            KeyType target;
#if UNITY_EDITOR
            target = editorTarget;
#elif DEVELOPMENT_BUILD
            target = devBuildTarget;
#else
            target = releaseBuildTarget;
#endif

            return target == KeyType.Staging ? stagingIngestion : productionIngestion;
        }

        public string GetSessionStartEndPoint()
        {
            KeyType target;
#if UNITY_EDITOR
            target = editorTarget;
#elif DEVELOPMENT_BUILD
            target = devBuildTarget;
#else
            target = releaseBuildTarget;
#endif

            return target == KeyType.Staging ? stagingEndpoint : productionEndpoint;
        }

        public string GetActiveKey()
        {
            KeyType target;
#if UNITY_EDITOR
            target = editorTarget;
#elif DEVELOPMENT_BUILD
            target = devBuildTarget;
#else
            target = releaseBuildTarget;
#endif

            return target == KeyType.Staging ? stagingApiKey : productionApiKey;
        }
    }
}


