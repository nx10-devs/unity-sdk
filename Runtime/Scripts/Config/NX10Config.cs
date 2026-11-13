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

        [Header("Consent & Privacy")]
        [Tooltip("Persist user consent decision in PlayerPrefs across launches. Disable if an external CMP is your sole source of truth.")]
        public bool persistConsent = true;

        [Tooltip("Block StartSession() until SetConsent(true) has been called, deferring session creation safely.")]
        public bool requireExplicitConsent = true;

        [Tooltip("Automatically begin telemetry collection as soon as a consented session starts.")]
        public bool autoStartTelemetry = true;

        private const string stagingEndpoint = "https://control-plane.affectstack-stage.com/routes/sessions/start";
        private const string productionEndpoint = "https://control-plane.affectstack.com/routes/sessions/start";

        private const string stagingControlPlanePrefix = "https://control-plane.affectstack-stage.com/routes/";
        private const string productionControlPlancePrefix = "https://control-plane.affectstack.com/routes/";

        public string GetControlPlanePrefix()
        {
            KeyType target;
#if UNITY_EDITOR
            target = editorTarget;
#elif DEVELOPMENT_BUILD
            target = devBuildTarget;
#else
            target = releaseBuildTarget;
#endif

            return target == KeyType.Staging ? stagingControlPlanePrefix : productionControlPlancePrefix;
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


