using Udpi30.Ontology.Forms;
using Udpi30.Ontology.Models;
using Udpi30.Ontology.SemanticModel;

namespace Udpi30.Ontology;

/// <summary>
/// Main window: a tree of the ontology on the left (entities → properties,
/// relationships), a property grid on the right for editing logical names and
/// descriptions, and commands to load from a semantic model, save/open a local
/// file, and publish to Fabric.
/// </summary>
public sealed class MainForm : Form
{
    private OntologyDocument? _doc;
    private string? _path;
    private bool _dirty;
    private bool _useBrowser;
    private bool _busy;

    private readonly TreeView _tree = new()
    {
        Dock = DockStyle.Fill,
        HideSelection = false,
        PathSeparator = "/",
    };

    private readonly PropertyGrid _grid = new()
    {
        Dock = DockStyle.Fill,
        PropertySort = PropertySort.Categorized,
    };

    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

    public MainForm()
    {
        Text = "UDPI30 Ontology Builder";
        ClientSize = new Size(1040, 700);
        StartPosition = FormStartPosition.CenterScreen;

        BuildBody();
        BuildStatus();
        BuildToolbar();
        BuildMenu();

        _tree.AfterSelect += (_, e) => _grid.SelectedObject = e.Node?.Tag as object;
        _grid.PropertyValueChanged += (_, _) =>
        {
            MarkDirty();
            RefreshSelectedNodeText();
        };

        FormClosing += (_, e) =>
        {
            if (_busy)
            {
                e.Cancel = true;
                return;
            }
            if (!ConfirmDiscardIfDirty())
            {
                e.Cancel = true;
            }
        };

        RebuildTree();
        UpdateTitle();
        SetStatus("Load a semantic model to begin, or open an existing .ontology.json file.");
    }

    /// <summary>Open a file specified on the command line, after the form is constructed.</summary>
    public void OpenOnLoad(string path) => LoadFile(path);

    // -- UI construction -----------------------------------------------------

