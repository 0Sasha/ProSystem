using System;

namespace ProSystem;

public record class Market(string ID, string Name);

public record class TimeFrame(string ID, int Seconds, string Name = null);

public record class ClientAccount(string ID, string Market, string Union);

[Serializable]
public record class ScriptProperties(bool IsOSC, string[] UpperProperties,
    string[] MiddleProperties = null, string MAProperty = null, NameMA[] MAObjects = null);
