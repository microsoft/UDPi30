using Udpi30.Core.Providers;
using Udpi30.Ontology.SemanticModel;

namespace Udpi30.Ontology.Forms;

/// <summary>
/// Connects to a Fabric workspace, lists its semantic models, and lets the user
/// pick one to import. Discovery contacts the live Fabric API on a background
/// thread so the UI stays responsive.
/// </summary>
public sealed class LoadSemanticModelDialog : Form
{
    private readonly TextBox _workspace = new() { Width = 280, PlaceholderText = "workspace name or ID" };
    private readonly CheckBox _useBrowser = new() { Text = "Use browser sign-in", AutoSize = true };
    private readonly Button _list = new() { Text = "List models", Width = 110 };
    private readonly ListBox _models = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Button _ok = new() { Text = "Import", DialogResult = DialogResult.OK, Width = 100, Enabled = false };
    private readonly ToolStripStatusLabel _statusLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

    /// <summary>The semantic model the user chose, or null if cancelled.</summary>
    public SemanticModelRef? Selected => _models.SelectedItem as SemanticModelRef;

    /// <summary>Whether the user asked for interactive browser sign-in.</summary>
    public bool UseBrowser => _useBrowser.Checked;

    public LoadSemanticModelDialog(string? defaultWorkspace, bool useBrowser)
    {
        Text = "Load from Fabric semantic model";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 420);
        MinimumSize = new Size(460, 340);
        _workspace.Text = defaultWorkspace ?? "";
        _useBrowser.Checked = useBrowser;

        Controls.Add(BuildBody());
        Controls.Add(BuildButtons());
        Controls.Add(BuildTop());
        Controls.Add(BuildStatus());

        _list.Click += async (_, _) => await ListAsync();
        _models.SelectedIndexChanged += (_, _) => _ok.Enabled = _models.SelectedItem is SemanticModelRef;
        _models.DoubleClick += (_, _) =>
        {
            if (_models.SelectedItem is SemanticModelRef)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        };
        AcceptButton = _list;
    }

    private Control BuildTop()
    {
        var box = new GroupBox { Text = "Workspace", Dock = DockStyle.Top, Height = 96, Padding = new Padding(8) };
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
        layout.Controls.Add(new Label { Text = "Workspace:", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        layout.Controls.Add(_workspace);
        layout.Controls.Add(_list);
        layout.Controls.Add(_useBrowser);
        box.Controls.Add(layout);
        return box;
    }

    private Control BuildBody()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 4) };
        panel.Controls.Add(_models);
        return panel;
    }

    private Control BuildButtons()
    {
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8),
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(_ok);
        CancelButton = cancel;
        return buttons;
    }

    private Control BuildStatus()
    {
        var strip = new StatusStrip();
        strip.Items.Add(_statusLabel);
        return strip;
    }

    private async Task ListAsync()
    {
        var ws = _workspace.Text.Trim();
        if (ws.Length == 0)
        {
            MessageBox.Show(this, "Enter a workspace name or ID.", "Load");
            return;
        }

        _list.Enabled = false;
        _ok.Enabled = false;
        _models.Items.Clear();
        _statusLabel.Text = "Connecting to Fabric…";
        UseWaitCursor = true;

        var found = new List<SemanticModelRef>();
        string? error = null;
        var useBrowser = _useBrowser.Checked;

        await Task.Run(() =>
        {
            try
            {
                var client = FabricSession.CreateClient(useBrowser);
                var (id, name) = FabricSession.ResolveWorkspace(client, ws);
                if (id is null)
                {
                    error = $"Workspace '{ws}' not found.";
                    return;
                }
                found.AddRange(SemanticModelReader.ListSemanticModels(client, id, name ?? ws));
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
        });

        UseWaitCursor = false;
        _list.Enabled = true;
        foreach (var m in found)
        {
            _models.Items.Add(m);
        }
        _statusLabel.Text = error is not null
            ? "Error: " + error
            : found.Count == 0
                ? "No semantic models found in this workspace."
                : $"Found {found.Count} semantic model(s). Select one and click Import.";
    }
}
