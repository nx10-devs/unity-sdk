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


