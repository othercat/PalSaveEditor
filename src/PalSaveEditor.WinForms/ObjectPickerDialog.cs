using PalSaveEditor.Core;

namespace PalSaveEditor.WinForms;

internal sealed class ObjectPickerDialog : Form
{
    private readonly PalResourceCatalog? _catalog;
    private readonly TextBox _search = new();
    private readonly ListBox _results = new();
    private readonly NumericUpDown _id = new();

    public ObjectPickerDialog(PalResourceCatalog? catalog, string title, ushort initialId = 0)
    {
        _catalog = catalog;
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new(520, 430);
        MinimumSize = new(480, 380);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new(12),
            RowCount = 4,
            ColumnCount = 2,
        };
        root.ColumnStyles.Add(new(SizeType.AutoSize));
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.AutoSize));

        root.Controls.Add(new Label { Text = "搜索名称或编号：", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        _search.Dock = DockStyle.Fill;
        root.Controls.Add(_search, 1, 0);

        _results.Dock = DockStyle.Fill;
        _results.IntegralHeight = false;
        root.SetColumnSpan(_results, 2);
        root.Controls.Add(_results, 0, 1);

        root.Controls.Add(new Label { Text = "对象编号：", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        _id.Maximum = ushort.MaxValue;
        _id.Value = initialId;
        _id.Width = 140;
        root.Controls.Add(_id, 1, 2);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
        };
        var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        root.SetColumnSpan(buttons, 2);
        root.Controls.Add(buttons, 0, 3);

        Controls.Add(root);
        AcceptButton = ok;
        CancelButton = cancel;

        _search.TextChanged += (_, _) => RefreshResults();
        _results.SelectedIndexChanged += (_, _) =>
        {
            if (_results.SelectedItem is ObjectChoice choice)
            {
                _id.Value = choice.Id;
            }
        };
        _results.DoubleClick += (_, _) =>
        {
            if (_results.SelectedItem is not null)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        };

        RefreshResults();
    }

    public ushort SelectedId => decimal.ToUInt16(_id.Value);

    private void RefreshResults()
    {
        _results.BeginUpdate();
        try
        {
            _results.Items.Clear();
            if (_catalog is null)
            {
                _results.Items.Add("未加载 WORD.DAT；仍可直接输入对象编号。");
                return;
            }

            foreach (var (id, name) in _catalog.SearchObjects(_search.Text, 300))
            {
                _results.Items.Add(new ObjectChoice(id, name));
            }
        }
        finally
        {
            _results.EndUpdate();
        }
    }

    private sealed record ObjectChoice(ushort Id, string Name)
    {
        public override string ToString() => $"{Id,4}  {Name}";
    }
}
