using NUnit.Framework;
using ProSystem;
using System;

namespace NUnitTest;

public class ExtensionsTests
{
    readonly double[][] SameValues =
        [
            [0.00_00_00_00_00_00_01, 0.00_00_00_00_00_00_99],
            [9_999_999.7, 9_999_999.70_00_00_00_01]
        ];

    readonly double[][] DifValues =
        [
            [0.00_00_00_00_02_00, 0.00_00_00_00_01_90],
            [99_999_999_999.000_10, 99_999_999_999.000_09],
            [-0.00_00_00_00_00_01, -0.00_00_00_00_01_00],
            [-99_999_999_999.000_09, -99_999_999_999.000_10]
        ];

    [Test]
    public void More()
    {
        foreach (var pair in DifValues)
        {
            if (!pair[0].More(pair[1]))
                throw new Exception(pair[0] + "/" + pair[1]);
        }
        foreach (var pair in SameValues)
        {
            if (pair[0].More(pair[1]))
                throw new Exception(pair[0] + "/" + pair[1]);
        }
    }

    [Test]
    public void MoreEq()
    {
        foreach (var pair in DifValues)
        {
            if (!pair[0].MoreEq(pair[1]))
                throw new Exception(pair[0] + "/" + pair[1]);
        }
        foreach (var pair in SameValues)
        {
            if (!pair[0].MoreEq(pair[1]))
                throw new Exception(pair[0] + "/" + pair[1]);
        }
    }

    [Test]
    public void Less()
    {
        foreach (var pair in DifValues)
        {
            if (pair[0].Less(pair[1]))
                throw new Exception(pair[0] + "/" + pair[1]);
        }
        foreach (var pair in SameValues)
        {
            if (pair[0].Less(pair[1]))
                throw new Exception(pair[0] + "/" + pair[1]);
        }
    }

    [Test]
    public void LessEq()
    {
        foreach (var pair in DifValues)
        {
            if (pair[0].LessEq(pair[1]))
                throw new Exception(pair[0] + "/" + pair[1]);
        }
        foreach (var pair in SameValues)
        {
            if (!pair[0].LessEq(pair[1]))
                throw new Exception(pair[0] + "/" + pair[1]);
        }
    }

    const double SmallPos = 0.00_00_00_01;
    const double SmallNeg = -0.00_00_00_01;
    const double BigPos = 9999.000001;
    const double BigNeg = -9999.000001;

    [Test]
    [TestCase(SmallPos, SmallPos + 1 - 1)]
    [TestCase(SmallNeg, SmallNeg - 1 + 1)]
    [TestCase(BigPos, BigPos + 0.00_00_00_00_00_1)]
    [TestCase(BigNeg, BigNeg - 0.00_00_00_00_00_1)]
    public void Equal(double origin, double changed)
    {
        if (!origin.Eq(changed)) throw new Exception(origin + "/" + changed);
    }
}
