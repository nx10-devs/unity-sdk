using UnityEngine;

namespace NX10
{
    public enum KineticState
    {
        unknown,
        stationary,
        [InspectorName("in hand")] inHand, 
        moving
    }

    [System.Serializable]
    public class DeviceState
    {
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
