using PalSaveEditor.Core;

namespace PalSaveEditor.WinForms;

internal sealed class SkillPickerDialog : Form
{
    private readonly IReadOnlyList<PalRegisteredSkill> _skills;
    private readonly TextBox _search = new();
    private readonly ListView _results = new();
    private readonly NumericUpDown _id = new();
    private readonly Label _status = new() { AutoSize = true };

    public SkillPickerDialog(PalSkillRegistryResolution resolution)
    {
        _skills = resolution.Skills;
        Text = "添加法术（全部扩展技能）";
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new(760, 520);
        MinimumSize = new(620, 420);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new(12),
            RowCount = 5,
            ColumnCount = 2,
        };
        root.ColumnStyles.Add(new(SizeType.AutoSize));
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.AutoSize));

        var hint = new Label
        {
            Text = "这里列出当前资源可写入的全部注册技能，不读取也不受配置工具候选池勾选限制。",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
        };
        root.SetColumnSpan(hint, 2);
        root.Controls.Add(hint, 0, 0);

        root.Controls.Add(new Label
        {
            Text = "搜索技能、技能池或编号：",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
        }, 0, 1);
        _search.Dock = DockStyle.Fill;
        root.Controls.Add(_search, 1, 1);

        _results.Dock = DockStyle.Fill;
        _results.View = View.Details;
        _results.FullRowSelect = true;
        _results.HideSelection = false;
        _results.MultiSelect = false;
        _results.Columns.Add("技能池", 260);
        _results.Columns.Add("技能", 260);
        _results.Columns.Add("对象编号", 100);
        root.SetColumnSpan(_results, 2);
        root.Controls.Add(_results, 0, 2);

        var selectedRow = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = false,
        };
        selectedRow.Controls.Add(new Label
        {
            Text = "写入对象编号：",
            AutoSize = true,
            Margin = new Padding(0, 6, 6, 0),
        });
        _id.Maximum = ushort.MaxValue;
        _id.Width = 120;
        selectedRow.Controls.Add(_id);
        _status.Margin = new Padding(12, 6, 0, 0);
        selectedRow.Controls.Add(_status);
        root.SetColumnSpan(selectedRow, 2);
        root.Controls.Add(selectedRow, 0, 3);

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
        root.Controls.Add(buttons, 0, 4);

        Controls.Add(root);
        AcceptButton = ok;
        CancelButton = cancel;

        _search.TextChanged += (_, _) => RefreshResults();
        _results.SelectedIndexChanged += (_, _) => ApplySelection();
        _results.DoubleClick += (_, _) =>
        {
            if (_results.SelectedItems.Count != 0)
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
        string query = _search.Text.Trim();
        _results.BeginUpdate();
        try
        {
            _results.Items.Clear();
            foreach (PalRegisteredSkill skill in _skills.Where(skill =>
                         query.Length == 0 ||
                         skill.DisplayName.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                         skill.SkillSetDisplayName.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                         skill.LogicalId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                         skill.ObjectId.ToString().IndexOf(query, StringComparison.Ordinal) >= 0))
            {
                string groupName = skill.SkillSetDisplayName
                    .Replace("（随机候选）", string.Empty);
                var item = new ListViewItem(groupName)
                {
                    Tag = skill,
                    ToolTipText = skill.LogicalId,
                };
                item.SubItems.Add(skill.DisplayName + (skill.Deprecated ? "（废弃）" : string.Empty));
                item.SubItems.Add($"{skill.ObjectId} / 0x{skill.ObjectId:X4}");
                _results.Items.Add(item);
            }
        }
        finally
        {
            _results.EndUpdate();
        }

        _status.Text = $"当前显示 {_results.Items.Count} / {_skills.Count} 个注册技能";
        if (_results.Items.Count != 0)
        {
            _results.Items[0].Selected = true;
        }
    }

    private void ApplySelection()
    {
        if (_results.SelectedItems.Count == 0 ||
            _results.SelectedItems[0].Tag is not PalRegisteredSkill skill)
        {
            return;
        }
        _id.Value = skill.ObjectId;
    }
}
