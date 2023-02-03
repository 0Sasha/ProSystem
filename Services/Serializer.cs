using System;
using System.IO;
namespace ProSystem.Services;

abstract class Serializer
{
    public abstract string DataDirectory { get; set; }
    public abstract void SerializeObject(object obj, string nameFile);
    public abstract object DeserializeObject(string nameFile);
}

internal class BinarySerializer : Serializer
{
    public override string DataDirectory { get; set; }
    public BinarySerializer(string dataDirectory) => DataDirectory = dataDirectory;

    public override void SerializeObject(object obj, string nameFile)
    {
        if (!Directory.Exists(DataDirectory)) Directory.CreateDirectory(DataDirectory);

        if (File.Exists(DataDirectory + "/" + nameFile + " copy.bin"))
        {
            File.Move(DataDirectory + "/" + nameFile + " copy.bin",
                DataDirectory + "/" + nameFile + " copy " + DateTime.Now.ToString("dd.MM.yyyy HH.mm.ss") + ".bin", true);
        }

        if (File.Exists(DataDirectory + "/" + nameFile + ".bin"))
            File.Copy(DataDirectory + "/" + nameFile + ".bin", DataDirectory + "/" + nameFile + " copy.bin", true);

        using Stream myStream = new FileStream(DataDirectory + "/" + nameFile + ".bin", FileMode.Create, FileAccess.Write, FileShare.None);
        var formatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
        formatter.Serialize(myStream, obj);

        if (File.Exists(DataDirectory + "/" + nameFile + " copy.bin")) File.Delete(DataDirectory + "/" + nameFile + " copy.bin");
    }

    public override object DeserializeObject(string nameFile)
    {
        var formatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
        using Stream myStream = new FileStream(DataDirectory + "/" + nameFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        return formatter.Deserialize(myStream);
    }
}