    private void BuildBody()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 360,
            FixedPanel = FixedPanel.Panel1,
        };
        split.Panel1.Controls.Add(_tree);
        split.Panel2.Controls.Add(_grid);
        Controls.Add(split);
    }

    private void BuildStatus()
    {
        _status.Items.Add(_statusLabel);
        Controls.Add(_status);
    }

    private void BuildToolbar()
    {
        var bar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
        bar.Items.Add(new ToolStripButton("Load model…", null, (_, _) => LoadFromSemanticModel()));
        bar.Items.Add(new ToolStripSeparator());
        bar.Items.Add(new ToolStripButton("Open", null, (_, _) => OpenFile()));
        bar.Items.Add(new ToolStripButton("Save", null, (_, _) => SaveFile()));
        bar.Items.Add(new ToolStripSeparator());
        bar.Items.Add(new ToolStripButton("Publish to Fabric…", null, (_, _) => PublishToFabric()));
        Controls.Add(bar);
    }

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

        var source = new ToolStripMenuItem("&Source");
        source.DropDownItems.Add(new ToolStripMenuItem("&Load from semantic model…", null, (_, _) => LoadFromSemanticModel()) { ShortcutKeys = Keys.Control | Keys.L });
        source.DropDownItems.Add(new ToolStripMenuItem("&Regenerate names && descriptions", null, (_, _) => RegenerateMetadata()));

        var publish = new ToolStripMenuItem("&Publish");
        publish.DropDownItems.Add(new ToolStripMenuItem("&Publish to Fabric…", null, (_, _) => PublishToFabric()) { ShortcutKeys = Keys.Control | Keys.P });

        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add(new ToolStripMenuItem("&About", null, (_, _) => ShowAbout()));

        menu.Items.AddRange([file, source, publish, help]);
        MainMenuStrip = menu;
        Controls.Add(menu);
    }

    // -- tree ----------------------------------------------------------------

    private void RebuildTree()
    {
        _tree.BeginUpdate();
        _tree.Nodes.Clear();

        if (_doc is null)
        {
            _tree.EndUpdate();
            _grid.SelectedObject = null;
            return;
        }

        var root = new TreeNode(_doc.ToString()) { Tag = _doc };

        var entities = new TreeNode($"Entities ({_doc.Entities.Count})");
        foreach (var entity in _doc.Entities)
        {
            var node = new TreeNode(entity.ToString()) { Tag = entity };
            foreach (var prop in entity.AllProperties)
            {
                node.Nodes.Add(new TreeNode(prop.ToString()) { Tag = prop });
            }
            entities.Nodes.Add(node);
        }

        var relationships = new TreeNode($"Relationships ({_doc.Relationships.Count})");
        foreach (var rel in _doc.Relationships)
        {
            relationships.Nodes.Add(new TreeNode(rel.ToString()) { Tag = rel });
        }

        root.Nodes.Add(entities);
        root.Nodes.Add(relationships);
        _tree.Nodes.Add(root);
        root.Expand();
        entities.Expand();
        relationships.Expand();
        _tree.EndUpdate();
        _tree.SelectedNode = root;
    }

    private void RefreshSelectedNodeText()
    {
        var node = _tree.SelectedNode;
        if (node?.Tag is null)
        {
            return;
        }
        node.Text = node.Tag.ToString() ?? node.Text;
        if (node.Tag is OntologyDocument)
        {
            UpdateTitle();
        }
    }

    // -- source --------------------------------------------------------------

    private async void LoadFromSemanticModel()
    {
        if (_busy)
        {
            return;
        }
        if (!ConfirmDiscardIfDirty())
        {
            return;
        }

        using var dialog = new LoadSemanticModelDialog(_doc?.SourceWorkspaceName, _useBrowser);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Selected is not { } model)
        {
            return;
        }

        _useBrowser = dialog.UseBrowser;
        SetStatus($"Reading semantic model '{model.Name}'…");
        BeginOperation();

        OntologyDocument? built = null;
        string? error = null;
        var useBrowser = _useBrowser;

        try
        {
            await Task.Run(() =>
            {
                try
                {
                    var client = FabricSession.CreateClient(useBrowser);
                    var schema = SemanticModelReader.ReadSchema(client, model.WorkspaceId, model.ItemId);
                    built = OntologyBuilder.Build(schema, model);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            });
        }
        finally
        {
            EndOperation();
        }

        if (error is not null || built is null)
        {
            MessageBox.Show(this, "Failed to read the semantic model:\n\n" + error, "Load",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Load failed.");
            return;
        }

        _doc = built;
        _path = null;
        RebuildTree();
        MarkDirty();
        UpdateTitle();
        SetStatus($"Built ontology from '{model.Name}': {built.Entities.Count} entit(ies), {built.Relationships.Count} relationship(s).");
    }

    private void RegenerateMetadata()
    {
        if (_doc is null)
        {
            return;
        }
        if (MessageBox.Show(this,
                "Regenerate logical names and descriptions for every entity, property, and relationship? " +
                "This overwrites any manual edits to those fields.",
                "Regenerate", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
        {
            return;
        }

        foreach (var entity in _doc.Entities)
        {
            entity.LogicalName = Naming.Humanize(string.IsNullOrEmpty(entity.SourceTable) ? entity.Name : entity.SourceTable);
            entity.Description = Naming.DescribeEntity(entity.LogicalName, entity.SourceTable, entity.Properties.Count - 1);
            foreach (var prop in entity.AllProperties)
            {
                var logical = string.IsNullOrEmpty(prop.SourceColumn)
                    ? Naming.Humanize(prop.Name)
                    : Naming.Humanize(prop.SourceColumn);
                prop.LogicalName = logical;
                prop.Description = Naming.DescribeProperty(logical, prop.ValueType, prop.SourceColumn);
            }
        }
        foreach (var rel in _doc.Relationships)
        {
            var src = _doc.Entities.FirstOrDefault(e => e.Id == rel.SourceEntityId)?.LogicalName ?? "source";
            var tgt = _doc.Entities.FirstOrDefault(e => e.Id == rel.TargetEntityId)?.LogicalName ?? "target";
            rel.LogicalName = $"{src} to {tgt}";
            rel.Description = Naming.DescribeRelationship(src, tgt);
        }
        _doc.Description = Naming.DescribeOntology(_doc.SourceModelName, _doc.Entities.Count, _doc.Relationships.Count);

        RebuildTree();
        MarkDirty();
        _grid.Refresh();
        SetStatus("Regenerated logical names and descriptions.");
    }

    // -- local file ----------------------------------------------------------

    private void NewFile()
    {
        if (!ConfirmDiscardIfDirty())
        {
            return;
        }
        _doc = null;
        _path = null;
        _dirty = false;
        RebuildTree();
        UpdateTitle();
        SetStatus("Cleared. Load a semantic model to begin.");
    }

    private void OpenFile()
    {
        if (!ConfirmDiscardIfDirty())
        {
            return;
        }
        using var dialog = new OpenFileDialog
        {
            Filter = $"Ontology files (*{OntologyIo.FileExtension})|*{OntologyIo.FileExtension}|JSON files (*.json)|*.json|All files (*.*)|*.*",
            Title = "Open ontology",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            LoadFile(dialog.FileName);
        }
    }

    private void LoadFile(string path)
    {
        try
        {
            _doc = OntologyIo.Load(path);
            _path = path;
            _dirty = false;
            RebuildTree();
            UpdateTitle();
            SetStatus($"Opened {Path.GetFileName(path)}.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not open the file:\n\n" + ex.Message, "Open",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool SaveFile()
    {
        if (_doc is null)
        {
            return false;
        }
        if (_path is null)
        {
            return SaveFileAs();
        }
        try
        {
            OntologyIo.Save(_doc, _path);
            _dirty = false;
            UpdateTitle();
            SetStatus($"Saved {Path.GetFileName(_path)}.");
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not save:\n\n" + ex.Message, "Save",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private bool SaveFileAs()
    {
        if (_doc is null)
        {
            return false;
        }
        using var dialog = new SaveFileDialog
        {
            Filter = $"Ontology files (*{OntologyIo.FileExtension})|*{OntologyIo.FileExtension}|All files (*.*)|*.*",
            Title = "Save ontology",
            FileName = _doc.Name + OntologyIo.FileExtension,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return false;
        }
        _path = dialog.FileName;
        return SaveFile();
    }

    // -- publish -------------------------------------------------------------

    private async void PublishToFabric()
    {
        if (_busy)
        {
            return;
        }
        if (_doc is null)
        {
            MessageBox.Show(this, "Load or open an ontology first.", "Publish");
            return;
        }
        if (_doc.Entities.Count == 0)
        {
            MessageBox.Show(this, "The ontology has no entity types to publish.", "Publish");
            return;
        }

        using var dialog = new PublishDialog(
            _doc.SourceWorkspaceName, _doc.Name, _useBrowser, _doc.Entities.Count, _doc.Relationships.Count);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        if (dialog.Workspace.Length == 0 || dialog.ItemName.Length == 0)
        {
            MessageBox.Show(this, "Target workspace and item name are required.", "Publish");
            return;
        }

        _useBrowser = dialog.UseBrowser;
        if (!string.Equals(_doc.Name, dialog.ItemName, StringComparison.Ordinal))
        {
            _doc.Name = dialog.ItemName;
            MarkDirty();
            RefreshRootNode();
        }

        SetStatus($"Publishing '{dialog.ItemName}' to '{dialog.Workspace}'…");
    BeginOperation();

        PublishResult? result = null;
        string? error = null;
        var useBrowser = _useBrowser;
        var workspace = dialog.Workspace;
        var doc = _doc;

        try
        {
            await Task.Run(() =>
            {
                try
                {
                    var client = FabricSession.CreateClient(useBrowser);
                    var (id, _) = FabricSession.ResolveWorkspace(client, workspace);
                    if (id is null)
                    {
                        error = $"Workspace '{workspace}' not found.";
                        return;
                    }
                    result = OntologyPublisher.Publish(client, id, doc);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            });
        }
        finally
        {
            EndOperation();
        }

        if (error is not null || result is null)
        {
            MessageBox.Show(this, "Publish failed:\n\n" + error, "Publish",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Publish failed.");
            return;
        }

        var idText = result.ItemId is null ? "" : $"\n\nItem ID: {result.ItemId}";
        MessageBox.Show(this, $"Published '{result.DisplayName}' to the workspace.{idText}", "Publish",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        SetStatus($"Published '{result.DisplayName}'.");
    }

    // -- helpers -------------------------------------------------------------

    private void BeginOperation()
    {
        _busy = true;
        Enabled = false;
        UseWaitCursor = true;
    }

    private void EndOperation()
    {
        UseWaitCursor = false;
        Enabled = true;
        _busy = false;
    }

    private void RefreshRootNode()
    {
        if (_tree.Nodes.Count > 0 && _tree.Nodes[0].Tag is OntologyDocument)
        {
            _tree.Nodes[0].Text = _doc!.ToString();
        }
        UpdateTitle();
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
        var name = _path is not null ? Path.GetFileName(_path) : _doc?.Name ?? "(no ontology)";
        Text = $"UDPI30 Ontology Builder — {name}{(_dirty ? " *" : "")}";
    }

    private void SetStatus(string text) => _statusLabel.Text = text;

    private bool ConfirmDiscardIfDirty()
    {
        if (!_dirty || _doc is null)
        {
            return true;
        }
        var choice = MessageBox.Show(this, "Save changes to the current ontology?", "Unsaved changes",
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return choice switch
        {
            DialogResult.Yes => SaveFile(),
            DialogResult.No => true,
            _ => false,
        };
    }

    private void ShowAbout() =>
        MessageBox.Show(this,
            "UDPI30 Ontology Builder\n\n" +
            "Reads a Microsoft Fabric semantic model, generates a digital-twin-builder " +
            "ontology (entity types, properties, relationship types) with auto-generated " +
            "logical names and descriptions, saves it locally as .ontology.json, and " +
            "publishes it as a Fabric Ontology item.\n\n" +
            "Part of the UDPI30 toolset.",
            "About", MessageBoxButtons.OK, MessageBoxIcon.Information);
}
