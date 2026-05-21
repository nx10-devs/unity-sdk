using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;

namespace NX10
{
    public enum KineticState
    {
        [System.Runtime.Serialization.EnumMember(Value = "unknown")]
        Unknown,
        [System.Runtime.Serialization.EnumMember(Value = "stationary")]
        Stationary,
        [System.Runtime.Serialization.EnumMember(Value = "in hand")]
        InHand,
        [System.Runtime.Serialization.EnumMember(Value = "moving")]
        Moving
    }

    [System.Serializable]
    public class DeviceState
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public KineticState kineticState;
    }

    [System.Serializable]
    public class ActivityData
    {
        public string timestamp;
        public DeviceState device;
    }

    [System.Serializable]
    public class RootActivityResponse
    {
        public string status;
        public ActivityData data;
    }
}
