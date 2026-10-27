using Udpi30.Core.Models;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Udpi30.Core.Yaml;

/// <summary>
/// Deserializes a <see cref="VariableValue"/> from either a YAML scalar (plain
/// string value) or a mapping (a <see cref="VariableDefinition"/> with
/// <c>description</c>/<c>default</c>).
/// </summary>
public sealed class YamlVariableValueConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(VariableValue);

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        if (parser.Current is Scalar)
        {
            var scalar = parser.Consume<Scalar>();
            return VariableValue.FromString(scalar.Value);
        }

        var def = (VariableDefinition?)rootDeserializer(typeof(VariableDefinition));
        return VariableValue.FromDefinition(def ?? new VariableDefinition());
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        if (value is not VariableValue v)
        {
            emitter.Emit(new Scalar(string.Empty));
            return;
        }

        if (v.RawString is not null)
        {
            emitter.Emit(new Scalar(v.RawString));
        }
        else
        {
            serializer(v.Definition ?? new VariableDefinition(), typeof(VariableDefinition));
        }
    }
}
