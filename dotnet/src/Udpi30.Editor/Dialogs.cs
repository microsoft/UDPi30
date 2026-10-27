using System.Collections;
using Udpi30.Core.Models;

namespace Udpi30.Editor;

/// <summary>A simple single-line text prompt dialog.</summary>
public sealed class InputDialog : Form
{
    private readonly TextBox _text = new() { Dock = DockStyle.Fill };

    private InputDialog(string title, string prompt, string initial)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(420, 120);

        var label = new Label { Text = prompt, Dock = DockStyle.Top, Height = 24 };
        _text.Text = initial;

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8),
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        body.Controls.Add(_text);
        body.Controls.Add(label);

        Controls.Add(body);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    /// <summary>Show the prompt; returns the entered text, or null if cancelled.</summary>
    public static string? Prompt(IWin32Window owner, string title, string prompt, string initial = "")
    {
        using var dlg = new InputDialog(title, prompt, initial);
        if (dlg.ShowDialog(owner) == DialogResult.OK)
        {
            var value = dlg._text.Text.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }
        return null;
    }

    private void InitializeComponent()
    {

    }
}

/// <summary>Pick a resource type and key when adding a new resource. The type
/// list is platform-aware (Fabric / Entra / Azure) with an optional filter.</summary>
public sealed class AddResourceDialog : Form
{
    private readonly ComboBox _platform = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _types = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _key = new() { Dock = DockStyle.Fill };

    public ResourceTypeInfo? SelectedType =>
        _types.SelectedItem is TypeItem item ? item.Info : null;

    public string Key => _key.Text.Trim();

    private sealed record TypeItem(ResourceTypeInfo Info)
    {
        public override string ToString()
        {
            var prefix = Info.Platform == ResourcePlatform.Fabric ? "" : $"[{Info.Platform}] ";
            var strict = Info.StrictNaming ? "  — strict naming" : "";
            return $"{prefix}{Info.FieldName}  —  {Info.ProviderType}{strict}";
        }
    }

    public AddResourceDialog()
    {
        Text = "Add Resource";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(520, 210);

        _platform.Items.AddRange(["All", "Fabric", "Entra", "Azure"]);
        _platform.SelectedIndex = 0;
        _platform.SelectedIndexChanged += (_, _) => PopulateTypes();
        PopulateTypes();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(10),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "Platform:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 0);
        layout.Controls.Add(_platform, 1, 0);
        layout.Controls.Add(new Label { Text = "Type:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 1);
        layout.Controls.Add(_types, 1, 1);
        layout.Controls.Add(new Label { Text = "Key:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 2);
        layout.Controls.Add(_key, 1, 2);

        var ok = new Button { Text = "Add", DialogResult = DialogResult.OK, Width = 90 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8),
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        Controls.Add(layout);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void PopulateTypes()
    {
        _types.BeginUpdate();
        _types.Items.Clear();
        var selected = _platform.SelectedItem as string ?? "All";
        var infos = ResourceTypeRegistry.All
            .Where(i => selected == "All" || i.Platform.ToString() == selected)
            .OrderBy(i => i.Platform)
            .ThenBy(i => i.FieldName, StringComparer.Ordinal);
        foreach (var info in infos)
        {
            _types.Items.Add(new TypeItem(info));
        }
        if (_types.Items.Count > 0)
        {
            _types.SelectedIndex = 0;
        }
        _types.EndUpdate();
    }
}

/// <summary>Hosts a nested <see cref="PropertyGrid"/> for editing one model object.</summary>
public sealed class ObjectEditDialog : Form
{
    /// <summary>True if the user chose to clear the value (set it to null).</summary>
    public bool Cleared { get; private set; }

    public ObjectEditDialog(object instance, string label)
    {
        Text = $"Edit — {label}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(460, 460);
        MinimumSize = new Size(360, 300);

        var grid = new PropertyGrid
        {
            Dock = DockStyle.Fill,
            SelectedObject = instance,
            PropertySort = PropertySort.NoSort,
            ToolbarVisible = false,
        };

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90 };
        var clear = new Button { Text = "Clear", Width = 90 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
        clear.Click += (_, _) => { Cleared = true; DialogResult = DialogResult.OK; };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8),
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        buttons.Controls.Add(clear);

        Controls.Add(grid);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}

/// <summary>Key/value grid editor for string-valued dictionaries.</summary>
public sealed class StringMapDialog : Form
{
    private readonly IDictionary _dict;
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = true,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
    };

    public StringMapDialog(IDictionary dict, string label)
    {
        _dict = dict;
        Text = $"Edit — {label}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(440, 360);
        MinimumSize = new Size(320, 240);

        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Key", Name = "Key" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Value", Name = "Value" });

        foreach (DictionaryEntry entry in _dict)
        {
            _grid.Rows.Add(entry.Key?.ToString() ?? "", entry.Value?.ToString() ?? "");
        }

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
        ok.Click += (_, _) => Apply();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8),
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        Controls.Add(_grid);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void Apply()
    {
        _dict.Clear();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }
            var key = row.Cells["Key"].Value?.ToString()?.Trim();
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }
            var value = row.Cells["Value"].Value?.ToString() ?? "";
            _dict[key] = value;
        }
    }
}

