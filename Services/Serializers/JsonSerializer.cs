using Newtonsoft.Json;
using System.IO;
using System.Runtime.Serialization;

namespace ProSystem.Services;

internal class JsonSerializer(string directory, AddInformation addInfo) : Serializer(directory, addInfo)
{
    private readonly JsonSerializerSettings settings = new()
    {
        Formatting = Formatting.Indented,
        TypeNameHandling = TypeNameHandling.All,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        MissingMemberHandling = MissingMemberHandling.Error
    };

    protected override string Format => ".json";

    protected override void SerializeObject<T>(T obj, string fileName)
    {
        ArgumentNullException.ThrowIfNull(obj, nameof(obj));
        var jsonString = JsonConvert.SerializeObject(obj, settings);
        File.WriteAllText(DataDirectory + "/" + fileName + ".json", jsonString);
    }

    protected override T DeserializeObject<T>(string fileName)
    {
        var jsonString = File.ReadAllText(DataDirectory + "/" + fileName + ".json");
        return (T)(JsonConvert.DeserializeObject(jsonString, settings) ??
            throw new SerializationException("Deserialized object is null"));
    }
}
