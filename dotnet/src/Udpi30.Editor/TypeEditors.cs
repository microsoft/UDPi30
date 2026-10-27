using System.Collections;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Drawing.Design;
using System.Reflection;
using Udpi30.Core.Models;

namespace Udpi30.Editor;

/// <summary>
/// Wires up <see cref="TypeDescriptor"/> attributes so a stock
/// <see cref="PropertyGrid"/> can fully edit the Udpi30.Core model graph:
/// nested objects expand inline and can be created/edited via a dialog, and
/// the various dictionary shapes get a key/value editor.
/// </summary>
public static class EditorRegistry
{
    private static bool _registered;

    public static void RegisterAll()
    {
        if (_registered)
        {
            return;
        }
        _registered = true;

        // Every model class becomes expandable (so it renders inline) and gets a
        // modal editor (so null members can be created and deep graphs edited).
        var modelTypes = typeof(DeploymentDefinition).Assembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract
                        && t.Namespace == "Udpi30.Core.Models"
                        && t.GetConstructor(Type.EmptyTypes) is not null);

        foreach (var t in modelTypes)
        {
            TypeDescriptor.AddAttributes(
                t,
                new TypeConverterAttribute(typeof(ExpandableObjectConverter)),
                new EditorAttribute(typeof(ObjectDialogEditor), typeof(UITypeEditor)));
        }

        // Dictionary shapes used across the model.
        TypeDescriptor.AddAttributes(
            typeof(Dictionary<string, string>),
            new EditorAttribute(typeof(StringMapEditor), typeof(UITypeEditor)));
        TypeDescriptor.AddAttributes(
            typeof(Dictionary<string, object>),
            new EditorAttribute(typeof(StringMapEditor), typeof(UITypeEditor)));
        TypeDescriptor.AddAttributes(
            typeof(Dictionary<string, TableSchema>),
            new EditorAttribute(typeof(TypedMapEditor), typeof(UITypeEditor)));
    }
}

/// <summary>
/// Modal editor for a single nested model object. Creates the instance if the
/// property is currently null, and edits it in a dialog hosting a nested
/// <see cref="PropertyGrid"/>. Also offers a "clear" action to reset to null.
/// </summary>
public sealed class ObjectDialogEditor : UITypeEditor
{
    public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context)
        => UITypeEditorEditStyle.Modal;

    public override object? EditValue(
        ITypeDescriptorContext? context, IServiceProvider provider, object? value)
    {
        var type = context?.PropertyDescriptor?.PropertyType ?? value?.GetType();
        if (type is null)
        {
            return value;
        }

        var created = false;
        var instance = value;
        if (instance is null)
        {
            instance = Activator.CreateInstance(type);
            created = true;
        }
        if (instance is null)
        {
            return value;
        }

        var label = context?.PropertyDescriptor?.DisplayName ?? type.Name;
        using var dlg = new ObjectEditDialog(instance, label);
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            return dlg.Cleared ? null : instance;
        }
        return created ? value : instance;
    }
}

/// <summary>Modal key/value editor for <c>Dictionary&lt;string, string&gt;</c>
/// and <c>Dictionary&lt;string, object&gt;</c>. Mutates the dictionary in place.</summary>
public sealed class StringMapEditor : UITypeEditor
{
    public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context)
        => UITypeEditorEditStyle.Modal;

    public override object? EditValue(
        ITypeDescriptorContext? context, IServiceProvider provider, object? value)
    {
        if (value is not IDictionary dict)
        {
            return value;
        }
        var label = context?.PropertyDescriptor?.DisplayName ?? "Entries";
        using var dlg = new StringMapDialog(dict, label);
        dlg.ShowDialog();
        return value;
    }
}

/// <summary>Modal editor for a <c>Dictionary&lt;string, T&gt;</c> whose values are
/// complex model objects (e.g. lakehouse <c>tables</c>).</summary>
public sealed class TypedMapEditor : UITypeEditor
{
    public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context)
        => UITypeEditorEditStyle.Modal;

    public override object? EditValue(
        ITypeDescriptorContext? context, IServiceProvider provider, object? value)
    {
        if (value is not IDictionary dict)
        {
            return value;
        }
        var label = context?.PropertyDescriptor?.DisplayName ?? "Entries";
        using var dlg = new TypedMapDialog(dict, label);
        dlg.ShowDialog();
        return value;
    }
}
