
namespace NX10
{
    #region Input Events
    public interface IInputEvent
    {
        object[] ToArray();
    }

    [System.Serializable]
    public struct GyroEvent : IInputEvent
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
    public struct AccelerometerEvent : IInputEvent
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
    public struct TouchInputEvent : IInputEvent
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
    public struct TouchInputEventV2 : IInputEvent
    {
        public string type => "touch";
        public string eventVersion => "2";
        public double timestampOffsetMs { get; set; }
        public string touchId;
        public string touchType;
        public string touchObject;
        public double xMm;
        public double yMm;
        public double touchRadiusMm;

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

