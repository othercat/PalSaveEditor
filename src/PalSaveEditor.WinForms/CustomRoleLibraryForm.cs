using PalSaveEditor.Core;

namespace PalSaveEditor.WinForms;

internal sealed class CustomRoleLibraryForm : Form
{
    private readonly string _gameDirectory;
    private readonly PalSaveDocument? _document;
    private PalCustomRoleLibrary _library;
    private readonly TextBox _libraryId = new() { Dock = DockStyle.Fill };
    private readonly TextBox _libraryVersion = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _nativeNames = CreateGrid(readOnly: false);
    private readonly ListBox _roles = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly TextBox _displayName = new() { Dock = DockStyle.Fill };
    private readonly Dictionary<string, NumericUpDown> _general = new();
    private readonly PropertyGrid _initial = new() { Dock = DockStyle.Fill, HelpVisible = true, ToolbarVisible = false };
    private readonly PropertyGrid _growth = new() { Dock = DockStyle.Fill, HelpVisible = true, ToolbarVisible = false };
    private readonly DataGridView _learned = CreateGrid(readOnly: false);
    private int _selectedIndex = -1;
    private bool _loading;

    public CustomRoleLibraryForm(string gameDirectory, PalSaveDocument? document)
    {
        _gameDirectory = Path.GetFullPath(gameDirectory);
        _document = document;
        if (!PalCustomRoleLibraryStore.TryLoad(
                _gameDirectory, out _library, out string? error,
                document?.Catalog?.RuntimeObjectRecordCount) &&
            !string.IsNullOrWhiteSpace(error))
        {
            throw new InvalidDataException(error);
        }

        Text = "自定义主角库";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new(980, 680);
        ClientSize = new(1_160, 780);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new("Microsoft YaHei UI", 9F);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(10), RowCount = 3, ColumnCount = 1 };
        root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize));

        var identity = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 5 };
        identity.ColumnStyles.Add(new(SizeType.AutoSize));
        identity.ColumnStyles.Add(new(SizeType.Percent, 55));
        identity.ColumnStyles.Add(new(SizeType.AutoSize));
        identity.ColumnStyles.Add(new(SizeType.Percent, 45));
        identity.ColumnStyles.Add(new(SizeType.AutoSize));
        identity.Controls.Add(new Label { Text = "角色库 ID：", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        identity.Controls.Add(_libraryId, 1, 0);
        identity.Controls.Add(new Label { Text = "版本：", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 0);
        identity.Controls.Add(_libraryVersion, 3, 0);
        identity.Controls.Add(new Label { Text = "角色 0–5 原生；6–15 自定义", AutoSize = true, ForeColor = Color.DarkRed, Anchor = AnchorStyles.Left }, 4, 0);
        root.Controls.Add(identity, 0, 0);

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 330, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.Controls.Add(BuildLeftPanel());
        split.Panel2.Controls.Add(BuildRolePanel());
        root.Controls.Add(split, 0, 1);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
        var close = new Button { Text = "关闭", DialogResult = DialogResult.Cancel, AutoSize = true };
        var save = new Button { Text = "保存角色库", AutoSize = true };
        save.Click += (_, _) => SaveLibrary();
        buttons.Controls.Add(close);
        buttons.Controls.Add(save);
        buttons.Controls.Add(new Label
        {
            Text = $"固定文件：{PalCustomRoleLibraryStore.GetPath(_gameDirectory)}",
            AutoSize = true,
            Margin = new Padding(4, 9, 18, 0),
        });
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
        CancelButton = close;

        PopulateLibrary();
    }

    private Control BuildLeftPanel()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        root.RowStyles.Add(new(SizeType.Percent, 42));
        root.RowStyles.Add(new(SizeType.Percent, 58));

        var native = new GroupBox { Text = "原生角色姓名覆盖（留空则保持资源姓名）", Dock = DockStyle.Fill, Padding = new(8) };
        _nativeNames.Columns.Add("role", "角色 ID");
        _nativeNames.Columns.Add("name", "显示姓名");
        _nativeNames.Columns[0].ReadOnly = true;
        _nativeNames.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _nativeNames.AllowUserToAddRows = false;
        _nativeNames.AllowUserToDeleteRows = false;
        native.Controls.Add(_nativeNames);
        root.Controls.Add(native, 0, 0);

        var custom = new GroupBox { Text = "独立自定义角色", Dock = DockStyle.Fill, Padding = new(8) };
        var customRoot = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        customRoot.RowStyles.Add(new(SizeType.Percent, 100));
        customRoot.RowStyles.Add(new(SizeType.AutoSize));
        customRoot.Controls.Add(_roles, 0, 0);
        var roleButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        roleButtons.Controls.Add(CreateButton("添加新角色", (_, _) => AddRole()));
        roleButtons.Controls.Add(CreateButton("移除末尾角色", (_, _) => RemoveLastRole()));
        customRoot.Controls.Add(roleButtons, 0, 1);
        custom.Controls.Add(customRoot);
        root.Controls.Add(custom, 0, 1);
        _roles.SelectedIndexChanged += (_, _) => SelectRole(_roles.SelectedIndex);
        return root;
    }

    private Control BuildRolePanel()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.Percent, 100));

        var general = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, Padding = new(4) };
        AddGeneralRow(general, 0, "姓名", _displayName, "地图 MGO", GeneralNumeric("map_sprite"));
        AddGeneralRow(general, 1, "战斗形象", GeneralNumeric("battle_sprite"), "头像", GeneralNumeric("avatar"));
        AddGeneralRow(general, 2, "行走帧上界", GeneralNumeric("walk_frames"), "合体法术", GeneralNumeric("cooperative_magic"));
        AddGeneralRow(general, 3, "普攻范围", GeneralNumeric("attack_all"), "角色 ID", GeneralNumeric("role_id"));
        _general["role_id"].Enabled = false;
        root.Controls.Add(general, 0, 0);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(new TabPage("初始属性（含隐藏属性）") { Controls = { _initial } });
        tabs.TabPages.Add(new TabPage("每级成长（含隐藏属性）") { Controls = { _growth } });
        _learned.Columns.Add("level", "学会等级");
        _learned.Columns.Add("object", "法术对象 ID");
        _learned.Columns.Add("name", "当前资料名称（只读）");
        _learned.Columns[2].ReadOnly = true;
        _learned.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        var learnedPage = new TabPage("等级领悟表（最多 32 条）");
        var learnedRoot = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new(6) };
        learnedRoot.RowStyles.Add(new(SizeType.Percent, 100));
        learnedRoot.RowStyles.Add(new(SizeType.AutoSize));
        learnedRoot.Controls.Add(_learned, 0, 0);
        var learnedButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        learnedButtons.Controls.Add(CreateButton("新增一行", (_, _) => AddLearnedRow()));
        learnedButtons.Controls.Add(CreateButton("删除选中行", (_, _) => DeleteLearnedRow()));
        learnedRoot.Controls.Add(learnedButtons, 0, 1);
        learnedPage.Controls.Add(learnedRoot);
        tabs.TabPages.Add(learnedPage);
        root.Controls.Add(tabs, 0, 1);
        return root;
    }

    private void PopulateLibrary()
    {
        _loading = true;
        try
        {
            _libraryId.Text = _library.LibraryId;
            _libraryVersion.Text = _library.LibraryVersion;
            _nativeNames.Rows.Clear();
            for (int role = 0; role < PalCustomRoleLibrary.NativeRoleCount; role++)
            {
                _library.NativeRoleNames.TryGetValue(role, out string? name);
                _nativeNames.Rows.Add(role, name ?? string.Empty);
            }
            RefreshRoleList();
            if (_roles.Items.Count != 0) _roles.SelectedIndex = 0;
            else SelectRole(-1);
        }
        finally
        {
            _loading = false;
        }
        SelectRole(_roles.Items.Count == 0 ? -1 : 0);
    }

    private void RefreshRoleList(int? select = null)
    {
        int target = select ?? _roles.SelectedIndex;
        _roles.Items.Clear();
        foreach (PalCustomRoleDefinition role in _library.CustomRoles)
            _roles.Items.Add($"{role.RoleId} - {role.DisplayName}");
        if (_roles.Items.Count != 0)
            _roles.SelectedIndex = Math.Max(0, Math.Min(_roles.Items.Count - 1, target));
    }

    private void SelectRole(int index)
    {
        if (_loading) return;
        CommitSelected();
        _selectedIndex = index;
        bool enabled = index >= 0 && index < _library.CustomRoles.Count;
        _displayName.Enabled = enabled;
        foreach (NumericUpDown numeric in _general.Values) numeric.Enabled = enabled && numeric != _general["role_id"];
        _initial.Enabled = enabled;
        _growth.Enabled = enabled;
        _learned.Enabled = enabled;
        if (!enabled)
        {
            _displayName.Clear();
            _initial.SelectedObject = null;
            _growth.SelectedObject = null;
            _learned.Rows.Clear();
            return;
        }
        _loading = true;
        try
        {
            PalCustomRoleDefinition role = _library.CustomRoles[index];
            _displayName.Text = role.DisplayName;
            _general["role_id"].Value = role.RoleId;
            _general["map_sprite"].Value = role.MapSprite;
            _general["battle_sprite"].Value = role.BattleSprite;
            _general["avatar"].Value = role.Avatar;
            _general["walk_frames"].Value = role.WalkFrames;
            _general["cooperative_magic"].Value = role.CooperativeMagic;
            _general["attack_all"].Value = role.AttackAll;
            _initial.SelectedObject = role.InitialState;
            _growth.SelectedObject = role.GrowthPerLevel;
            _learned.Rows.Clear();
            foreach (PalCustomRoleLearnedMagic magic in role.LearnedMagics)
                _learned.Rows.Add(magic.Level, magic.ObjectId, ResolveObjectName(magic.ObjectId));
        }
        finally
        {
            _loading = false;
        }
    }

    private void CommitSelected()
    {
        if (_loading || _selectedIndex < 0 || _selectedIndex >= _library.CustomRoles.Count) return;
        PalCustomRoleDefinition role = _library.CustomRoles[_selectedIndex];
        role.DisplayName = _displayName.Text.Trim();
        role.MapSprite = Value("map_sprite");
        role.BattleSprite = Value("battle_sprite");
        role.Avatar = Value("avatar");
        role.WalkFrames = Value("walk_frames");
        role.CooperativeMagic = Value("cooperative_magic");
        role.AttackAll = Value("attack_all");
        role.LearnedMagics.Clear();
        foreach (DataGridViewRow row in _learned.Rows)
        {
            if (row.IsNewRow) continue;
            if (!ushort.TryParse(Convert.ToString(row.Cells[0].Value), out ushort level) ||
                !ushort.TryParse(Convert.ToString(row.Cells[1].Value), out ushort objectId))
                throw new InvalidDataException("等级领悟表必须填写 1–99 级和非零法术对象 ID。");
            role.LearnedMagics.Add(new(level, objectId));
        }
        role.LearnedMagics.Sort((left, right) =>
            left.Level != right.Level ? left.Level.CompareTo(right.Level) : left.ObjectId.CompareTo(right.ObjectId));
    }

    private void AddRole()
    {
        RunUiAction(() =>
        {
            CommitSelected();
            if (_library.CustomRoles.Count >= PalCustomRoleLibrary.MaximumCustomRoleCount)
                throw new InvalidOperationException("v1 最多可添加 10 个自定义角色（角色 ID 6–15）。");
            int roleId = PalCustomRoleLibrary.NativeRoleCount + _library.CustomRoles.Count;
            var role = CreateTemplate(roleId);
            _library.CustomRoles.Add(role);
            _selectedIndex = -1;
            RefreshRoleList(_library.CustomRoles.Count - 1);
        });
    }

    private PalCustomRoleDefinition CreateTemplate(int roleId)
    {
        var role = new PalCustomRoleDefinition
        {
            RoleId = roleId,
            DisplayName = $"新主角{roleId - 5}",
            MapSprite = 12,
            BattleSprite = 5,
            Avatar = 5,
            WalkFrames = 3,
            CooperativeMagic = 0x0182,
            InitialState = new PalCustomRoleStats
            {
                Level = 1,
                MaxHp = 160,
                MaxMp = 80,
                Attack = 35,
                MagicPower = 25,
                Defense = 30,
                Dexterity = 25,
                FleeRate = 20,
                PoisonResistance = 10,
            },
            GrowthPerLevel = new PalCustomRoleGrowth
            {
                MaxHp = 12, MaxMp = 8, Attack = 4, MagicPower = 3,
                Defense = 3, Dexterity = 3, FleeRate = 2,
                PoisonResistance = 1,
            },
        };
        if (_document is not null)
        {
            int source = 5;
            role.BattleSprite = _document.GetRoleField(source, RoleField.BattleSprite);
            role.Avatar = _document.GetRoleField(source, RoleField.Avatar);
            role.CooperativeMagic = _document.GetRoleField(source, RoleField.CooperativeMagic);
            role.AttackAll = _document.GetRoleField(source, RoleField.AttackAll);
            role.InitialState = new PalCustomRoleStats
            {
                Level = _document.GetRoleField(source, RoleField.Level),
                MaxHp = checked((short)Math.Min(short.MaxValue, _document.GetRoleField(source, RoleField.MaxHp))),
                MaxMp = checked((short)Math.Min(short.MaxValue, _document.GetRoleField(source, RoleField.MaxMp))),
                Attack = checked((short)Math.Min(short.MaxValue, _document.GetRoleField(source, RoleField.Attack))),
                MagicPower = checked((short)Math.Min(short.MaxValue, _document.GetRoleField(source, RoleField.MagicPower))),
                Defense = checked((short)Math.Min(short.MaxValue, _document.GetRoleField(source, RoleField.Defense))),
                Dexterity = checked((short)Math.Min(short.MaxValue, _document.GetRoleField(source, RoleField.Dexterity))),
                FleeRate = checked((short)Math.Min(short.MaxValue, _document.GetRoleField(source, RoleField.FleeRate))),
                PoisonResistance = _document.GetRoleSignedField(source, RoleField.PoisonResistance),
                WindResistance = _document.GetRoleSignedField(source, RoleField.WindResistance),
                ThunderResistance = _document.GetRoleSignedField(source, RoleField.ThunderResistance),
                WaterResistance = _document.GetRoleSignedField(source, RoleField.WaterResistance),
                FireResistance = _document.GetRoleSignedField(source, RoleField.FireResistance),
                EarthResistance = _document.GetRoleSignedField(source, RoleField.EarthResistance),
            };
        }
        foreach ((ushort level, ushort objectId) in DefaultLearnedMagics)
            role.LearnedMagics.Add(new(level, objectId));
        return role;
    }

    // 固定模板只使用原版对象编号，因此 Classic 与所有 PALDLL 内容 profile 都能读取。
    // 它提供一条近似盖罗娇定位的完整成长线；作者可在 GUI 中逐行调整。
    private static readonly (ushort Level, ushort ObjectId)[] DefaultLearnedMagics =
    [
        (1, 0x0128),  // 气疗术
        (1, 0x0168),  // 鞭击
        (5, 0x0159),  // 御剑术
        (8, 0x0161),  // 御蜂术
        (12, 0x0137), // 天罡战气
        (16, 0x015A), // 万剑诀
        (20, 0x0162), // 万蚁蚀象
        (24, 0x015B), // 心剑合一
        (30, 0x0174), // 万蛊蚀天
        (36, 0x015C), // 天剑
        (42, 0x015F), // 武神
        (50, 0x016B), // 剑神
    ];

    private void RemoveLastRole()
    {
        RunUiAction(() =>
        {
            if (_library.CustomRoles.Count == 0) return;
            if (_roles.SelectedIndex != _library.CustomRoles.Count - 1)
                throw new InvalidOperationException("为保持存档角色 ID 稳定，只能移除末尾角色。");
            _library.CustomRoles.RemoveAt(_library.CustomRoles.Count - 1);
            _selectedIndex = -1;
            RefreshRoleList(_library.CustomRoles.Count - 1);
            if (_library.CustomRoles.Count == 0) SelectRole(-1);
        });
    }

    private void AddLearnedRow()
    {
        if (_selectedIndex < 0 || _learned.Rows.Cast<DataGridViewRow>().Count(row => !row.IsNewRow) >= 32) return;
        _learned.Rows.Add(1, 1, ResolveObjectName(1));
    }

    private void DeleteLearnedRow()
    {
        if (_learned.CurrentRow is DataGridViewRow row && !row.IsNewRow)
            _learned.Rows.Remove(row);
    }

    private void SaveLibrary()
    {
        RunUiAction(() =>
        {
            CommitSelected();
            _library.LibraryId = _libraryId.Text.Trim();
            _library.LibraryVersion = _libraryVersion.Text.Trim();
            _library.NativeRoleNames.Clear();
            foreach (DataGridViewRow row in _nativeNames.Rows)
            {
                if (row.IsNewRow) continue;
                int roleId = Convert.ToInt32(row.Cells[0].Value);
                string name = Convert.ToString(row.Cells[1].Value)?.Trim() ?? string.Empty;
                if (name.Length != 0) _library.NativeRoleNames.Add(roleId, name);
            }
            PalCustomRoleLibraryWriteResult result = PalCustomRoleLibraryStore.WriteAtomically(
                _gameDirectory, _library, createBackup: true,
                _document?.Catalog?.RuntimeObjectRecordCount);
            MessageBox.Show(this,
                result.BackupPath is null
                    ? $"角色库已保存：\r\n{result.Path}"
                    : $"角色库已保存：\r\n{result.Path}\r\n备份：{result.BackupPath}",
                "保存成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }, "无法保存角色库");
    }

    private string ResolveObjectName(ushort objectId) =>
        _document?.Catalog?.GetObjectName(objectId) ?? $"法术 #{objectId}";

    private ushort Value(string name) => decimal.ToUInt16(_general[name].Value);

    private NumericUpDown GeneralNumeric(string name)
    {
        var numeric = new NumericUpDown { Minimum = 0, Maximum = ushort.MaxValue, Dock = DockStyle.Fill, ThousandsSeparator = true };
        _general.Add(name, numeric);
        return numeric;
    }

    private static void AddGeneralRow(TableLayoutPanel table, int row, string firstLabel, Control first, string secondLabel, Control second)
    {
        table.Controls.Add(new Label { Text = firstLabel, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        table.Controls.Add(first, 1, row);
        table.Controls.Add(new Label { Text = secondLabel, AutoSize = true, Anchor = AnchorStyles.Left }, 2, row);
        table.Controls.Add(second, 3, row);
    }

    private static Button CreateButton(string text, EventHandler click)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += click;
        return button;
    }

    private static DataGridView CreateGrid(bool readOnly) => new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = readOnly,
        AllowUserToAddRows = !readOnly,
        AllowUserToDeleteRows = !readOnly,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        BackgroundColor = SystemColors.Window,
    };

    private void RunUiAction(Action action, string title = "操作失败")
    {
        try { action(); }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
