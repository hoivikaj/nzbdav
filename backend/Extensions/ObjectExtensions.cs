using System.Text.Json;

namespace NzbWebDAV.Extensions;

public static class ObjectExtensions
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string ToJson(this object obj)
    {
        return JsonSerializer.Serialize(obj);
    }

    public static string ToIndentedJson(this object obj)
    {
        return JsonSerializer.Serialize(obj, Indented);
    }
}
