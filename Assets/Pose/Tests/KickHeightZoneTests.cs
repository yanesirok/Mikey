using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    public class KickHeightZoneTests
    {
        // Y растёт вниз: hip 0.5, shoulder 0.2.
        [TestCase(0.65f, KickZone.Gedan)]   // ниже бедра
        [TestCase(0.35f, KickZone.Chudan)]  // между бедром и плечом
        [TestCase(0.50f, KickZone.Chudan)]  // ровно на бедре — уже chudan
        [TestCase(0.20f, KickZone.Jodan)]   // ровно на плече — jodan
        [TestCase(0.10f, KickZone.Jodan)]   // выше плеча
        public void ClassifiesByHeight(float ankleY, KickZone expected)
        {
            Assert.AreEqual(expected, KickHeightZone.Classify(ankleY, hipY: 0.5f, shoulderY: 0.2f));
        }
    }
}
