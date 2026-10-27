using System.Text.Json.Nodes;
using Udpi30.Core.Generators;
using Udpi30.Core.Models;
using Udpi30.Core.Providers;

namespace Udpi30.Editor;

/// <summary>
/// "Import from deployed environment" — connects to the live Fabric workspace,
/// Microsoft Entra, and/or Azure subscription, lists what is currently deployed,
/// and lets the user pick which resources to reverse-generate into the open
/// <c>udp.yml</c>. Discovery runs through <see cref="ReverseDiscovery"/>, the same
/// engine the CLI <c>generate</c> command uses, so the imported models are
/// byte-for-byte what a hand-authored deployment would contain.
/// </summary>
public sealed class ImportDialog : Form
{
    private readonly CheckBox _useFabric = new() { Text = "Fabric workspace", AutoSize = true, Checked = true };
    private readonly TextBox _fabricWorkspace = new() { Width = 240, PlaceholderText = "workspace name or ID" };

    private readonly CheckBox _useEntra = new() { Text = "Microsoft Entra (groups & app registrations)", AutoSize = true };

    private readonly CheckBox _useAzure = new() { Text = "Azure resources", AutoSize = true };
    private readonly TextBox _subscription = new() { Width = 240, PlaceholderText = "subscription (blank = az default)" };
    private readonly TextBox _resourceGroup = new() { Width = 240, PlaceholderText = "resource group (optional)" };

    private readonly TextBox _filter = new() { Dock = DockStyle.Fill, PlaceholderText = "Filter…" };
    private readonly CheckedListBox _results = new()
    {
        Dock = DockStyle.Fill,
        CheckOnClick = true,
        IntegralHeight = false,
    };

    private readonly Button _discover = new() { Text = "Discover", Width = 110 };
    private readonly Button _import = new() { Text = "Import Selected", DialogResult = DialogResult.OK, Width = 130, Enabled = false };
    private readonly ToolStripStatusLabel _statusLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

    private List<Row> _allRows = [];
    private readonly HashSet<Row> _checked = [];

    /// <summary>Wraps a discovered resource for display in the checked list.</summary>
    private sealed record Row(DiscoveredResource Resource)
    {
        public override string ToString()
        {
            var platform = Resource.Platform == ResourcePlatform.Fabric ? "" : $"[{Resource.Platform}] ";
            return $"{platform}{Resource.FieldName} / {Resource.Key}";
        }
    }

    /// <summary>The resources the user checked for import.</summary>
    public IReadOnlyList<DiscoveredResource> Selected =>
        _results.CheckedItems.Cast<Row>().Select(r => r.Resource).ToList();

    public ImportDialog(string? defaultWorkspace = null)
    {
        Text = "Import from deployed environment";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(640, 560);
        MinimumSize = new Size(520, 440);
        _fabricWorkspace.Text = defaultWorkspace ?? "";

        Controls.Add(BuildBody());
        Controls.Add(BuildButtons());
        Controls.Add(BuildSources());
        Controls.Add(BuildStatus());

        _discover.Click += async (_, _) => await DiscoverAsync();
        _filter.TextChanged += (_, _) => ApplyFilter();
        _results.ItemCheck += (_, e) =>
        {
            if (_results.Items[e.Index] is Row row)
            {
                if (e.NewValue == CheckState.Checked)
                {
                    _checked.Add(row);
                }
                else
                {
                    _checked.Remove(row);
                }
            }
        };

        SyncEnabled();
        _useFabric.CheckedChanged += (_, _) => SyncEnabled();
        _useAzure.CheckedChanged += (_, _) => SyncEnabled();

        AcceptButton = _discover;
    }

    // -- layout --------------------------------------------------------------

