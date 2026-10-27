using System.Collections;
using System.Reflection;
using System.Text;
using Udpi30.Core.Models;

namespace Udpi30.Editor;

/// <summary>Main window: a tree of the deployment on the left, a property grid on the right.</summary>
public sealed class MainForm : Form
{
    private DeploymentDefinition _def = NewDeployment();
    private string? _path;
    private bool _dirty;

    private readonly TreeView _tree = new()
    {
        Dock = DockStyle.Fill,
        HideSelection = false,
        LabelEdit = true,
        PathSeparator = "/",
    };

    private readonly PropertyGrid _grid = new()
    {
        Dock = DockStyle.Fill,
        PropertySort = PropertySort.NoSort,
    };

    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

    public MainForm()
    {
        Text = "udp.yml Editor";
        ClientSize = new Size(1000, 680);
        StartPosition = FormStartPosition.CenterScreen;

        // Add order matters for docking: the Fill control must be added first so
        // it ends up at the back of the z-order and receives the leftover area;
        // edge-docked controls are added after, outermost last.
        BuildBody();
        BuildStatus();
        BuildToolbar();
        BuildMenu();
        BuildContextMenu();

        _grid.PropertyValueChanged += (_, _) =>
        {
            MarkDirty();
            if (_tree.SelectedNode?.Tag is NodeMeta { Kind: NodeKind.Variable } meta)
            {
                if (meta.Target is VariableAdapter adapter)
                {
                    adapter.WriteBack();
                }
            }
        };

        _tree.AfterSelect += (_, e) => _grid.SelectedObject = (e.Node?.Tag as NodeMeta)?.Target;
        _tree.BeforeLabelEdit += (_, e) =>
        {
            if (e.Node?.Tag is not NodeMeta { CanRename: true })
            {
                e.CancelEdit = true;
            }
        };
        _tree.AfterLabelEdit += OnAfterLabelEdit;

        FormClosing += (_, e) =>
        {
            if (!ConfirmDiscardIfDirty())
            {
                e.Cancel = true;
            }
        };

        RebuildTree();
        UpdateTitle();
    }

    /// <summary>Open a file specified on the command line, after the form is constructed.</summary>
    public void OpenOnLoad(string path) => LoadFile(path);

    // -- UI construction -----------------------------------------------------

    private void BuildMenu()
    {
        var menu = new MenuStrip();

        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add(new ToolStripMenuItem("&New", null, (_, _) => NewFile()) { ShortcutKeys = Keys.Control | Keys.N });
        file.DropDownItems.Add(new ToolStripMenuItem("&Open…", null, (_, _) => OpenFile()) { ShortcutKeys = Keys.Control | Keys.O });
        file.DropDownItems.Add(new ToolStripMenuItem("&Save", null, (_, _) => SaveFile()) { ShortcutKeys = Keys.Control | Keys.S });
        file.DropDownItems.Add(new ToolStripMenuItem("Save &As…", null, (_, _) => SaveFileAs()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, (_, _) => Close()));

        var edit = new ToolStripMenuItem("&Edit");
        edit.DropDownItems.Add(new ToolStripMenuItem("Add &Resource…", null, (_, _) => AddResource()) { ShortcutKeys = Keys.Control | Keys.R });
        edit.DropDownItems.Add(new ToolStripMenuItem("Add &Variable…", null, (_, _) => AddVariable()));
        edit.DropDownItems.Add(new ToolStripMenuItem("Add &Connection…", null, (_, _) => AddConnection()));
        edit.DropDownItems.Add(new ToolStripMenuItem("Add &Target…", null, (_, _) => AddTarget()));
        edit.DropDownItems.Add(new ToolStripMenuItem("Add Tenant &Setting…", null, (_, _) => AddTenantSetting()));
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(new ToolStripMenuItem("Re&name Selected", null, (_, _) => RenameSelected()) { ShortcutKeys = Keys.F2 });
        edit.DropDownItems.Add(new ToolStripMenuItem("&Remove Selected", null, (_, _) => RemoveSelected()) { ShortcutKeys = Keys.Delete });

        var tools = new ToolStripMenuItem("&Tools");
        tools.DropDownItems.Add(new ToolStripMenuItem("&Import from deployed environment…", null, (_, _) => ImportFromDeployed()) { ShortcutKeys = Keys.Control | Keys.I });
        tools.DropDownItems.Add(new ToolStripSeparator());
        tools.DropDownItems.Add(new ToolStripMenuItem("&Validate", null, (_, _) => ValidateDeployment()) { ShortcutKeys = Keys.F5 });
        tools.DropDownItems.Add(new ToolStripMenuItem("View &YAML", null, (_, _) => ViewYaml()));
        tools.DropDownItems.Add(new ToolStripSeparator());
        tools.DropDownItems.Add(new ToolStripMenuItem("&Group items into workspace folders by type", null, (_, _) => ToggleFoldersByType()));

        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add(new ToolStripMenuItem("&About", null, (_, _) => ShowAbout()));

        menu.Items.AddRange([file, edit, tools, help]);
        MainMenuStrip = menu;
        Controls.Add(menu);
    }

