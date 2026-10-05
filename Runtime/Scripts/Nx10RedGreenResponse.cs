using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace NX10
{
    public enum Affect
    {
        [System.Runtime.Serialization.EnumMember(Value = "frustrated")]
        Frustrated,
        [System.Runtime.Serialization.EnumMember(Value = "not-frustrated")]
        NotFrustrated,
        Unknown,
    }

    [System.Serializable]
    public class AffectData
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public Affect currentAffect;
        public float confidence;
    }

    [System.Serializable]
    public class RootRedGreenResponse
    {
        public string status;
        public AffectData data;
    }
}
