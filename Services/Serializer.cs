using System;
using System.IO;
using System.Threading;
namespace ProSystem.Services;

public abstract class Serializer
{
    public abstract string DataDirectory { get; set; }
    public abstract void SerializeObject(object obj, string nameFile, Action<string> notify);
    public abstract object DeserializeObject(string nameFile);
}

internal class BinarySerializer : Serializer
{
    private string directory;
    public override string DataDirectory
    {
        get => directory;
        set => directory = value ?? throw new ArgumentNullException(nameof(value));
    }
    public BinarySerializer(string dataDirectory) => DataDirectory = dataDirectory;

    public override void SerializeObject(object obj, string nameFile, Action<string> notify)
    {
        if (obj == null) throw new ArgumentNullException(nameof(obj));
        if (nameFile == null) throw new ArgumentNullException(nameof(nameFile));
        if (notify == null) throw new ArgumentNullException(nameof(notify));

        string dirFile = DataDirectory + "/" + nameFile + ".bin";
        string dirCopyFile = DataDirectory + "/" + nameFile + " copy.bin";
        if (!Directory.Exists(DataDirectory)) Directory.CreateDirectory(DataDirectory);

        if (File.Exists(dirCopyFile))
        {
            notify("SerializeObject: копия " + nameFile + " уже существует");
            File.Move(dirCopyFile, DataDirectory + "/" + nameFile + " copy " + DateTime.Now.ToString("dd.MM.yyyy HH.mm.ss") + ".bin", true);
        }

        if (File.Exists(dirFile)) File.Copy(dirFile, dirCopyFile, true);

        try
        {
            using Stream myStream = new FileStream(dirFile, FileMode.Create, FileAccess.Write, FileShare.None);
            var formatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
            formatter.Serialize(myStream, obj);
        }
        catch (Exception ex)
        {
            notify("SerializeObject: " + nameFile + ": " + ex.Message);
            if (File.Exists(dirCopyFile))
            {
                Thread.Sleep(1500);
                try
                {
                    File.Move(dirCopyFile, dirFile, true);
                    notify("SerializeObject: Исходный файл восстановлен.");
                }
                catch (Exception e) { notify("SerializeObject: Исходный файл не восстановлен: " + e.Message); }
            }
            else notify("SerializeObject: Исходный файл не восстановлен, поскольку нет копии файла");
        }
        if (File.Exists(dirCopyFile)) File.Delete(dirCopyFile);
    }

    public override object DeserializeObject(string nameFile)
    {
        if (nameFile == null) throw new ArgumentNullException(nameof(nameFile));
        if (!Directory.Exists(DataDirectory)) throw new Exception("Нет данных для десериализации");
        if (!File.Exists(DataDirectory + "/" + nameFile))
            throw new ArgumentException("В директории " + directory + " не найден файл для десериализации", nameof(nameFile));

        var formatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
        using Stream myStream = new FileStream(DataDirectory + "/" + nameFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        return formatter.Deserialize(myStream);
    }
}
