using NUnit.Framework;

namespace NUnitTest
{
    public class Tests
    {
        [SetUp]
        public void Setup()
        {
            // Preparation for tests.
        }

        [TestCase(new double[] { 24, 25, 26, 27 }, 2, ExpectedResult = new double[] { 0, 24.5, 25.5, 26.5 })]
        [TestCase(new double[] { 13546, 13909, 14455, 14077, 13765, 13728, 13483 }, 4,
            ExpectedResult = new double[] { 0, 0, 0, 13996.75, 14051.5, 14006.25, 13763.25 })]
        public double[] TestSMA(double[] inputs, int period)
        {
            return ProSystem.Indicators.SMA(inputs, period, 2);
        }
    }
}