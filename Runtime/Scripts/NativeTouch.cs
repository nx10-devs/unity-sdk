using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;


#if ENABLE_INPUT_SYSTEM
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
#endif


namespace NX10
{
    public class NativeTouch : MonoBehaviour
    {
        public struct TouchObject
        {
            public double x;
            public double y;
            public string touchId;
            public string touchType;
            public double touchRadius;
        }

        private NativeScale nativeScale;
        private float dpi;

        public void Initialise(NativeScale nativeScale, float dpi)
        {
            this.nativeScale = nativeScale;
            this.dpi = dpi;
        }

        public List<TouchObject> Touches
        {
            get
            {
                List<TouchObject> touches = new List<TouchObject>();

#if ENABLE_INPUT_SYSTEM
                foreach (var touch in Touch.activeTouches)
                {

                    TouchObject obj = new TouchObject()
                    {
                        x = PixelsToMillimeters(touch.screenPosition.x),
                        y = PixelsToMillimeters(touch.screenPosition.y),
                        touchId = touch.touchId.ToString(),
                        touchType = ConvertTouchPhaseToTouchType(touch.phase),
#if UNITY_IOS
                        touchRadius = ConvertPixelRadToMM(touch.radius)
#elif UNITY_ANDROID
                        touchRadius = Math.Round(Mathf.Max(touch.radius.x, touch.radius.y), 3, MidpointRounding.AwayFromZero)
                    };
                    
                    touches.Add(obj);
                }
#else
                foreach (var touch in Input.touches)
                {
                    TouchObject obj = new TouchObject()
                    {
                        x = PixelsToMillimeters(touch.position.x),
                        y = PixelsToMillimeters(touch.position.y),
                        touchId = touch.fingerId.ToString(),
                        touchType = ConvertTouchPhaseToTouchType(touch.phase),
#if UNITY_IOS
                        touchRadius = ConvertPixelRadToMM(touch.radius)
#elif UNITY_ANDROID
                        touchRadius = Math.Round(touch.radius, 3, MidpointRounding.AwayFromZero)
#endif

                    };

                    touches.Add(obj);
                }
#endif
                return touches;
            }
        }

        public double ConvertPixelRadToMM(Vector2 radius)
        {
            float majorRadius = Mathf.Max(radius.x, radius.y);
            return ConvertPixelRadToMM(majorRadius);
        }

        public double ConvertPixelRadToMM(float radius)
        {
            double radiusMm = MmPerPoint() * radius;
            radiusMm = Math.Round(radiusMm, 3, MidpointRounding.AwayFromZero);
            return radiusMm;
        }

        public double MmPerPoint()
        {
            double nativeIOSScale = nativeScale.GetNativeScale();
            return (nativeIOSScale / dpi) * 25.4;
        }

        public double PixelsToMillimeters(double pixels)
        {
            double inches = pixels / dpi;
            double millimeters = inches * 25.4f;
            return Math.Round(millimeters, 3, MidpointRounding.AwayFromZero);
        }

#if ENABLE_INPUT_SYSTEM
        private string ConvertTouchPhaseToTouchType(UnityEngine.InputSystem.TouchPhase touchPhase)
        {
            switch (touchPhase)
            {
                case UnityEngine.InputSystem.TouchPhase.Began:
                    return "down";
                case UnityEngine.InputSystem.TouchPhase.Ended:
                    return "up";
                case UnityEngine.InputSystem.TouchPhase.Moved:
                    return "move";
                case UnityEngine.InputSystem.TouchPhase.Stationary:
                    return "stationary";
                case UnityEngine.InputSystem.TouchPhase.Canceled:
                    return "cancelled";
            }

            throw new NotImplementedException();
        }
#endif

        private string ConvertTouchPhaseToTouchType(UnityEngine.TouchPhase touchPhase)
        {
            switch (touchPhase)
            {
                case UnityEngine.TouchPhase.Began:
                    return "down";
                case UnityEngine.TouchPhase.Ended:
                    return "up";
                case UnityEngine.TouchPhase.Moved:
                    return "move";
                case UnityEngine.TouchPhase.Stationary:
                    return "stationary";
                case UnityEngine.TouchPhase.Canceled:
                    return "cancelled";
            }

            throw new NotImplementedException();
        }

    }
}
