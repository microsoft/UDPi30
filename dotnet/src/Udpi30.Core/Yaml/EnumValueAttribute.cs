namespace Udpi30.Core.Yaml;

/// <summary>
/// Associates the YAML/JSON string value with an enum member, mirroring the
/// string values of the Python <c>str, Enum</c> definitions (e.g. <c>adls_gen2</c>,
/// <c>1.2</c>) which are not valid C# identifiers.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class EnumValueAttribute(string value) : Attribute
{
    public string Value { get; } = value;
}
