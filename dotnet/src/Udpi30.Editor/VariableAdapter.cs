using System.ComponentModel;
using Udpi30.Core.Models;

namespace Udpi30.Editor;

/// <summary>
/// Editable wrapper around a top-level <see cref="VariableValue"/>, which in YAML
/// is either a bare string or an object with <c>description</c>/<c>default</c>.
/// The grid edits <see cref="Value"/> and <see cref="Description"/>; setting a
/// description switches the entry to the object form on write-back.
/// </summary>
public sealed class VariableAdapter
{
    private readonly Dictionary<string, VariableValue> _vars;

    [Browsable(false)]
    public string Key { get; set; }

    [Category("Variable")]
    [Description("The literal value, or the default value when a description is set.")]
    public string? Value { get; set; }

    [Category("Variable")]
    [Description("Optional description. When set, the variable is written in object form (description + default).")]
    public string? Description { get; set; }

    public VariableAdapter(Dictionary<string, VariableValue> vars, string key)
    {
        _vars = vars;
        Key = key;
        if (vars.TryGetValue(key, out var v))
        {
            if (v.RawString is not null)
            {
                Value = v.RawString;
            }
            else if (v.Definition is not null)
            {
                Value = v.Definition.Default;
                Description = v.Definition.Description;
            }
        }
    }

    /// <summary>Persist the edited values back into the variables map.</summary>
    public void WriteBack()
    {
        _vars[Key] = string.IsNullOrEmpty(Description)
            ? VariableValue.FromString(Value ?? "")
            : VariableValue.FromDefinition(new VariableDefinition { Description = Description, Default = Value });
    }
}