/// <summary>Editor for a dictionary keyed by string with complex model values.</summary>
public sealed class TypedMapDialog : Form
{
    private readonly IDictionary _dict;
    private readonly Type _valueType;
    private readonly ListBox _keys = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly PropertyGrid _grid = new() { Dock = DockStyle.Fill, ToolbarVisible = false };

    public TypedMapDialog(IDictionary dict, string label)
    {
        _dict = dict;
        _valueType = dict.GetType().GetGenericArguments()[1];

        Text = $"Edit — {label}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(620, 420);
        MinimumSize = new Size(480, 300);

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 200 };

        var add = new Button { Text = "Add…", Dock = DockStyle.Top, Height = 28 };
        var rename = new Button { Text = "Rename…", Dock = DockStyle.Top, Height = 28 };
        var remove = new Button { Text = "Remove", Dock = DockStyle.Top, Height = 28 };
        add.Click += (_, _) => OnAdd();
        rename.Click += (_, _) => OnRename();
        remove.Click += (_, _) => OnRemove();

        // Dock order: last added sits on top.
        split.Panel1.Controls.Add(_keys);
        split.Panel1.Controls.Add(remove);
        split.Panel1.Controls.Add(rename);
        split.Panel1.Controls.Add(add);
        split.Panel2.Controls.Add(_grid);

        _keys.SelectedIndexChanged += (_, _) => ShowSelected();

        var close = new Button { Text = "Close", DialogResult = DialogResult.OK, Width = 90 };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8),
        };
        buttons.Controls.Add(close);

        Controls.Add(split);
        Controls.Add(buttons);
        AcceptButton = close;

        ReloadKeys(null);
    }

    private void ReloadKeys(string? select)
    {
        _keys.Items.Clear();
        foreach (var key in _dict.Keys)
        {
            _keys.Items.Add(key?.ToString() ?? "");
        }
        if (select is not null)
        {
            _keys.SelectedItem = select;
        }
        else if (_keys.Items.Count > 0)
        {
            _keys.SelectedIndex = 0;
        }
        ShowSelected();
    }

    private void ShowSelected()
    {
        _grid.SelectedObject = _keys.SelectedItem is string k && _dict.Contains(k) ? _dict[k] : null;
    }

    private void OnAdd()
    {
        var key = InputDialog.Prompt(this, "Add Entry", "Key:");
        if (key is null)
        {
            return;
        }
        if (_dict.Contains(key))
        {
            MessageBox.Show(this, $"'{key}' already exists.", "Add Entry");
            return;
        }
        _dict[key] = Activator.CreateInstance(_valueType);
        ReloadKeys(key);
    }

    private void OnRename()
    {
        if (_keys.SelectedItem is not string oldKey)
        {
            return;
        }
        var newKey = InputDialog.Prompt(this, "Rename Entry", "New key:", oldKey);
        if (newKey is null || newKey == oldKey)
        {
            return;
        }
        if (_dict.Contains(newKey))
        {
            MessageBox.Show(this, $"'{newKey}' already exists.", "Rename Entry");
            return;
        }
        var value = _dict[oldKey];
        _dict.Remove(oldKey);
        _dict[newKey] = value;
        ReloadKeys(newKey);
    }

    private void OnRemove()
    {
        if (_keys.SelectedItem is string key)
        {
            _dict.Remove(key);
            ReloadKeys(null);
        }
    }
}
