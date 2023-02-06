using System;
using System.IO;
using System.Xml;
using System.Threading;
using System.Runtime.Serialization;
using ProSystem.Algorithms;
namespace ProSystem.Services;

public abstract class Serializer
{
    public abstract Action<string> Inform { get; set; }
    public abstract string DataDirectory { get; set; }
    public abstract void Serialize(object obj, string fileName);
    public abstract object Deserialize(string fileName, Type type);
}

internal class DCSerializer : Serializer
{
    private int usingMethod;
    private string directory;
    private Action<string> inform;
    private readonly DataContractSerializerSettings settings = new()
    {
        KnownTypes= new Type[]
        {
            typeof(Position), typeof(Trade), typeof(Settings), typeof(Tool), typeof(Security),
            typeof(AD), typeof(ATRS), typeof(CCI), typeof(Channel), typeof(CHO), typeof(CMF),
            typeof(CMO), typeof(CrossEMA), typeof(DeMarker), typeof(DPO), typeof(FRC), typeof(MA), typeof(MACD), typeof(MFI), typeof(OBV),
            typeof(PARS), typeof(ROC), typeof(RSI), typeof(RVI), typeof(Stochastic), typeof(StochRSI), typeof(SumLine)
        },
        SerializeReadOnlyTypes = true,
        PreserveObjectReferences = true
    };

    public override Action<string> Inform
    {
        get => inform;
        set => inform = value ?? throw new ArgumentNullException(nameof(value));
    }

    public override string DataDirectory
    {
        get => directory;
        set => directory = value ?? throw new ArgumentNullException(nameof(value));
    }

    public DCSerializer(string dataDirectory, Action<string> inform)
    {
        DataDirectory = dataDirectory;
        Inform = inform;
    }

    public override void Serialize(object obj, string fileName)
    {
        if (obj == null) throw new ArgumentNullException(nameof(obj));
        if (fileName == null) throw new ArgumentNullException(nameof(fileName));
        if (fileName == "") throw new ArgumentException("Пустое значение", nameof(fileName));

        string dirFile = DataDirectory + "/" + fileName + ".xml";
        string dirCopyFile = DataDirectory + "/" + fileName + " copy.xml";
        if (!Directory.Exists(DataDirectory)) Directory.CreateDirectory(DataDirectory);

        while (Interlocked.Exchange(ref usingMethod, 1) != 0) Thread.Sleep(150);
        try
        {
            if (File.Exists(dirCopyFile))
            {
                Inform("Serialize: копия " + fileName + " уже существует");
                File.Move(dirCopyFile, DataDirectory + "/" + fileName + " copy " + DateTime.Now.ToString("dd.MM.yyyy HH.mm.ss") + ".bin", true);
            }

            if (File.Exists(dirFile)) File.Copy(dirFile, dirCopyFile, true);
        }
        catch
        {
            Interlocked.Exchange(ref usingMethod, 0);
            throw;
        }

        try
        {
            using FileStream fs = new(DataDirectory + "/" + fileName + ".xml", FileMode.Create);
            DataContractSerializer ser = new(obj.GetType(), settings);
            ser.WriteObject(fs, obj);
            fs.Close();
            if (File.Exists(dirCopyFile)) File.Delete(dirCopyFile);
        }
        catch (Exception ex)
        {
            Inform("Serialize: " + fileName + ": " + ex.Message);
            if (File.Exists(dirCopyFile))
            {
                Thread.Sleep(1500);
                try
                {
                    File.Move(dirCopyFile, dirFile, true);
                    Inform("Serialize: Исходный файл восстановлен.");
                }
                catch (Exception e) { Inform("Serialize: Исходный файл не восстановлен: " + e.Message); }
            }
            else Inform("Serialize: Исходный файл не восстановлен, поскольку нет копии файла");
        }
        finally { Interlocked.Exchange(ref usingMethod, 0); }
    }

