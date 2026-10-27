using System.Collections.Concurrent;
using System.Reflection;

namespace Udpi30.Core.Yaml;

/// <summary>
/// Caches the bidirectional mapping between enum members and their
/// <see cref="EnumValueAttribute"/> string values (falling back to a
/// lower-cased member name). Shared by the YAML and JSON converters.
/// </summary>
internal static class EnumValueMap
{
    private static readonly ConcurrentDictionary<Type, (Dictionary<string, object> ToEnum, Dictionary<object, string> ToStr)> Cache = new();

    private static (Dictionary<string, object> ToEnum, Dictionary<object, string> ToStr) For(Type enumType)
    {
        return Cache.GetOrAdd(enumType, static t =>
        {
            var toEnum = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var toString = new Dictionary<object, string>();
            foreach (var field in t.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var value = field.GetValue(null)!;
                var attr = field.GetCustomAttribute<EnumValueAttribute>();
                var str = attr?.Value ?? field.Name.ToLowerInvariant();
                toEnum[str] = value;
                toString[value] = str;
            }
            return (toEnum, toString);
        });
    }

    public static object Parse(Type enumType, string value)
    {
        var (toEnum, _) = For(enumType);
        if (toEnum.TryGetValue(value, out var result))
        {
            return result;
        }
        // Tolerate exact enum-name matches too.
        if (Enum.TryParse(enumType, value, ignoreCase: true, out var parsed) && parsed is not null)
        {
            return parsed;
        }
        throw new ArgumentException($"'{value}' is not a valid value for {enumType.Name}.");
    }

    public static string ToString(Type enumType, object value)
    {
        var (_, toStr) = For(enumType);
        return toStr.TryGetValue(value, out var str) ? str : value.ToString()!.ToLowerInvariant();
    }
}
