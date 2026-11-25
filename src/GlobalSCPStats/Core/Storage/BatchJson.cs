using System.IO;
using System.Runtime.Serialization.Json;
using GlobalSCPStats.Core.Models;

namespace GlobalSCPStats.Core.Storage;

public static class BatchJson
{
    public static byte[] Serialize(StatsBatch batch)
    {
        using var stream = new MemoryStream();
        Serializer().WriteObject(stream, batch);
        return stream.ToArray();
    }

    public static StatsBatch Deserialize(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return (StatsBatch)Serializer().ReadObject(stream)!;
    }

    private static DataContractJsonSerializer Serializer() => new DataContractJsonSerializer(typeof(StatsBatch),
        new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
}
