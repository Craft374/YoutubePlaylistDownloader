using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YPD.Core;

/// <summary>설정 파일에 쓰는 JSON 옵션. 키는 camelCase, enum은 문자열("audioOriginal")입니다.</summary>
public static class CoreJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // 숫자 enum은 범위를 벗어난 값도 그대로 받아들이므로 막습니다.
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };
}

/// <summary>
/// 키마다 따로 읽어, 없거나 null이거나 읽을 수 없는 값(예: 없어진 enum 이름)은 기본값을 씁니다.
/// 새 옵션을 추가하거나 값 하나가 잘못돼도 나머지 설정이 지워지지 않습니다.
/// </summary>
public sealed class LenientJsonConverter<T> : JsonConverter<T> where T : class, new()
{
    private static readonly PropertyInfo[] Properties = typeof(T)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
        .ToArray();

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var result = new T();
        if (document.RootElement.ValueKind != JsonValueKind.Object) return result;
        foreach (var json in document.RootElement.EnumerateObject())
        {
            if (json.Value.ValueKind == JsonValueKind.Null) continue;
            var property = Array.Find(Properties, p => string.Equals(Name(p, options), json.Name, StringComparison.OrdinalIgnoreCase));
            if (property == null) continue;
            try
            {
                property.SetValue(result, json.Value.Deserialize(property.PropertyType, options));
            }
            catch (JsonException)
            {
                // 이 키만 기본값으로 둡니다.
            }
        }
        return result;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var property in Properties)
        {
            writer.WritePropertyName(Name(property, options));
            JsonSerializer.Serialize(writer, property.GetValue(value), property.PropertyType, options);
        }
        writer.WriteEndObject();
    }

    private static string Name(PropertyInfo property, JsonSerializerOptions options) =>
        options.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name;
}
