namespace PalSaveEditor.WinForms;

internal sealed class NumericPromptDialog : Form
{
    private readonly NumericUpDown _value = new();

    public NumericPromptDialog(string title, string label, decimal initialValue, decimal maximum = ushort.MaxValue)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ClientSize = new(360, 120);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(12), ColumnCount = 2, RowCount = 2 };
        root.ColumnStyles.Add(new(SizeType.AutoSize));
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        root.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        _value.Maximum = maximum;
        _value.Value = Math.Max(0, Math.Min(maximum, initialValue));
        _value.Dock = DockStyle.Fill;
        _value.ThousandsSeparator = true;
        root.Controls.Add(_value, 1, 0);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
        var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        root.SetColumnSpan(buttons, 2);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public decimal SelectedValue => _value.Value;
}
