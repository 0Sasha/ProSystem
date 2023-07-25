using System;

namespace ProSystem.Services;

public interface ISerializer
{
    public string DataDirectory { get; set; }
    public void Serialize(object obj, string fileName);
    public object Deserialize(string fileName, Type type);
}
