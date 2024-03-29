using NUnit.Framework;
using ProSystem;

namespace NUnitTest;

public class IndicatorsTests
{
    [TestCase(new double[] { 24, 25, 26, 27 }, 2, ExpectedResult = new double[] { 0, 24.5, 25.5, 26.5 })]
    [TestCase(new double[] { 13546, 13909, 14455, 14077, 13765, 13728, 13483 }, 4,
        ExpectedResult = new double[] { 0, 0, 0, 13996.75, 14051.5, 14006.25, 13763.25 })]
    public double[] TestSMA(double[] inputs, int period)
    {
        return Indicators.SMA(inputs, period, 2);
    }

    /*[Test]
    [Timeout(1000)]
    public void TestProcessTrades()
    {
        var dataProcessor = new TXmlDataProcessor();
        var trades = System.IO.File.ReadAllText("trades.txt");
        for (int i = 0; i < 100000; i++) dataProcessor.ProcessData(trades);
    }*/
}