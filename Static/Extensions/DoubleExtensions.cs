namespace ProSystem;

public static class DoubleExtensions
{
    const double MinDif = 0.00_00_00_00_00_1;

    public static bool More(this double value1, double value2) => value1 - value2 > MinDif;

    public static bool MoreEq(this double value1, double value2) => value1 - value2 > -MinDif;

    public static bool Less(this double value1, double value2) => value1 - value2 < -MinDif;

    public static bool LessEq(this double value1, double value2) => value1 - value2 < MinDif;

    public static bool Eq(this double value1, double value2) => Math.Abs(value1 - value2) < MinDif;

    public static bool NotEq(this double value1, double value2) => !value1.Eq(value2);
}
