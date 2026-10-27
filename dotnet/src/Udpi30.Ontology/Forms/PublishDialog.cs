namespace Udpi30.Ontology.Forms;

/// <summary>
/// Collects the target workspace and item name before publishing an ontology to
/// Fabric. The actual create call (an outward-facing action) is performed by the
/// caller after this dialog returns OK.
/// </summary>
public sealed class PublishDialog : Form
{
    private readonly TextBox _workspace = new() { Width = 300, PlaceholderText = "workspace name or ID" };
    private readonly TextBox _itemName = new() { Width = 300 };
    private readonly CheckBox _useBrowser = new() { Text = "Use browser sign-in", AutoSize = true };

    public string Workspace => _workspace.Text.Trim();
    public string ItemName => _itemName.Text.Trim();
    public bool UseBrowser => _useBrowser.Checked;

    public PublishDialog(string defaultWorkspace, string defaultItemName, bool useBrowser, int entityCount, int relationshipCount)
    {
        Text = "Publish ontology to Fabric";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(460, 250);
        _workspace.Text = defaultWorkspace;
        _itemName.Text = defaultItemName;
        _useBrowser.Checked = useBrowser;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(12),
            AutoSize = false,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label
        {
            Text = $"This creates a new Ontology item ({entityCount} entity type(s), " +
                   $"{relationshipCount} relationship type(s)) in the target workspace.",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10),
        }, 0, 0);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 0)!, 2);

        layout.Controls.Add(new Label { Text = "Target workspace:", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 0, 0) }, 0, 1);
        layout.Controls.Add(_workspace, 1, 1);
        layout.Controls.Add(new Label { Text = "Item name:", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 0, 0) }, 0, 2);
        layout.Controls.Add(_itemName, 1, 2);
        layout.Controls.Add(_useBrowser, 1, 3);

        var ok = new Button { Text = "Publish", DialogResult = DialogResult.OK, Width = 100 };
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
}