    private void BuildToolbar()
    {
        var bar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
        bar.Items.Add(new ToolStripButton("New", null, (_, _) => NewFile()));
        bar.Items.Add(new ToolStripButton("Open", null, (_, _) => OpenFile()));
        bar.Items.Add(new ToolStripButton("Save", null, (_, _) => SaveFile()));
        bar.Items.Add(new ToolStripSeparator());
        bar.Items.Add(new ToolStripButton("Add Resource", null, (_, _) => AddResource()));
        bar.Items.Add(new ToolStripButton("Rename", null, (_, _) => RenameSelected()));
        bar.Items.Add(new ToolStripButton("Remove", null, (_, _) => RemoveSelected()));
        bar.Items.Add(new ToolStripSeparator());
        bar.Items.Add(new ToolStripButton("Validate", null, (_, _) => ValidateDeployment()));
        bar.Items.Add(new ToolStripButton("View YAML", null, (_, _) => ViewYaml()));
        Controls.Add(bar);
    }

    private void BuildBody()
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 320 };
        split.Panel1.Controls.Add(_tree);
        split.Panel2.Controls.Add(_grid);
        Controls.Add(split);
    }

    private void InitializeComponent()
    {

    }

    private void BuildStatus()
    {
        _status.Items.Add(_statusLabel);
        Controls.Add(_status);
    }

    private ContextMenuStrip _treeMenu = null!;

    private void BuildContextMenu()
    {
        _treeMenu = new ContextMenuStrip();
        _treeMenu.Items.Add(new ToolStripMenuItem("Add Resource…", null, (_, _) => AddResource()));
        _treeMenu.Items.Add(new ToolStripMenuItem("Rename", null, (_, _) => RenameSelected()));
        _treeMenu.Items.Add(new ToolStripMenuItem("Remove", null, (_, _) => RemoveSelected()));
        _tree.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                var node = _tree.GetNodeAt(e.X, e.Y);
                if (node is not null)
                {
                    _tree.SelectedNode = node;
                }
            }
        };
        _tree.ContextMenuStrip = _treeMenu;
    }

    // -- tree ----------------------------------------------------------------

    private void RebuildTree()
    {
        var selectedPath = _tree.SelectedNode?.FullPath;
        _tree.BeginUpdate();
        _tree.Nodes.Clear();

        AddNode(null, $"Deployment: {_def.Deployment.Name}", new NodeMeta { Kind = NodeKind.Deployment, Target = _def.Deployment });
        AddNode(null, "Workspace", new NodeMeta { Kind = NodeKind.Workspace, Target = _def.Workspace });
        AddNode(null, "Azure (defaults)", new NodeMeta { Kind = NodeKind.Azure, Target = _def.Azure });

        var variables = AddNode(null, $"Variables ({_def.Variables.Count})", new NodeMeta { Kind = NodeKind.Container });
        foreach (var key in _def.Variables.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            AddNode(variables, key, new NodeMeta
            {
                Kind = NodeKind.Variable,
                Key = key,
                Target = new VariableAdapter(_def.Variables, key),
            });
        }

        var resources = AddNode(null, "Resources", new NodeMeta { Kind = NodeKind.Container });

        // Fabric types sit directly under Resources (the common case); Entra and
        // Azure types are grouped under a platform node so 100+ types stay navigable.
        AddResourceTypeNodes(resources, ResourceTypeRegistry.ForPlatform(ResourcePlatform.Fabric));
        foreach (var platform in new[] { ResourcePlatform.Entra, ResourcePlatform.Azure })
        {
            var infos = ResourceTypeRegistry.ForPlatform(platform)
                .Where(i => DictFor(i.FieldName).Count > 0).ToList();
            if (infos.Count == 0)
            {
                continue;
            }
            var total = infos.Sum(i => DictFor(i.FieldName).Count);
            var platformNode = AddNode(resources, $"{platform} ({total})", new NodeMeta { Kind = NodeKind.Container });
            AddResourceTypeNodes(platformNode, infos);
        }

        AddNode(null, $"Security roles ({_def.Security.Roles.Count})",
            new NodeMeta { Kind = NodeKind.Security, Target = _def.Security });

        var connections = AddNode(null, $"Connections ({_def.Connections.Count})",
            new NodeMeta { Kind = NodeKind.Container });
        foreach (var key in _def.Connections.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            AddNode(connections, key, new NodeMeta
            {
                Kind = NodeKind.Connection,
                Key = key,
                Target = _def.Connections[key],
            });
        }

        var targets = AddNode(null, $"Targets ({_def.Targets.Count})",
            new NodeMeta { Kind = NodeKind.Container });
        foreach (var key in _def.Targets.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            AddNode(targets, key, new NodeMeta
            {
                Kind = NodeKind.Target,
                Key = key,
                Target = _def.Targets[key],
            });
        }

        var admin = AddNode(null, $"Admin / tenant settings ({_def.Admin.TenantSettings.Count})",
            new NodeMeta { Kind = NodeKind.Container });
        foreach (var key in _def.Admin.TenantSettings.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            AddNode(admin, key, new NodeMeta
            {
                Kind = NodeKind.TenantSetting,
                Key = key,
                Target = _def.Admin.TenantSettings[key],
            });
        }

        var advanced = AddNode(null, "Advanced", new NodeMeta { Kind = NodeKind.Container });
        AddNode(advanced, "Policies", new NodeMeta { Kind = NodeKind.Policies, Target = _def.Policies });
        AddNode(advanced, "Notifications", new NodeMeta { Kind = NodeKind.Notifications, Target = _def.Notifications });
        AddNode(advanced, "State", new NodeMeta { Kind = NodeKind.State, Target = _def.State });

        foreach (TreeNode top in _tree.Nodes)
        {
            top.Expand();
        }

        _tree.EndUpdate();

        if (selectedPath is not null)
        {
            SelectByPath(selectedPath);
        }
    }

    /// <summary>Add a type node (and its resource children) for each non-empty type.</summary>
    private void AddResourceTypeNodes(TreeNode parent, IEnumerable<ResourceTypeInfo> infos)
    {
        foreach (var info in infos)
        {
            var dict = DictFor(info.FieldName);
            if (dict.Count == 0)
            {
                continue;
            }
            var typeNode = AddNode(parent, $"{info.FieldName} ({dict.Count})",
                new NodeMeta { Kind = NodeKind.ResourceType });
            foreach (var keyObj in dict.Keys)
            {
                var key = (string)keyObj;
                AddNode(typeNode, key, new NodeMeta
                {
                    Kind = NodeKind.Resource,
                    Key = key,
                    ResourceField = info.FieldName,
                    Target = dict[key],
                });
            }
        }
    }

    private TreeNode AddNode(TreeNode? parent, string text, NodeMeta meta)
    {
        var node = new TreeNode(text) { Tag = meta };
        if (parent is null)
        {
            _tree.Nodes.Add(node);
        }
        else
        {
            parent.Nodes.Add(node);
        }
        return node;
    }

    private void SelectByPath(string path)
    {
        var parts = path.Split('/');
        TreeNodeCollection nodes = _tree.Nodes;
        TreeNode? current = null;
        foreach (var part in parts)
        {
            current = nodes.Cast<TreeNode>().FirstOrDefault(n => n.Text == part);
            if (current is null)
            {
                break;
            }
            nodes = current.Nodes;
        }
        if (current is not null)
        {
            _tree.SelectedNode = current;
        }
    }

    // -- node label editing (rename) ----------------------------------------

    private void OnAfterLabelEdit(object? sender, NodeLabelEditEventArgs e)
    {
        if (e.Node?.Tag is not NodeMeta meta || !meta.CanRename)
        {
            e.CancelEdit = true;
            return;
        }
        var newKey = e.Label?.Trim();
        if (string.IsNullOrEmpty(newKey) || newKey == meta.Key)
        {
            e.CancelEdit = true;
            return;
        }

        var ok = meta.Kind switch
        {
            NodeKind.Variable => RenameDictKey(_def.Variables, meta, newKey),
            NodeKind.Connection => RenameDictKey(_def.Connections, meta, newKey),
            NodeKind.Target => RenameDictKey(_def.Targets, meta, newKey),
            NodeKind.TenantSetting => RenameDictKey(_def.Admin.TenantSettings, meta, newKey),
            NodeKind.Resource => RenameResource(meta, newKey),
            _ => false,
        };

        if (!ok)
        {
            e.CancelEdit = true;
            return;
        }

        meta.Key = newKey;
        MarkDirty();
        // Defer the label commit; cancel and set text ourselves to keep counts/labels consistent.
        e.CancelEdit = true;
        e.Node.Text = newKey;
    }

    private bool RenameDictKey<TValue>(Dictionary<string, TValue> dict, NodeMeta meta, string newKey)
    {
        if (meta.Key is null || !dict.TryGetValue(meta.Key, out var value) || dict.ContainsKey(newKey))
        {
            if (dict.ContainsKey(newKey))
            {
                MessageBox.Show(this, $"'{newKey}' already exists.", "Rename");
            }
            return false;
        }
        dict.Remove(meta.Key);
        dict[newKey] = value;
        if (meta.Target is VariableAdapter adapter)
        {
            adapter.Key = newKey;
        }
        return true;
    }

    private bool RenameResource(NodeMeta meta, string newKey)
    {
        if (meta.ResourceField is null || meta.Key is null)
        {
            return false;
        }
        var dict = DictFor(meta.ResourceField);
        if (!dict.Contains(meta.Key))
        {
            return false;
        }
        if (dict.Contains(newKey))
        {
            MessageBox.Show(this, $"'{newKey}' already exists.", "Rename");
            return false;
        }
        var value = dict[meta.Key];
        dict.Remove(meta.Key);
        dict[newKey] = value;
        return true;
    }

    private void RenameSelected()
    {
        if (_tree.SelectedNode?.Tag is NodeMeta { CanRename: true })
        {
            _tree.SelectedNode.BeginEdit();
        }
    }

    // -- add / remove --------------------------------------------------------

    private void AddResource()
    {
        using var dlg = new AddResourceDialog();
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedType is not { } info)
        {
            return;
        }
        var key = dlg.Key;
        if (string.IsNullOrEmpty(key))
        {
            MessageBox.Show(this, "A resource key is required.", "Add Resource");
            return;
        }
        var dict = DictFor(info.FieldName);
        if (dict.Contains(key))
        {
            MessageBox.Show(this, $"'{key}' already exists under {info.FieldName}.", "Add Resource");
            return;
        }
        var valueType = typeof(ResourcesConfig).GetProperty(info.PropertyName)!
            .PropertyType.GetGenericArguments()[1];
        dict[key] = Activator.CreateInstance(valueType);
        MarkDirty();
        RebuildTree();
        SelectResource(info.FieldName, key);
    }

    private void AddVariable()
    {
        var key = InputDialog.Prompt(this, "Add Variable", "Variable name:");
        if (key is null)
        {
            return;
        }
        if (_def.Variables.ContainsKey(key))
        {
            MessageBox.Show(this, $"Variable '{key}' already exists.", "Add Variable");
            return;
        }
        _def.Variables[key] = VariableValue.FromString("");
        MarkDirty();
        RebuildTree();
    }

    private void AddConnection()
    {
        var key = InputDialog.Prompt(this, "Add Connection", "Connection name:");
        if (key is null)
        {
            return;
        }
        if (_def.Connections.ContainsKey(key))
        {
            MessageBox.Show(this, $"Connection '{key}' already exists.", "Add Connection");
            return;
        }
        _def.Connections[key] = new ConnectionConfig();
        MarkDirty();
        RebuildTree();
    }

    private void AddTarget()
    {
        var key = InputDialog.Prompt(this, "Add Target", "Target name (e.g. dev, prod):");
        if (key is null)
        {
            return;
        }
        if (_def.Targets.ContainsKey(key))
        {
            MessageBox.Show(this, $"Target '{key}' already exists.", "Add Target");
            return;
        }
        _def.Targets[key] = new TargetConfig { Workspace = new WorkspaceConfig() };
        MarkDirty();
        RebuildTree();
    }

    private void AddTenantSetting()
    {
        var key = InputDialog.Prompt(this, "Add Tenant Setting", "Setting name (API settingName, e.g. PublishToWeb):");
        if (key is null)
        {
            return;
        }
        if (_def.Admin.TenantSettings.ContainsKey(key))
        {
            MessageBox.Show(this, $"Tenant setting '{key}' already exists.", "Add Tenant Setting");
            return;
        }
        _def.Admin.TenantSettings[key] = new TenantSetting();
        MarkDirty();
        RebuildTree();
    }

    private void RemoveSelected()
    {
        if (_tree.SelectedNode?.Tag is not NodeMeta meta || !meta.CanRemove || meta.Key is null)
        {
            return;
        }
        if (MessageBox.Show(this, $"Remove '{meta.Key}'?", "Remove",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        switch (meta.Kind)
        {
            case NodeKind.Variable:
                _def.Variables.Remove(meta.Key);
                break;
            case NodeKind.Connection:
                _def.Connections.Remove(meta.Key);
                break;
            case NodeKind.Target:
                _def.Targets.Remove(meta.Key);
                break;
            case NodeKind.TenantSetting:
                _def.Admin.TenantSettings.Remove(meta.Key);
                break;
            case NodeKind.Resource when meta.ResourceField is not null:
                DictFor(meta.ResourceField).Remove(meta.Key);
                break;
        }
        MarkDirty();
        RebuildTree();
    }

    private void SelectResource(string field, string key)
    {
        // Resource nodes may be nested under a platform group (Entra/Azure), so
        // search the whole tree rather than assuming a fixed depth.
        var match = FindResourceNode(_tree.Nodes, field, key);
        if (match is not null)
        {
            _tree.SelectedNode = match;
        }
    }

    private static TreeNode? FindResourceNode(TreeNodeCollection nodes, string field, string key)
    {
        foreach (TreeNode node in nodes)
        {
            if (node.Tag is NodeMeta { Kind: NodeKind.Resource } m
                && m.ResourceField == field && m.Key == key)
            {
                return node;
            }
            var found = FindResourceNode(node.Nodes, field, key);
            if (found is not null)
            {
                return found;
            }
        }
        return null;
    }

    // -- file operations -----------------------------------------------------

    private void NewFile()
    {
        if (!ConfirmDiscardIfDirty())
        {
            return;
        }
        _def = NewDeployment();
        _path = null;
        _dirty = false;
        RebuildTree();
        UpdateTitle();
    }

    private void OpenFile()
    {
        if (!ConfirmDiscardIfDirty())
        {
            return;
        }
        using var dlg = new OpenFileDialog
        {
            Filter = "udp.yml files (*.yml;*.yaml)|*.yml;*.yaml|All files (*.*)|*.*",
            Title = "Open udp.yml",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            LoadFile(dlg.FileName);
        }
    }

    private void LoadFile(string path)
    {
        try
        {
            _def = DeploymentIo.Load(path);
            _path = path;
            _dirty = false;
            RebuildTree();
            UpdateTitle();
            SetStatus($"Loaded {path}");
        }
        catch (Exception ex)
        {
            var detail = ex.Message;
            for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
            {
                detail += "\n→ " + inner.Message;
            }
            MessageBox.Show(this, $"Could not open file:\n\n{detail}", "Open",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool SaveFile()
    {
        if (_path is null)
        {
            return SaveFileAs();
        }
        return WriteTo(_path);
    }

    private bool SaveFileAs()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "udp.yml files (*.yml;*.yaml)|*.yml;*.yaml|All files (*.*)|*.*",
            Title = "Save udp.yml",
            FileName = _path is null ? "udp.yml" : Path.GetFileName(_path),
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            return WriteTo(dlg.FileName);
        }
        return false;
    }

    private bool WriteTo(string path)
    {
        try
        {
            DeploymentIo.Save(_def, path);
            _path = path;
            _dirty = false;
            UpdateTitle();
            SetStatus($"Saved {path}");
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not save file:\n\n{ex.Message}", "Save",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private bool ConfirmDiscardIfDirty()
    {
        if (!_dirty)
        {
            return true;
        }
        var result = MessageBox.Show(this,
            "You have unsaved changes. Save before continuing?",
            "Unsaved changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        return result switch
        {
            DialogResult.Yes => SaveFile(),
            DialogResult.No => true,
            _ => false,
        };
    }

    // -- tools ---------------------------------------------------------------

    private void ValidateDeployment()
    {
        var errors = DeploymentIo.Validate(_def);
        if (errors.Count == 0)
        {
            SetStatus("Validation passed.");
            MessageBox.Show(this, "✓ Deployment is valid.", "Validate",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        SetStatus($"Validation failed: {errors.Count} issue(s).");
        var sb = new StringBuilder("Validation found the following issues:\n\n");
        foreach (var error in errors)
        {
            sb.Append("• ").AppendLine(error);
        }
        MessageBox.Show(this, sb.ToString(), "Validate",
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void ViewYaml()
    {
        string yaml;
        try
        {
            yaml = DeploymentIo.Serialize(_def);
        }
        catch (Exception ex)
        {
            yaml = $"# Serialization error:\n# {ex.Message}";
        }

        using var dlg = new Form
        {
            Text = "YAML preview",
            ClientSize = new Size(680, 640),
            StartPosition = FormStartPosition.CenterParent,
        };
        var box = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 9.5f),
            Text = yaml,
        };
        dlg.Controls.Add(box);
        dlg.ShowDialog(this);
    }

    private void ToggleFoldersByType()
    {
        var enabling = _def.Workspace.FoldersByType != true;
        _def.Workspace.FoldersByType = enabling ? true : null;
        MarkDirty();
        _grid.Refresh();

        if (!enabling)
        {
            MessageBox.Show(this,
                "Disabled grouping items into workspace folders (workspace.folders_by_type = false).",
                "Group by type", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var byFolder = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var info in ResourceTypeRegistry.All)
        {
            var count = DictFor(info.FieldName).Count;
            if (count > 0)
            {
                byFolder[info.Folder] = byFolder.GetValueOrDefault(info.Folder) + count;
            }
        }

        var sb = new StringBuilder("On deploy, items will be grouped into Fabric workspace folders by type.\n\n");
        if (byFolder.Count == 0)
        {
            sb.Append("(No grouped resource types are defined yet — folders are created as you add them.)");
        }
        else
        {
            foreach (var (folder, count) in byFolder)
            {
                sb.Append($"• {folder}: {count} item(s)\n");
            }
        }
        sb.Append("\nSet on workspace.folders_by_type. A per-item 'folder' override still takes precedence.");
        MessageBox.Show(this, sb.ToString(), "Group by type", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ImportFromDeployed()
    {
        using var dlg = new ImportDialog(_def.Workspace.Name);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Selected.Count == 0)
        {
            return;
        }

        int added = 0, skipped = 0;
        foreach (var resource in dlg.Selected)
        {
            var dict = DictFor(resource.FieldName);
            if (dict.Contains(resource.Key))
            {
                skipped++;
                continue;
            }
            dict[resource.Key] = resource.Model;
            added++;
        }

        if (added > 0)
        {
            MarkDirty();
            RebuildTree();
        }

        var msg = $"Imported {added} resource(s)."
            + (skipped > 0 ? $"\nSkipped {skipped} already present in this deployment." : "");
        SetStatus($"Imported {added}, skipped {skipped}.");
        MessageBox.Show(this, msg, "Import", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowAbout()
    {
        MessageBox.Show(this,
            "udp.yml Editor\n\n" +
            "A standalone editor for Unified Data Platform deployment definitions.\n" +
            "Reads and writes udp.yml using the Udpi30.Core model and serializer, " +
            "so output is compatible with the udpi30 CLI and MCP tooling.",
            "About", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // -- helpers -------------------------------------------------------------

    private IDictionary DictFor(string fieldName)
    {
        var info = ResourceTypeRegistry.All.First(r => r.FieldName == fieldName);
        var prop = typeof(ResourcesConfig).GetProperty(info.PropertyName)!;
        return (IDictionary)prop.GetValue(_def.Resources)!;
    }

    private void MarkDirty()
    {
        if (!_dirty)
        {
            _dirty = true;
            UpdateTitle();
        }
    }

    private void UpdateTitle()
    {
        var name = _path is null ? "untitled" : Path.GetFileName(_path);
        Text = $"udp.yml Editor — {name}{(_dirty ? " *" : "")}";
    }

    private void SetStatus(string text) => _statusLabel.Text = text;

    private static DeploymentDefinition NewDeployment() =>
        new() { Deployment = new DeploymentMetadata { Name = "new-deployment", Version = "0.1.0" } };
}
