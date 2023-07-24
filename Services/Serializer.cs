using System;

namespace ProSystem.Services;

public abstract class Serializer
{
    public abstract string DataDirectory { get; set; }
    public abstract void Serialize(object obj, string fileName);
    public abstract object Deserialize(string fileName, Type type);
}