    public override object Deserialize(string fileName, Type type)
    {
        if (fileName == null) throw new ArgumentNullException(nameof(fileName));
        if (type == null) throw new ArgumentNullException(nameof(type));
        if (!Directory.Exists(DataDirectory)) throw new Exception("Директория " + DataDirectory + " не существует");
        if (!File.Exists(DataDirectory + "/" + fileName + ".xml"))
            throw new ArgumentException("В директории " + DataDirectory + " не найден файл для десериализации", nameof(fileName));

        using FileStream fs = new(DataDirectory + "/" + fileName + ".xml", FileMode.Open);
        using XmlDictionaryReader reader = XmlDictionaryReader.CreateTextReader(fs, new XmlDictionaryReaderQuotas());
        DataContractSerializer ser = new(type, settings);
        var obj = ser.ReadObject(reader, true);
        fs.Close();
        reader.Close();
        return obj;
    }
}

/*internal class BinarySerializer : Serializer
{
    private string directory;
    private Action<string> inform;

    public override Action<string> Inform
    {
        get => inform;
        set => inform = value ?? throw new ArgumentNullException(nameof(value));
    }

    public override string DataDirectory
    {
        get => directory;
        set => directory = value ?? throw new ArgumentNullException(nameof(value));
    }

    public BinarySerializer(string dataDirectory, Action<string> inform)
    {
        DataDirectory = dataDirectory;
        Inform = inform;
    }

    public override void Serialize(object obj, string fileName)
    {
        if (obj == null) throw new ArgumentNullException(nameof(obj));
        if (fileName == null) throw new ArgumentNullException(nameof(fileName));
        if (fileName == "") throw new ArgumentException("Пустое значение", nameof(fileName));

        string dirFile = DataDirectory + "/" + fileName + ".bin";
        string dirCopyFile = DataDirectory + "/" + fileName + " copy.bin";
        if (!Directory.Exists(DataDirectory)) Directory.CreateDirectory(DataDirectory);

        if (File.Exists(dirCopyFile))
        {
            Inform("Serialize: копия " + fileName + " уже существует");
            File.Move(dirCopyFile, DataDirectory + "/" + fileName + " copy " + DateTime.Now.ToString("dd.MM.yyyy HH.mm.ss") + ".bin", true);
        }

        if (File.Exists(dirFile)) File.Copy(dirFile, dirCopyFile, true);

        try
        {
            using Stream myStream = new FileStream(dirFile, FileMode.Create, FileAccess.Write, FileShare.None);
            var formatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
            formatter.Serialize(myStream, obj);
            myStream.Close();
        }
        catch (Exception ex)
        {
            Inform("Serialize: " + fileName + ": " + ex.Message);
            if (File.Exists(dirCopyFile))
            {
                Thread.Sleep(1500);
                try
                {
                    File.Move(dirCopyFile, dirFile, true);
                    Inform("Serialize: Исходный файл восстановлен.");
                }
                catch (Exception e) { Inform("Serialize: Исходный файл не восстановлен: " + e.Message); }
            }
            else Inform("Serialize: Исходный файл не восстановлен, поскольку нет копии файла");
        }
        if (File.Exists(dirCopyFile)) File.Delete(dirCopyFile);
    }

    public override object Deserialize(string fileName, Type _)
    {
        if (fileName == null) throw new ArgumentNullException(nameof(fileName));
        if (!Directory.Exists(DataDirectory)) throw new Exception("Директория " + DataDirectory + " не существует");
        if (!File.Exists(DataDirectory + "/" + fileName + ".bin"))
            throw new ArgumentException("В директории " + DataDirectory + " не найден файл для десериализации", nameof(fileName));

        var formatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
        using Stream myStream = new FileStream(DataDirectory + "/" + fileName + ".bin", FileMode.Open, FileAccess.Read, FileShare.Read);
        return formatter.Deserialize(myStream);
    }
}
*/
