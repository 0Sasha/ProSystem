using Newtonsoft.Json;

namespace ProSystem;

[Serializable]
public class ScriptResult
{
    public ScriptType Type { get; set; }
    public bool[] IsGrow { get; set; }
    public double[][] Indicators { get; set; }
    public DateTime IndLastDT { get; set; }

    public int Centre { get; set; } = -1;
    public int Level { get; set; } = -1;
    public bool OnlyLimit { get; set; }

    [JsonConstructor]
    public ScriptResult(ScriptType type, bool[] isGrow, double[][] indicators, DateTime indLastDT)
    {
        Type = type;
        IsGrow = isGrow;
        Indicators = indicators;
        IndLastDT = indLastDT;
    }

    public ScriptResult(ScriptType type, bool[] isGrow, double[][] indicators, DateTime inLastDT,
        bool onlyLimit) : this(type, isGrow, indicators, inLastDT) => OnlyLimit = onlyLimit;

    public ScriptResult(ScriptType type, bool[] isGrow, double[][] indicators, DateTime inLastDT,
        int centre, int level, bool onlyLimit) : this(type, isGrow, indicators, inLastDT, onlyLimit)
    {
        Centre = centre;
        Level = level;
    }
}
