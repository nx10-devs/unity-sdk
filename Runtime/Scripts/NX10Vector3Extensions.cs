using System;
using UnityEngine;

namespace NX10
{
    public static class NX10Vector3Extensions
    {
        public static Vector3 Round(this Vector3 vector, int decimals, MidpointRounding mode)
        {
            return new Vector3(
                (float)Math.Round(vector.x, decimals, mode),
                (float)Math.Round(vector.y, decimals, mode),
                (float)Math.Round(vector.z, decimals, mode)
            );
        }

        public static Vector3 RoundToFivePlaces(this Vector3 vector)
        {
            return vector.Round(5, MidpointRounding.AwayFromZero);
        }
    }
}
