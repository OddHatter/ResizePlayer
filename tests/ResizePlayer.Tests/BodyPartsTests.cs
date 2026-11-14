using System;
using System.Linq;
using Oddhatter.ResizePlayer;
using Xunit;

namespace ResizePlayer.Tests
{
    public class BodyPartsTests
    {
        [Fact] public void Clamp_inRange_unchanged() => Assert.Equal(2.5f, BodyParts.Clamp(2.5f));

        [Fact] public void Clamp_low() { Assert.Equal(0.25f, BodyParts.Clamp(0f)); Assert.Equal(0.25f, BodyParts.Clamp(-1f)); }

        [Fact] public void Clamp_max_is_10() => Assert.Equal(10f, BodyParts.Max);
        [Fact] public void Clamp_keeps_values_up_to_10() => Assert.Equal(7.5f, BodyParts.Clamp(7.5f));
        [Fact] public void Clamp_high() => Assert.Equal(10f, BodyParts.Clamp(100f));

        [Fact] public void Clamp_nonFinite_isOne() { Assert.Equal(1f, BodyParts.Clamp(float.NaN)); Assert.Equal(1f, BodyParts.Clamp(float.PositiveInfinity)); }

        [Fact] public void ZdoKey_format() { Assert.Equal("oddhatter.resizeplayer.head", BodyParts.ZdoKey(Part.Head)); Assert.Equal("oddhatter.resizeplayer.overall", BodyParts.ZdoKey(Part.Overall)); }

        [Fact] public void LocalFactor_ratio() { Assert.Equal(2f, BodyParts.LocalFactor(2f, 1f)); Assert.Equal(0.5f, BodyParts.LocalFactor(1f, 2f)); }

        [Fact] public void LocalFactor_extremes() { Assert.Equal(40f, BodyParts.LocalFactor(10f, 0.25f)); Assert.Equal(1f / 40f, BodyParts.LocalFactor(0.25f, 10f), 5); }

        [Fact] public void ReachFactor_neverBelowOne() { Assert.Equal(1f, BodyParts.ReachFactor(0.25f)); Assert.Equal(3f, BodyParts.ReachFactor(3f)); }

        [Fact] public void All_hasEveryPartOnce() => Assert.Equal(Enum.GetValues(typeof(Part)).Length, BodyParts.All.Distinct().Count());

        [Fact] public void PartValues_identity_isAllOne() { var v = PartValues.Identity(); Assert.All(BodyParts.All, p => Assert.Equal(1f, v[p])); }

        [Fact]
        public void PartValues_approxEquals()
        {
            var a = PartValues.Identity();
            var b = PartValues.Identity();
            b[Part.Hips] = 1.00005f;
            Assert.True(a.ApproximatelyEquals(b));
            b[Part.Hips] = 1.1f;
            Assert.False(a.ApproximatelyEquals(b));
            Assert.False(a.ApproximatelyEquals(null));
        }

        [Fact]
        public void PartValues_copyFrom_isIndependent()
        {
            var a = PartValues.Identity();
            var b = PartValues.Identity();
            b[Part.Head] = 3f;
            a.CopyFrom(b);
            b[Part.Head] = 1f;
            Assert.Equal(3f, a[Part.Head]);
        }
    }
}
