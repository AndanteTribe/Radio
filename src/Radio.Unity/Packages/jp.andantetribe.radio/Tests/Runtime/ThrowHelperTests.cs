#nullable enable

using System;
using NUnit.Framework;

namespace Radio.Tests
{
    public class ThrowHelperTests
    {
        [TestCase(float.Epsilon)]
        [TestCase(0.5f)]
        [TestCase(1.0f)]
        public void ThrowIfVolumeOutOfRangeAcceptsValuesInRange(float volume)
        {
            Assert.DoesNotThrow(() => ThrowHelper.ThrowIfVolumeOutOfRange(volume));
        }

        [TestCase(0.0f)]
        [TestCase(-0.1f)]
        [TestCase(1.0001f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void ThrowIfVolumeOutOfRangeRejectsValuesOutsideRange(float volume)
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => ThrowHelper.ThrowIfVolumeOutOfRange(volume));

            Assert.That(exception!.ParamName, Is.EqualTo(nameof(volume)));
        }
    }
}
