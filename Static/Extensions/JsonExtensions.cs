using System.Globalization;
using System.Text.Json;

namespace ProSystem;

internal static class JsonExtensions
{
    private static readonly CultureInfo IC = CultureInfo.InvariantCulture;

    public static string GetString(this JsonElement root, string property) =>
        root.GetProperty(property).GetString() ?? throw new Exception("Parsed property " + property + " is null");

    public static long GetLong(this JsonElement root, string property) => root.GetProperty(property).GetInt64();

    public static int GetInt(this JsonElement root, string property) => root.GetProperty(property).GetInt32();

    public static bool GetBool(this JsonElement root, string property) => root.GetProperty(property).GetBoolean();

    public static DateTime GetDateTime(this JsonElement root, string property) =>
        DateTimeOffset.FromUnixTimeMilliseconds(root.GetProperty(property).GetInt64()).DateTime;

    public static DateTime GetDateTimeFromLong(this JsonElement root) =>
        DateTimeOffset.FromUnixTimeMilliseconds(root.GetInt64()).DateTime;

    public static double GetDouble(this JsonElement root, string property)
    {
        return double.Parse(root.GetProperty(property).GetString() ??
            throw new Exception("Property " + property + " is null"), IC);
    }

    public static double GetDoubleFromString(this JsonElement root) =>
        double.Parse(root.GetString() ?? throw new Exception("Parsed value is null"), IC);
}
