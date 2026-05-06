
namespace NX10
{
    #region Input Events
    public interface IInputEvent
    {
        object[] ToArray();
    }

    [System.Serializable]
    public class GyroEvent : IInputEvent
    {
        public string type => "gyro";
        public double timestampOffsetMs { get; set; }
        public float x;
        public float y;
        public float z;

        public object[] ToArray()
        {
            return new object[]
            {
                    type,
                    timestampOffsetMs,
                    x,
                    y,
                    z,
            };
        }
    }

    [System.Serializable]
    public class AccelerometerEvent : IInputEvent
    {
        public string type => "acc";
        public double timestampOffsetMs { get; set; }
        public float x;
        public float y;
        public float z;

        public object[] ToArray()
        {
            return new object[]
            {
                    type,
                    timestampOffsetMs,
                    x,
                    y,
                    z,
            };
        }
    }

    [System.Serializable]
    public class TouchInputEvent : IInputEvent
    {
        public string type => "touch";
        public double timestampOffsetMs { get; set; }
        public float x;
        public float y;
        public float velocityX;
        public float velocityY;

        public object[] ToArray()
        {
            return new object[]
            {
                    type,
                    timestampOffsetMs,
                    x,
                    y,
                    velocityX,
                    velocityY
            };
        }
    }

    [System.Serializable]
    public class TouchInputEventV2 : IInputEvent
    {
        public string type => "touch";
        public string eventVersion => "2";
        public double timestampOffsetMs { get; set; }
        public string touchId;
        public string touchType;
        public string touchObject;
        public float xMm;
        public float yMm;
        public float touchRadiusMm;

        public object[] ToArray()
        {
            return new object[]
            {
                    type,
                    eventVersion,
                    timestampOffsetMs,
                    touchId,
                    touchType,
                    touchObject,
                    xMm,
                    yMm,
                    touchRadiusMm
            };
        }
    }
    #endregion
}

