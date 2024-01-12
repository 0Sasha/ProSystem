using System.Globalization;
using System.Xml;

namespace ProSystem;

internal static class XmlExtensions
{
    private static readonly CultureInfo IC = CultureInfo.InvariantCulture;
    private static readonly string DTForm = "dd.MM.yyyy HH:mm:ss";

    public static int GetIntAttribute(this XmlReader root, string attr) =>
        int.Parse(root.GetAttribute(attr) ?? throw new Exception("Attribute " + attr + " is null"), IC);

    public static string GetStringAttribute(this XmlReader root, string attr) =>
        root.GetAttribute(attr) ?? throw new Exception("Attribute " + attr + " is null");

    public static double GetDoubleAttribute(this XmlReader root, string attr) =>
        double.Parse(root.GetAttribute(attr) ?? throw new Exception("Attribute " + attr + " is null"), IC);

    public static DateTime GetDateTimeAttribute(this XmlReader root, string attr) =>
        DateTime.ParseExact(root.GetAttribute(attr) ??
            throw new Exception("Attribute " + attr + " is null"), DTForm, IC);

    public static string GetNextString(this XmlReader root, string element)
    {
        if ((root.Name == element || root.ReadToFollowing(element)) && root.Read()) return root.Value;

        throw new Exception("Element " + element + " is not found");
    }

    public static int GetNextInt(this XmlReader root, string element)
    {
        if ((root.Name == element || root.ReadToFollowing(element)) && root.Read())
            return int.Parse(root.Value, IC);
        throw new Exception("Element " + element + " is not found");
    }

    public static long GetNextLong(this XmlReader root, string element)
    {
        if ((root.Name == element || root.ReadToFollowing(element)) && root.Read())
            return long.Parse(root.Value, IC);
        throw new Exception("Element " + element + " is not found");
    }

    public static double GetNextDouble(this XmlReader root, string element)
    {
        if ((root.Name == element || root.ReadToFollowing(element)) && root.Read())
            return double.Parse(root.Value, IC);
        throw new Exception("Element " + element + " is not found");
    }

    public static DateTime GetNextDateTime(this XmlReader root, string element)
    {
        if ((root.Name == element || root.ReadToFollowing(element)) && root.Read())
            return DateTime.ParseExact(root.Value, DTForm, IC);
        throw new Exception("Element " + element + " is not found");
    }
}
