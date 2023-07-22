using System;

namespace ProSystem;

[Serializable]
public class ScriptResult
{
    public ScriptType Type { get; set; }
    public bool[] IsGrow { get; set; }
    public double[][] Indicators { get; set; }
    public DateTime iLastDT { get; set; }

    public int Centre { get; set; } = -1;
    public int Level { get; set; } = -1;
    public bool OnlyLimit { get; set; }

    public ScriptResult() { }
    public ScriptResult(ScriptType Type, bool[] IsGrow, double[][] Indicators, DateTime iLastDT)
    {
        this.Type = Type;
        this.IsGrow = IsGrow;
        this.Indicators = Indicators;
        this.iLastDT = iLastDT;
    }
    public ScriptResult(ScriptType Type, bool[] IsGrow, double[][] Indicators, DateTime iLastDT, bool OnlyLimit)
    {
        this.Type = Type;
        this.IsGrow = IsGrow;
        this.Indicators = Indicators;
        this.iLastDT = iLastDT;
        this.OnlyLimit = OnlyLimit;
    }
    public ScriptResult(ScriptType Type, bool[] IsGrow,
        double[][] Indicators, DateTime iLastDT, int Centre, int Level, bool OnlyLimit)
    {
        this.Type = Type;
        this.IsGrow = IsGrow;
        this.Indicators = Indicators;
        this.iLastDT = iLastDT;
        this.Centre = Centre;
        this.Level = Level;
        this.OnlyLimit = OnlyLimit;
    }
}
