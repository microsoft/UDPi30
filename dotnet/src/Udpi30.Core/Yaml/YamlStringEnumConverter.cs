using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Udpi30.Core.Yaml;

/// <summary>
/// YamlDotNet converter that (de)serializes any enum decorated with
/// <see cref="EnumValueAttribute"/> using its string value, matching the
/// Python <c>str, Enum</c> wire format. Also handles <c>Nullable&lt;TEnum&gt;</c>.
/// </summary>
public sealed class YamlStringEnumConverter : IYamlTypeConverter
{
    private static Type? UnderlyingEnum(Type type)
    {
        if (type.IsEnum)
        {
            return type;
        }
        var nullable = Nullable.GetUnderlyingType(type);
        return nullable is { IsEnum: true } ? nullable : null;
    }

    public bool Accepts(Type type) => UnderlyingEnum(type) is not null;

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var enumType = UnderlyingEnum(type)!;
        var scalar = parser.Consume<Scalar>();
        if (string.IsNullOrEmpty(scalar.Value))
        {
            return null;
        }
        return EnumValueMap.Parse(enumType, scalar.Value);
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        var enumType = UnderlyingEnum(type)!;
        if (value is null)
        {
            emitter.Emit(new Scalar(string.Empty));
            return;
        }
        emitter.Emit(new Scalar(EnumValueMap.ToString(enumType, value)));
    }
}
