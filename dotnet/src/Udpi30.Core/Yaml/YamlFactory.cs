using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Udpi30.Core.Yaml;

/// <summary>
/// Central YamlDotNet configuration. The typed (de)serializers use the
/// underscored naming convention so PascalCase C# properties map to the
/// snake_case keys used in <c>udp.yml</c> (e.g. <c>DefaultLakehouse</c> ↔
/// <c>default_lakehouse</c>), plus the custom enum/variable converters.
/// </summary>
public static class YamlFactory
{
    /// <summary>Deserializer for the strongly-typed model graph.</summary>
    public static IDeserializer CreateTypedDeserializer() =>
        new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .WithTypeConverter(new YamlStringEnumConverter())
            .WithTypeConverter(new YamlVariableValueConverter())
            .IgnoreUnmatchedProperties()
            .Build();

    /// <summary>Serializer for the strongly-typed model graph (used by dump).</summary>
    public static ISerializer CreateTypedSerializer() =>
        new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .WithTypeConverter(new YamlStringEnumConverter())
            .WithTypeConverter(new YamlVariableValueConverter())
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();

    /// <summary>Deserializer for an untyped object graph (Dictionary/List/string).</summary>
    public static IDeserializer CreateGenericDeserializer() =>
        new DeserializerBuilder().Build();

    /// <summary>Serializer for an untyped object graph.</summary>
    public static ISerializer CreateGenericSerializer() =>
        new SerializerBuilder().Build();
}
