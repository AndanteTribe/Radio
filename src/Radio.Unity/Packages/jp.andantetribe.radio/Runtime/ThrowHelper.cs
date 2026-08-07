#nullable enable

using System;
using System.Runtime.CompilerServices;

namespace Radio
{
    public static class ThrowHelper
    {
        public static void ThrowIfVolumeOutOfRange(float volume, [CallerArgumentExpression("volume")] string paramName = "")
        {
            if (0 < volume && volume <= 1.0f)
            {
                return;
            }
            throw new ArgumentOutOfRangeException(paramName, "Volume must be greater than 0 and less than or equal to 1.");
        }
    }
}