    private Control BuildSources()
    {
        var box = new GroupBox { Text = "Sources", Dock = DockStyle.Top, Height = 168, Padding = new Padding(8) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        layout.Controls.Add(_useFabric, 0, 0);
        layout.Controls.Add(_fabricWorkspace, 1, 0);
        layout.Controls.Add(_useEntra, 0, 1);
        layout.SetColumnSpan(_useEntra, 2);
        layout.Controls.Add(_useAzure, 0, 2);
        layout.SetColumnSpan(_useAzure, 2);
        layout.Controls.Add(new Label { Text = "Subscription:", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(18, 4, 0, 0) }, 0, 3);
        layout.Controls.Add(_subscription, 1, 3);
        layout.Controls.Add(new Label { Text = "Resource group:", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(18, 4, 0, 0) }, 0, 4);
        layout.Controls.Add(_resourceGroup, 1, 4);

        box.Controls.Add(layout);
        return box;
    }

    private Control BuildBody()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 4) };

        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 30, ColumnCount = 4, AutoSize = false };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        var selectAll = new Button { Text = "All", Dock = DockStyle.Fill };
        var selectNone = new Button { Text = "None", Dock = DockStyle.Fill };
        selectAll.Click += (_, _) => SetAllVisible(true);
        selectNone.Click += (_, _) => SetAllVisible(false);
        top.Controls.Add(_filter, 0, 0);
        top.Controls.Add(selectAll, 1, 0);
        top.Controls.Add(selectNone, 2, 0);
        top.Controls.Add(_discover, 3, 0);

        panel.Controls.Add(_results);
        panel.Controls.Add(top);
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
        buttons.Controls.Add(_import);
        CancelButton = cancel;
        return buttons;
    }

    private Control BuildStatus()
    {
        var strip = new StatusStrip();
        strip.Items.Add(_statusLabel);
        return strip;
    }

    // -- behavior ------------------------------------------------------------

    private void SyncEnabled()
    {
        _fabricWorkspace.Enabled = _useFabric.Checked;
        _subscription.Enabled = _useAzure.Checked;
        _resourceGroup.Enabled = _useAzure.Checked;
    }

    private async Task DiscoverAsync()
    {
        if (!_useFabric.Checked && !_useEntra.Checked && !_useAzure.Checked)
        {
            MessageBox.Show(this, "Select at least one source to discover.", "Import");
            return;
        }

        var doFabric = _useFabric.Checked;
        var doEntra = _useEntra.Checked;
        var doAzure = _useAzure.Checked;
        var fabricWs = _fabricWorkspace.Text.Trim();
        var sub = _subscription.Text.Trim();
        var rg = _resourceGroup.Text.Trim();

        _discover.Enabled = false;
        _import.Enabled = false;
        _statusLabel.Text = "Discovering — this contacts the live environment…";
        UseWaitCursor = true;

        var all = new List<DiscoveredResource>();
        var errors = new List<string>();

        await Task.Run(() =>
        {
            if (doFabric && fabricWs.Length > 0)
            {
                try
                {
                    var client = new FabricClient();
                    var id = IsGuid(fabricWs)
                        ? fabricWs
                        : client.FindWorkspace(fabricWs)?["id"]?.GetValue<string>();
                    if (id is null)
                    {
                        errors.Add($"Fabric workspace '{fabricWs}' not found.");
                    }
                    else
                    {
                        all.AddRange(ReverseDiscovery.DiscoverFabric(client, id));
                    }
                }
                catch (Exception ex)
                {
                    errors.Add("Fabric: " + ex.Message);
                }
            }
            if (doEntra)
            {
                try
                {
                    all.AddRange(ReverseDiscovery.DiscoverEntra(new GraphClient()));
                }
                catch (Exception ex)
                {
                    errors.Add("Entra: " + ex.Message);
                }
            }
            if (doAzure)
            {
                try
                {
                    all.AddRange(ReverseDiscovery.DiscoverAzure(new AzureCli(),
                        sub.Length > 0 ? sub : null, rg.Length > 0 ? rg : null));
                }
                catch (Exception ex)
                {
                    errors.Add("Azure: " + ex.Message);
                }
            }
        });

        _allRows = all
            .Select(r => new Row(r))
            .OrderBy(r => r.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToList();
        _checked.Clear();
        ApplyFilter();

        UseWaitCursor = false;
        _discover.Enabled = true;
        _import.Enabled = all.Count > 0;
        var summary = $"Discovered {all.Count} resource(s).";
        if (errors.Count > 0)
        {
            summary += "  Issues: " + string.Join("  |  ", errors);
        }
        _statusLabel.Text = summary;
    }

    private void ApplyFilter()
    {
        var f = _filter.Text.Trim();
        _results.BeginUpdate();
        _results.Items.Clear();
        foreach (var row in _allRows)
        {
            if (f.Length == 0 || row.ToString().Contains(f, StringComparison.OrdinalIgnoreCase))
            {
                _results.Items.Add(row, _checked.Contains(row));
            }
        }
        _results.EndUpdate();
    }

    private void SetAllVisible(bool on)
    {
        for (var i = 0; i < _results.Items.Count; i++)
        {
            _results.SetItemChecked(i, on);
        }
    }

    private static bool IsGuid(string s) => s.Length == 36 && s.Count(c => c == '-') == 4;
}
