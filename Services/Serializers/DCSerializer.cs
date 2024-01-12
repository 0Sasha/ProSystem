using ProSystem.Algorithms;
using System.IO;
using System.Runtime.Serialization;
using System.Xml;

namespace ProSystem.Services;

internal class DCSerializer(string directory, AddInformation addInfo) : Serializer(directory, addInfo)
{
    private readonly DataContractSerializerSettings settings = new()
    {
        KnownTypes = new Type[]
        {
            typeof(Script), typeof(ScriptProperties), typeof(Position), typeof(Trade), typeof(Settings),
            typeof(Tool), typeof(Security), typeof(AD), typeof(ATRS), typeof(CCI), typeof(Channel),
            typeof(CHO), typeof(CMF), typeof(CMO), typeof(CrossMA), typeof(DeMarker), typeof(DPO),
            typeof(FRC), typeof(MA), typeof(MACD), typeof(MFI), typeof(OBV), typeof(PARS), typeof(ROC),
            typeof(RSI), typeof(RVI), typeof(Stochastic), typeof(StochRSI), typeof(SumLine)
        },
        SerializeReadOnlyTypes = true,
        PreserveObjectReferences = true
    };

    protected override string Format => ".xml";

    protected override void SerializeObject<T>(T obj, string fileName)
    {
        ArgumentNullException.ThrowIfNull(obj, nameof(obj));
        using FileStream fs = new(DataDirectory + "/" + fileName + ".xml", FileMode.Create);
        var ser = new DataContractSerializer(obj.GetType(), settings);
        ser.WriteObject(fs, obj);
        fs.Close();
    }

    protected override T DeserializeObject<T>(string fileName)
    {
        using var fs = new FileStream(DataDirectory + "/" + fileName + ".xml", FileMode.Open);
        using var reader = XmlDictionaryReader.CreateTextReader(fs, new XmlDictionaryReaderQuotas());
        var ser = new DataContractSerializer(typeof(T), settings);
        return (T)(ser.ReadObject(reader, true) ?? throw new SerializationException("Deserialized object is null"));
    }
}
