using System.IO;

namespace ProSystem.Services;

public abstract class Serializer(string dataDirectory, AddInformation addInfo)
{
    private int occupied;
    private readonly AddInformation AddInfo = addInfo;

    protected abstract string Format { get; }
    protected readonly string DataDirectory = dataDirectory;

    public T TryDeserialize<T>(string? fileName = null) where T : new()
    {
        try
        {
            fileName ??= typeof(T).Name;
            return Deserialize<T>(fileName);
        }
        catch (Exception ex)
        {
            AddInfo("Serializer: " + ex.Message);
            return new T();
        }
    }

    public void Serialize<T>(T obj, string fileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName, nameof(fileName));
        var dirFile = DataDirectory + "/" + fileName + Format;
        var dirCopyFile = DataDirectory + "/" + fileName + " copy" + Format;
        if (!Directory.Exists(DataDirectory)) Directory.CreateDirectory(DataDirectory);

        while (Interlocked.Exchange(ref occupied, 1) != 0) Thread.Sleep(150);
        try
        {
            if (File.Exists(dirCopyFile))
            {
                AddInfo("Serialize: copy of the " + fileName + " already exists");
                File.Move(dirCopyFile, DataDirectory + "/" + fileName + " copy " +
                    DateTime.Now.ToString("dd.MM.yyyy HH.mm.ss") + Format, true);
            }

            if (File.Exists(dirFile)) File.Copy(dirFile, dirCopyFile, true);
        }
        catch
        {
            Interlocked.Exchange(ref occupied, 0);
            throw;
        }

        try
        {
            SerializeObject(obj, fileName);
            if (File.Exists(dirCopyFile)) File.Delete(dirCopyFile);
        }
        catch (Exception ex)
        {
            AddInfo("Serialize: " + fileName + ": " + ex.Message, true, true);
            if (File.Exists(dirCopyFile))
            {
                Thread.Sleep(1500);
                try
                {
                    File.Move(dirCopyFile, dirFile, true);
                    AddInfo("Serialize: source file is recovered.");
                }
                catch (Exception e) { AddInfo("Serialize: source file is not recovered: " + e.Message); }
            }
            else AddInfo("Serialize: source file is not recovered, no its copy");
        }
        finally
        {
            Interlocked.Exchange(ref occupied, 0);
        }
    }

    public T Deserialize<T>(string fileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        if (!Directory.Exists(DataDirectory)) throw new Exception("Directory " + DataDirectory + " does not exist");
        if (!File.Exists(DataDirectory + "/" + fileName + Format))
            throw new ArgumentException("File " + fileName + Format + " is not found in " + DataDirectory);
        return DeserializeObject<T>(fileName);
    }

    protected abstract void SerializeObject<T>(T obj, string fileName);

    protected abstract T DeserializeObject<T>(string fileName);
}
