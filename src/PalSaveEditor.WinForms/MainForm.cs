using PalSaveEditor.Core;

namespace PalSaveEditor.WinForms;

internal sealed class MainForm : Form
{
    private static readonly string[] EquipmentSlotNames = ["头戴", "披挂", "身穿", "手持", "脚穿", "佩戴"];
    private static readonly HashSet<RoleField> SignedRoleFields =
    [
        RoleField.PoisonResistance,
        RoleField.WindResistance,
        RoleField.ThunderResistance,
        RoleField.WaterResistance,
        RoleField.FireResistance,
        RoleField.EarthResistance,
    ];

    private readonly ToolStripButton _openButton = new("打开");
    private readonly ToolStripButton _saveButton = new("保存") { Enabled = false };
    private readonly ToolStripButton _saveAsButton = new("另存为") { Enabled = false };
    private readonly ToolStripButton _resourcesButton = new("游戏资料目录") { Enabled = false };
    private readonly CheckBox _keepBackupCheckBox = new()
    {
        Text = "保留原存档备份",
        Checked = true,
        AutoSize = true,
        Margin = new Padding(6, 0, 3, 0),
    };
    private readonly ToolStripComboBox _formatCombo = new()
    {
        AutoSize = false,
        DropDownStyle = ComboBoxStyle.DropDownList,
        DropDownWidth = 360,
        Enabled = false,
        Width = 330,
    };
    private readonly ToolStripStatusLabel _status = new("请打开 1.RPG～5.RPG 存档");
    private readonly ToolStripStatusLabel _dirtyStatus = new() { Spring = true, TextAlign = ContentAlignment.MiddleRight };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill, Enabled = false };

    private readonly ListBox _partyList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly ListBox _followerList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly ComboBox _roleCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly NumericUpDown _experience = CreateUShortNumeric();
    private readonly Dictionary<RoleField, NumericUpDown> _roleFields = new();
    private readonly ListBox _magicList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly DataGridView _equipmentGrid = CreateGrid();

    private readonly DataGridView _inventoryGrid = CreateGrid();
    private readonly TextBox _inventorySearch = CreateInventorySearchBox();

    private readonly NumericUpDown _savedTimes = CreateUShortNumeric();
    private readonly NumericUpDown _scene = CreateUShortNumeric();
    private readonly NumericUpDown _viewportX = CreateUShortNumeric();
    private readonly NumericUpDown _viewportY = CreateUShortNumeric();
    private readonly NumericUpDown _music = CreateUShortNumeric();
    private readonly NumericUpDown _battleMusic = CreateUShortNumeric();
    private readonly NumericUpDown _cash = CreateUIntNumeric();
    private readonly NumericUpDown _collect = CreateUShortNumeric();
    private readonly TextBox _formatInfo = new() { ReadOnly = true, Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
    private readonly Label _resourceInfo = new() { AutoSize = true, Dock = DockStyle.Fill };

    private PalSaveDocument? _document;
    private bool _loadingControls;

    public MainForm(string? initialPath)
    {
        Text = "仙剑存档编辑器";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new(1_020, 700);
        ClientSize = new(1_180, 780);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new("Microsoft YaHei UI", 9F);

        foreach (var format in new[]
                 {
                     SaveFormat.Auto,
                     SaveFormat.PalWin95,
                     SaveFormat.PalDos,
                     SaveFormat.Dream220Win95,
                     SaveFormat.Dream220Dos,
                 })
        {
            _formatCombo.Items.Add(new FormatChoice(format));
        }
        _formatCombo.SelectedIndex = 0;

        var menu = BuildMenu();
        var toolStrip = BuildToolStrip();
        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_status);
        statusStrip.Items.Add(_dirtyStatus);

        _tabs.TabPages.Add(BuildRoleTab());
        _tabs.TabPages.Add(BuildInventoryTab());
        _tabs.TabPages.Add(BuildMiscTab());

        Controls.Add(_tabs);
        Controls.Add(toolStrip);
        Controls.Add(menu);
        Controls.Add(statusStrip);
        MainMenuStrip = menu;

        WireEvents();
        FormClosing += OnFormClosing;
        Shown += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(initialPath) && File.Exists(initialPath))
            {
                OpenDocument(initialPath!);
            }
        };
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip();
        var file = new ToolStripMenuItem("文件(&F)");
        var open = new ToolStripMenuItem("打开(&O)…", null, (_, _) => OpenFromDialog()) { ShortcutKeys = Keys.Control | Keys.O };
        var save = new ToolStripMenuItem("保存(&S)", null, (_, _) => SaveDocument()) { ShortcutKeys = Keys.Control | Keys.S };
        var saveAs = new ToolStripMenuItem("另存为(&A)…", null, (_, _) => SaveDocumentAs()) { ShortcutKeys = Keys.Control | Keys.Shift | Keys.S };
        file.DropDownItems.Add(open);
        file.DropDownItems.Add(save);
        file.DropDownItems.Add(saveAs);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("退出(&X)", null, (_, _) => Close());

        var help = new ToolStripMenuItem("帮助(&H)");
        help.DropDownItems.Add("安全说明", null, (_, _) => MessageBox.Show(
            this,
            "“保留原存档备份”默认勾选，可在工具栏取消。\r\n" +
            "取消后不会留下 .bak 文件，但写入复核完成前仍保留临时回滚副本。\r\n" +
            "编辑器只修改 PAL 固定字段，未知的对象与事件尾部逐字节保留。\r\n" +
            "建议关闭游戏后再编辑存档。",
            "安全说明",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information));
        help.DropDownItems.Add("关于", null, (_, _) => MessageBox.Show(
            this,
            "仙剑存档编辑器\r\n支持：仙剑 98 柔情版、仙剑 DOS、梦幻 2.20 DOS 版和 PALDLL 移植版。\r\n界面信息架构参考 PalEdit，解析与写入核心重新实现。",
            "关于",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information));

        menu.Items.Add(file);
        menu.Items.Add(help);
        return menu;
    }

    private ToolStrip BuildToolStrip()
    {
        var strip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new(4, 2, 4, 2) };
        strip.Items.Add(_openButton);
        strip.Items.Add(_saveButton);
        strip.Items.Add(_saveAsButton);
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(_resourcesButton);
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(new ToolStripLabel("存档格式："));
        strip.Items.Add(_formatCombo);
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(new ToolStripControlHost(_keepBackupCheckBox));
        return strip;
    }

    private TabPage BuildRoleTab()
    {
        var tab = new TabPage("主要角色");
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 285, FixedPanel = FixedPanel.Panel1 };

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        left.RowStyles.Add(new(SizeType.Percent, 58));
        left.RowStyles.Add(new(SizeType.Percent, 42));

        var partyGroup = new GroupBox { Text = "正式队员", Dock = DockStyle.Fill, Padding = new(10) };
        var partyRoot = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        partyRoot.RowStyles.Add(new(SizeType.Percent, 100));
        partyRoot.RowStyles.Add(new(SizeType.AutoSize));
        partyRoot.Controls.Add(_partyList, 0, 0);
        var partyButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        partyButtons.Controls.Add(CreateButton("添加…", (_, _) => AddPartyMember()));
        partyButtons.Controls.Add(CreateButton("移除", (_, _) => RemovePartyMember()));
        partyButtons.Controls.Add(CreateButton("上移", (_, _) => MovePartyMember(-1)));
        partyButtons.Controls.Add(CreateButton("下移", (_, _) => MovePartyMember(1)));
        partyRoot.Controls.Add(partyButtons, 0, 1);
        partyGroup.Controls.Add(partyRoot);

        var followerGroup = new GroupBox { Text = "随从（直接使用 MGO 形象编号）", Dock = DockStyle.Fill, Padding = new(10) };
        var followerRoot = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        followerRoot.RowStyles.Add(new(SizeType.Percent, 100));
        followerRoot.RowStyles.Add(new(SizeType.AutoSize));
        followerRoot.Controls.Add(_followerList, 0, 0);
        var followerButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        followerButtons.Controls.Add(CreateButton("添加…", (_, _) => AddFollower()));
        followerButtons.Controls.Add(CreateButton("修改…", (_, _) => EditFollower()));
        followerButtons.Controls.Add(CreateButton("移除", (_, _) => RemoveFollower()));
        followerButtons.Controls.Add(CreateButton("上移", (_, _) => MoveFollower(-1)));
        followerButtons.Controls.Add(CreateButton("下移", (_, _) => MoveFollower(1)));
        followerRoot.Controls.Add(followerButtons, 0, 1);
        followerGroup.Controls.Add(followerRoot);

        left.Controls.Add(partyGroup, 0, 0);
        left.Controls.Add(followerGroup, 0, 1);
        split.Panel1.Padding = new(8);
        split.Panel1.Controls.Add(left);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(8), RowCount = 3, ColumnCount = 1 };
        right.RowStyles.Add(new(SizeType.AutoSize));
        right.RowStyles.Add(new(SizeType.Percent, 64));
        right.RowStyles.Add(new(SizeType.Percent, 36));

        var rolePicker = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 3 };
        rolePicker.ColumnStyles.Add(new(SizeType.AutoSize));
        rolePicker.ColumnStyles.Add(new(SizeType.Percent, 100));
        rolePicker.ColumnStyles.Add(new(SizeType.AutoSize));
        rolePicker.Controls.Add(new Label { Text = "角色：", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        rolePicker.Controls.Add(_roleCombo, 1, 0);
        rolePicker.Controls.Add(CreateButton("最强属性", (_, _) => ApplyStrongestRole()), 2, 0);
        right.Controls.Add(rolePicker, 0, 0);

        var properties = new GroupBox { Text = "角色属性", Dock = DockStyle.Fill };
        var propertyRoot = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        propertyRoot.RowStyles.Add(new(SizeType.Percent, 100));
        propertyRoot.RowStyles.Add(new(SizeType.AutoSize));
        var propertyTable = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(8), ColumnCount = 4, RowCount = 9, AutoScroll = true };
        propertyTable.ColumnStyles.Add(new(SizeType.AutoSize));
        propertyTable.ColumnStyles.Add(new(SizeType.Percent, 50));
        propertyTable.ColumnStyles.Add(new(SizeType.AutoSize));
        propertyTable.ColumnStyles.Add(new(SizeType.Percent, 50));
        AddNumericRow(propertyTable, 0, "经验值", _experience, "等级", AddRoleField(RoleField.Level));
        AddNumericRow(propertyTable, 1, "体力", AddRoleField(RoleField.Hp), "最大体力", AddRoleField(RoleField.MaxHp));
        AddNumericRow(propertyTable, 2, "真气", AddRoleField(RoleField.Mp), "最大真气", AddRoleField(RoleField.MaxMp));
        AddNumericRow(propertyTable, 3, "武术", AddRoleField(RoleField.Attack), "灵力", AddRoleField(RoleField.MagicPower));
        AddNumericRow(propertyTable, 4, "防御", AddRoleField(RoleField.Defense), "身法", AddRoleField(RoleField.Dexterity));
        AddNumericRow(propertyTable, 5, "吉运", AddRoleField(RoleField.FleeRate), "抗毒", AddRoleField(RoleField.PoisonResistance));
        AddNumericRow(propertyTable, 6, "合体法术编号", AddRoleField(RoleField.CooperativeMagic), "姓名字库编号", AddRoleField(RoleField.NameWordId));
        AddNumericRow(propertyTable, 7, "头像编号", AddRoleField(RoleField.Avatar), "战斗形象编号", AddRoleField(RoleField.BattleSprite));
        AddNumericRow(propertyTable, 8, "地图形象编号", AddRoleField(RoleField.MapSprite), "行走帧上界（3=4 帧）", AddRoleField(RoleField.WalkFrames));
        propertyRoot.Controls.Add(propertyTable, 0, 0);

        var resistanceGroup = new GroupBox { Text = "五系基础抗性（可为负）", AutoSize = true, Dock = DockStyle.Fill, Padding = new(8, 4, 8, 6) };
        var resistanceFlow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        foreach (var (label, field) in new[]
                 {
                     ("风", RoleField.WindResistance),
                     ("雷", RoleField.ThunderResistance),
                     ("水", RoleField.WaterResistance),
                     ("火", RoleField.FireResistance),
                     ("土", RoleField.EarthResistance),
                 })
        {
            resistanceFlow.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new(6, 7, 2, 0) });
            var resistance = AddRoleField(field);
            resistance.Width = 78;
            resistanceFlow.Controls.Add(resistance);
        }
        resistanceGroup.Controls.Add(resistanceFlow);
        propertyRoot.Controls.Add(resistanceGroup, 0, 1);
        properties.Controls.Add(propertyRoot);
        right.Controls.Add(properties, 0, 1);

        var lower = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            SplitterDistance = 300,
        };
        var magicGroup = new GroupBox { Text = "法术", Dock = DockStyle.Fill, Padding = new(8) };
        var magicRoot = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        magicRoot.RowStyles.Add(new(SizeType.Percent, 100));
        magicRoot.RowStyles.Add(new(SizeType.AutoSize));
        magicRoot.Controls.Add(_magicList, 0, 0);
        var magicButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        magicButtons.Controls.Add(CreateButton("添加法术…", (_, _) => AddMagic()));
        magicButtons.Controls.Add(CreateButton("移除法术", (_, _) => RemoveMagic()));
        magicRoot.Controls.Add(magicButtons, 0, 1);
        magicGroup.Controls.Add(magicRoot);
        lower.Panel1.Controls.Add(magicGroup);

        var equipmentGroup = new GroupBox { Text = "装备", Dock = DockStyle.Fill, Padding = new(8) };
        _equipmentGrid.Columns.Add("slot", "部位");
        _equipmentGrid.Columns.Add("id", "编号");
        _equipmentGrid.Columns.Add("name", "物品");
        _equipmentGrid.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _equipmentGrid.Columns[2].MinimumWidth = 140;
        var equipmentRoot = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        equipmentRoot.RowStyles.Add(new(SizeType.Percent, 100));
        equipmentRoot.RowStyles.Add(new(SizeType.AutoSize));
        equipmentRoot.Controls.Add(_equipmentGrid, 0, 0);
        equipmentRoot.Controls.Add(CreateButton("更换装备…", (_, _) => EditEquipment()), 0, 1);
        equipmentGroup.Controls.Add(equipmentRoot);
        lower.Panel2.Controls.Add(equipmentGroup);
        right.Controls.Add(lower, 0, 2);

        split.Panel2.Controls.Add(right);
        tab.Controls.Add(split);
        return tab;
    }

    private TabPage BuildInventoryTab()
    {
        var tab = new TabPage("物品");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(10), RowCount = 3, ColumnCount = 1 };
        root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize));
        root.Controls.Add(_inventorySearch, 0, 0);

        _inventoryGrid.Columns.Add("slot", "槽位");
        _inventoryGrid.Columns.Add("id", "编号");
        _inventoryGrid.Columns.Add("name", "物品名称");
        _inventoryGrid.Columns.Add("amount", "数量");
        _inventoryGrid.Columns.Add("inuse", "装备/使用中");
        _inventoryGrid.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        root.Controls.Add(_inventoryGrid, 0, 1);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        buttons.Controls.Add(CreateButton("添加物品…", (_, _) => AddInventoryItem()));
        buttons.Controls.Add(CreateButton("编辑数量…", (_, _) => EditInventoryItem()));
        buttons.Controls.Add(CreateButton("移除物品", (_, _) => RemoveInventoryItem()));
        root.Controls.Add(buttons, 0, 2);
        tab.Controls.Add(root);
        return tab;
    }

    private TabPage BuildMiscTab()
    {
        var tab = new TabPage("杂项");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 2, RowCount = 3 };
        root.ColumnStyles.Add(new(SizeType.Percent, 52));
        root.ColumnStyles.Add(new(SizeType.Percent, 48));
        root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.AutoSize));

        var values = new GroupBox { Text = "地图、金钱和其他", Dock = DockStyle.Fill, Padding = new(12) };
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, RowCount = 4 };
        table.ColumnStyles.Add(new(SizeType.AutoSize));
        table.ColumnStyles.Add(new(SizeType.Percent, 50));
        table.ColumnStyles.Add(new(SizeType.AutoSize));
        table.ColumnStyles.Add(new(SizeType.Percent, 50));
        AddNumericRow(table, 0, "保存次数", _savedTimes, "场景", _scene);
        AddNumericRow(table, 1, "X 坐标", _viewportX, "Y 坐标", _viewportY);
        AddNumericRow(table, 2, "音乐", _music, "战斗音乐", _battleMusic);
        AddNumericRow(table, 3, "金钱", _cash, "灵葫值", _collect);
        values.Controls.Add(table);
        root.Controls.Add(values, 0, 0);

        var safety = new GroupBox { Text = "格式识别与写入安全", Dock = DockStyle.Fill, Padding = new(12) };
        safety.Controls.Add(_formatInfo);
        root.Controls.Add(safety, 1, 0);

        var note = new Label
        {
            Text = "提示：请先退出游戏再保存。“保留原存档备份”默认勾选，可在工具栏取消；对象表与事件区不会被重建。",
            AutoSize = true,
            ForeColor = Color.FromArgb(120, 65, 20),
            Padding = new(4, 14, 4, 8),
            Dock = DockStyle.Fill,
        };
        root.SetColumnSpan(note, 2);
        root.Controls.Add(note, 0, 1);
        root.SetColumnSpan(_resourceInfo, 2);
        root.Controls.Add(_resourceInfo, 0, 2);
        tab.Controls.Add(root);
        return tab;
    }

    private void WireEvents()
    {
        _openButton.Click += (_, _) => OpenFromDialog();
        _saveButton.Click += (_, _) => SaveDocument();
        _saveAsButton.Click += (_, _) => SaveDocumentAs();
        _resourcesButton.Click += (_, _) => SelectResourceDirectory();
        _formatCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_loadingControls || _document is null || _formatCombo.SelectedItem is not FormatChoice choice)
            {
                return;
            }

            RunUiAction(() =>
            {
                _document.SetFormat(choice.Format);
                RefreshDocumentInfo();
            });
        };

        _partyList.SelectedIndexChanged += (_, _) =>
        {
            if (_partyList.SelectedItem is PartyChoice choice)
            {
                SelectRole(choice.RoleId);
            }
        };
        _followerList.DoubleClick += (_, _) => EditFollower();
        _roleCombo.SelectedIndexChanged += (_, _) => LoadSelectedRole();
        _experience.ValueChanged += (_, _) =>
        {
            if (!_loadingControls && _document is not null && SelectedRoleId is int roleId)
            {
                _document.SetExperience(roleId, decimal.ToUInt16(_experience.Value));
                UpdateDirtyState();
            }
        };
        foreach (KeyValuePair<RoleField, NumericUpDown> pair in _roleFields)
        {
            RoleField capturedField = pair.Key;
            NumericUpDown control = pair.Value;
            control.ValueChanged += (_, _) =>
            {
                if (!_loadingControls && _document is not null && SelectedRoleId is int roleId)
                {
                    if (SignedRoleFields.Contains(capturedField))
                    {
                        _document.SetRoleSignedField(roleId, capturedField, decimal.ToInt16(control.Value));
                    }
                    else
                    {
                        _document.SetRoleField(roleId, capturedField, decimal.ToUInt16(control.Value));
                    }
                    if (capturedField == RoleField.NameWordId)
                    {
                        RefreshRoleChoices(roleId);
                    }
                    UpdateDirtyState();
                }
            };
        }

        _inventorySearch.TextChanged += (_, _) => RefreshInventory();
        _savedTimes.ValueChanged += (_, _) => SetMiscValue(() => _document!.SavedTimes = decimal.ToUInt16(_savedTimes.Value));
        _scene.ValueChanged += (_, _) => SetMiscValue(() => _document!.SceneNumber = decimal.ToUInt16(_scene.Value));
        _viewportX.ValueChanged += (_, _) => SetMiscValue(() => _document!.ViewportX = decimal.ToUInt16(_viewportX.Value));
        _viewportY.ValueChanged += (_, _) => SetMiscValue(() => _document!.ViewportY = decimal.ToUInt16(_viewportY.Value));
        _music.ValueChanged += (_, _) => SetMiscValue(() => _document!.MusicNumber = decimal.ToUInt16(_music.Value));
        _battleMusic.ValueChanged += (_, _) => SetMiscValue(() => _document!.BattleMusicNumber = decimal.ToUInt16(_battleMusic.Value));
        _cash.ValueChanged += (_, _) => SetMiscValue(() => _document!.Cash = decimal.ToUInt32(_cash.Value));
        _collect.ValueChanged += (_, _) => SetMiscValue(() => _document!.CollectValue = decimal.ToUInt16(_collect.Value));
    }

    private void OpenFromDialog()
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "打开仙剑存档",
            Filter = "仙剑存档 (*.RPG)|*.RPG|所有文件 (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            OpenDocument(dialog.FileName);
        }
    }

    private void OpenDocument(string path)
    {
        RunUiAction(() =>
        {
            _document = PalSaveDocument.Load(path);
            _tabs.Enabled = true;
            _saveButton.Enabled = true;
            _saveAsButton.Enabled = true;
            _resourcesButton.Enabled = true;
            _formatCombo.Enabled = true;
            RefreshAll();
        }, "无法打开存档");
    }

    private void SaveDocument()
    {
        if (_document is null)
        {
            return;
        }

        RunUiAction(() =>
        {
            var result = _document.Save(createBackup: _keepBackupCheckBox.Checked);
            _status.Text = result.BackupPath is null
                ? $"已保存；未保留备份：{result.TargetPath}"
                : $"已保存；备份：{result.BackupPath}";
            UpdateDirtyState();
        }, "保存失败");
    }

    private void SaveDocumentAs()
    {
        if (_document is null)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "另存仙剑存档",
            Filter = "仙剑存档 (*.RPG)|*.RPG|所有文件 (*.*)|*.*",
            FileName = Path.GetFileName(_document.Path),
            InitialDirectory = Path.GetDirectoryName(_document.Path),
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        RunUiAction(() =>
        {
            var result = _document.Save(dialog.FileName, createBackup: _keepBackupCheckBox.Checked);
            _status.Text = result.BackupPath is null
                ? $"已另存为；未保留备份：{result.TargetPath}"
                : $"已另存为；备份：{result.BackupPath}";
            Text = $"仙剑存档编辑器 - [{Path.GetFileName(result.TargetPath)}]";
            UpdateDirtyState();
        }, "另存失败");
    }

    private void SelectResourceDirectory()
    {
        if (_document is null)
        {
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = "选择包含 WORD.DAT 和 SSS.MKF 的仙剑游戏资料目录",
            SelectedPath = _document.Catalog?.SourceDirectory ?? Path.GetDirectoryName(_document.Path) ?? string.Empty,
        };
#if !NETFRAMEWORK
        dialog.UseDescriptionForTitle = true;
#endif
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var selectedPath = dialog.SelectedPath ?? string.Empty;
        RunUiAction(() =>
        {
            _document.SetCatalog(PalResourceCatalog.Load(selectedPath));
            RefreshAll();
        }, "无法读取游戏资料");
    }

    private void RefreshAll()
    {
        if (_document is null)
        {
            return;
        }

        _loadingControls = true;
        try
        {
            Text = $"仙剑存档编辑器 - [{Path.GetFileName(_document.Path)}]";
            var formatIndex = _formatCombo.Items.Cast<FormatChoice>().ToList().FindIndex(choice => choice.Format == _document.Format);
            _formatCombo.SelectedIndex = Math.Max(0, formatIndex);
            RefreshRoleChoices();
            RefreshParty();
            RefreshFollowers();
            if (_roleCombo.Items.Count > 0)
            {
                _roleCombo.SelectedIndex = 0;
                LoadSelectedRole();
            }
            RefreshInventory();
            LoadMiscValues();
            RefreshDocumentInfo();
        }
        finally
        {
            _loadingControls = false;
        }
        LoadSelectedRole();
        UpdateDirtyState();
    }

    private void RefreshRoleChoices(int? preserveRoleId = null)
    {
        if (_document is null)
        {
            return;
        }

        preserveRoleId ??= SelectedRoleId;
        var oldLoading = _loadingControls;
        _loadingControls = true;
        try
        {
            _roleCombo.Items.Clear();
            for (var roleId = 0; roleId < PalSaveLayout.RoleCount; roleId++)
            {
                _roleCombo.Items.Add(new RoleChoice(roleId, _document.GetRole(roleId).DisplayName));
            }
            var index = preserveRoleId is int id ? id : 0;
            _roleCombo.SelectedIndex = Math.Max(0, Math.Min(_roleCombo.Items.Count - 1, index));
            RefreshParty();
            RefreshFollowers();
        }
        finally
        {
            _loadingControls = oldLoading;
        }
    }

    private void RefreshParty()
    {
        if (_document is null)
        {
            return;
        }

        var selected = _partyList.SelectedIndex;
        _partyList.Items.Clear();
        foreach (var member in _document.GetParty())
        {
            _partyList.Items.Add(new PartyChoice(member.PartyIndex, member.RoleId, _document.GetRole(member.RoleId).DisplayName));
        }
        if (_partyList.Items.Count > 0)
        {
            _partyList.SelectedIndex = Math.Max(0, Math.Min(_partyList.Items.Count - 1, selected));
        }
    }

    private void RefreshFollowers()
    {
        if (_document is null)
        {
            return;
        }

        var selected = _followerList.SelectedIndex;
        _followerList.Items.Clear();
        foreach (var follower in _document.GetFollowers())
        {
            _followerList.Items.Add(new FollowerChoice(follower.FollowerIndex, follower.SpriteId));
        }
        if (_followerList.Items.Count > 0)
        {
            _followerList.SelectedIndex = Math.Max(0, Math.Min(_followerList.Items.Count - 1, selected));
        }
    }

    private void LoadSelectedRole()
    {
        if (_document is null || SelectedRoleId is not int roleId)
        {
            return;
        }

        _loadingControls = true;
        try
        {
            var role = _document.GetRole(roleId);
            _experience.Value = role.Experience;
            foreach (KeyValuePair<RoleField, NumericUpDown> pair in _roleFields)
            {
                RoleField field = pair.Key;
                NumericUpDown control = pair.Value;
                control.Value = SignedRoleFields.Contains(field)
                    ? _document.GetRoleSignedField(roleId, field)
                    : _document.GetRoleField(roleId, field);
            }

            _magicList.Items.Clear();
            foreach (var magic in _document.GetMagics(roleId))
            {
                _magicList.Items.Add(new MagicChoice(magic));
            }

            _equipmentGrid.Rows.Clear();
            foreach (var equipment in _document.GetEquipment(roleId))
            {
                var row = _equipmentGrid.Rows.Add(EquipmentSlotNames[equipment.Slot], equipment.ItemId, equipment.DisplayName);
                _equipmentGrid.Rows[row].Tag = equipment;
            }
        }
        finally
        {
            _loadingControls = false;
        }
    }

    private void RefreshInventory()
    {
        if (_document is null)
        {
            return;
        }

        var filter = _inventorySearch.Text.Trim();
        _inventoryGrid.Rows.Clear();
        foreach (var entry in _document.GetInventory())
        {
            if (filter.Length != 0 &&
                entry.DisplayName.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) < 0 &&
                entry.ItemId.ToString().IndexOf(filter, StringComparison.Ordinal) < 0)
            {
                continue;
            }

            var row = _inventoryGrid.Rows.Add(entry.Slot, entry.ItemId, entry.DisplayName, entry.Amount, entry.AmountInUse);
            _inventoryGrid.Rows[row].Tag = entry;
        }
    }

    private void LoadMiscValues()
    {
        if (_document is null)
        {
            return;
        }

        _savedTimes.Value = _document.SavedTimes;
        _scene.Value = _document.SceneNumber;
        _viewportX.Value = _document.ViewportX;
        _viewportY.Value = _document.ViewportY;
        _music.Value = _document.MusicNumber;
        _battleMusic.Value = _document.BattleMusicNumber;
        _cash.Value = _document.Cash;
        _collect.Value = _document.CollectValue;
    }

    private void RefreshDocumentInfo()
    {
        if (_document is null)
        {
            return;
        }

        var eventOffset = _document.Format is SaveFormat.PalDos or SaveFormat.Dream220Dos
            ? PalSaveLayout.DosEventObjectOffset
            : PalSaveLayout.WinEventObjectOffset;
        var eventCount = (_document.Length - eventOffset) / 32;
        _formatInfo.Text =
            $"当前格式：{_document.Format.GetDisplayName()}\r\n" +
            $"文件长度：{_document.Length:N0} 字节\r\n" +
            $"固定前缀：{eventOffset:N0} 字节\r\n" +
            $"事件记录：{eventCount:N0} × 32 字节\r\n\r\n" +
            $"识别依据：{_document.Detection.Reason}\r\n" +
            $"证据强度：{(_document.Detection.IsHeuristic ? "长度/边界启发式，可手动复核" : "配套资源复核")}";
        _resourceInfo.Text = _document.Catalog is null
            ? "游戏资料：未加载。物品和法术将显示为编号；可点击工具栏“游戏资料目录”。"
            : BuildResourceInfo(_document.Catalog);
        _status.Text = $"{_document.Format.GetDisplayName()} · {_document.Length:N0} 字节 · {Path.GetFileName(_document.Path)}";
    }

    private static string BuildResourceInfo(PalResourceCatalog catalog)
    {
        string profile = catalog.IsActiveProfile
            ? $"active profile {catalog.ActiveProfileId}@{catalog.ActiveProfileVersion}（{catalog.ActiveProfileDisplayName}）  |  "
            : string.Empty;
        return $"游戏资料：{profile}{catalog.SourceDirectory}  |  WORD {catalog.WordCount} 条  |  " +
               $"对象记录 {catalog.ObjectRecordSize} 字节  |  事件区 {catalog.EventObjectBytes:N0} 字节";
    }

    private void AddPartyMember()
    {
        if (_document is null)
        {
            return;
        }
        var current = _document.GetParty().Select(member => member.RoleId).ToList();
        var available = Enumerable.Range(0, PalSaveLayout.RoleCount).Select(value => (ushort)value).Where(id => !current.Contains(id)).ToList();
        if (current.Count + _document.FollowerCount >= PalSaveLayout.PartyCapacity || available.Count == 0)
        {
            MessageBox.Show(this, "正式队员与随从共享 5 条队列记录，当前已无空位。", "队伍调整", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dialog = new Form { Text = "添加队员", StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, ClientSize = new(320, 120), MinimizeBox = false, MaximizeBox = false };
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, Margin = new(12) };
        foreach (var id in available) combo.Items.Add(new RoleChoice(id, _document.GetRole(id).DisplayName));
        combo.SelectedIndex = 0;
        var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        dialog.Controls.Add(combo);
        dialog.Controls.Add(ok);
        dialog.AcceptButton = ok;
        if (dialog.ShowDialog(this) == DialogResult.OK && combo.SelectedItem is RoleChoice choice)
        {
            current.Add((ushort)choice.RoleId);
            _document.SetParty(current);
            RefreshParty();
            RefreshFollowers();
            UpdateDirtyState();
        }
    }

    private void RemovePartyMember()
    {
        if (_document is null || _partyList.SelectedIndex < 0)
        {
            return;
        }
        var roles = _document.GetParty().Select(member => member.RoleId).ToList();
        if (roles.Count == 1)
        {
            MessageBox.Show(this, "队伍至少需要一名角色。", "队伍调整", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        roles.RemoveAt(_partyList.SelectedIndex);
        _document.SetParty(roles);
        RefreshParty();
        RefreshFollowers();
        UpdateDirtyState();
    }

    private void MovePartyMember(int direction)
    {
        if (_document is null || _partyList.SelectedIndex < 0)
        {
            return;
        }
        var roles = _document.GetParty().Select(member => member.RoleId).ToList();
        var source = _partyList.SelectedIndex;
        var target = source + direction;
        if ((uint)target >= (uint)roles.Count)
        {
            return;
        }
        (roles[source], roles[target]) = (roles[target], roles[source]);
        _document.SetParty(roles);
        RefreshParty();
        RefreshFollowers();
        _partyList.SelectedIndex = target;
        UpdateDirtyState();
    }

    private void AddFollower()
    {
        if (_document is null)
        {
            return;
        }

        var followers = _document.GetFollowers().Select(follower => follower.SpriteId).ToList();
        if (followers.Count >= PalSaveLayout.FollowerCapacity)
        {
            MessageBox.Show(this, "原版随从数量上限为 2。", "随从调整", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_document.PartyCount + followers.Count >= PalSaveLayout.PartyCapacity)
        {
            MessageBox.Show(this, "正式队员与随从共享 5 条队列记录，当前已无空位。", "随从调整", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var spriteId = PromptFollowerSprite(12, "添加随从");
        if (spriteId is null)
        {
            return;
        }
        followers.Add(spriteId.Value);
        RunUiAction(() =>
        {
            _document.SetFollowers(followers);
            RefreshFollowers();
            UpdateDirtyState();
        }, "无法添加随从");
    }

    private void EditFollower()
    {
        if (_document is null || _followerList.SelectedItem is not FollowerChoice selected)
        {
            return;
        }

        var spriteId = PromptFollowerSprite(selected.SpriteId, "修改随从");
        if (spriteId is null)
        {
            return;
        }
        var followers = _document.GetFollowers().Select(follower => follower.SpriteId).ToList();
        followers[selected.FollowerIndex] = spriteId.Value;
        RunUiAction(() =>
        {
            _document.SetFollowers(followers);
            RefreshFollowers();
            _followerList.SelectedIndex = selected.FollowerIndex;
            UpdateDirtyState();
        }, "无法修改随从");
    }

    private void RemoveFollower()
    {
        if (_document is null || _followerList.SelectedItem is not FollowerChoice selected)
        {
            return;
        }

        var followers = _document.GetFollowers().Select(follower => follower.SpriteId).ToList();
        followers.RemoveAt(selected.FollowerIndex);
        RunUiAction(() =>
        {
            _document.SetFollowers(followers);
            RefreshFollowers();
            UpdateDirtyState();
        }, "无法移除随从");
    }

    private void MoveFollower(int direction)
    {
        if (_document is null || _followerList.SelectedItem is not FollowerChoice selected)
        {
            return;
        }

        var followers = _document.GetFollowers().Select(follower => follower.SpriteId).ToList();
        var source = selected.FollowerIndex;
        var target = source + direction;
        if ((uint)target >= (uint)followers.Count)
        {
            return;
        }
        (followers[source], followers[target]) = (followers[target], followers[source]);
        RunUiAction(() =>
        {
            _document.SetFollowers(followers);
            RefreshFollowers();
            _followerList.SelectedIndex = target;
            UpdateDirtyState();
        }, "无法调整随从顺序");
    }

    private ushort? PromptFollowerSprite(ushort initialValue, string title)
    {
        using var dialog = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ClientSize = new(390, 165),
            MinimizeBox = false,
            MaximizeBox = false,
        };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(12), RowCount = 3, ColumnCount = 2 };
        root.ColumnStyles.Add(new(SizeType.AutoSize));
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        var numeric = CreateUShortNumeric();
        numeric.Minimum = 1;
        numeric.Value = initialValue == 0 ? 1 : initialValue;
        var presets = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        presets.Items.Add(new FollowerPreset("天鬼皇", 12));
        presets.Items.Add(new FollowerPreset("云姨", 81));
        presets.Items.Add(new FollowerPreset("自定义（保留当前编号）", null));
        presets.SelectedIndex = initialValue == 12 ? 0 : initialValue == 81 ? 1 : 2;
        presets.SelectedIndexChanged += (_, _) =>
        {
            if (presets.SelectedItem is FollowerPreset preset && preset.SpriteId is ushort value)
            {
                numeric.Value = value;
            }
        };
        root.Controls.Add(new Label { Text = "常用随从：", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        root.Controls.Add(presets, 1, 0);
        root.Controls.Add(new Label { Text = "MGO 编号：", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        root.Controls.Add(numeric, 1, 1);
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
        var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 2);
        root.SetColumnSpan(buttons, 2);
        dialog.Controls.Add(root);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        return dialog.ShowDialog(this) == DialogResult.OK ? decimal.ToUInt16(numeric.Value) : null;
    }

    private void ApplyStrongestRole()
    {
        if (_document is null || SelectedRoleId is not int roleId)
        {
            return;
        }
        if (MessageBox.Show(this, "将当前角色等级设为 99，体力/真气和五项战斗属性设为 999？", "最强属性", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
        {
            return;
        }
        _document.SetRoleField(roleId, RoleField.Level, 99);
        foreach (var field in new[] { RoleField.MaxHp, RoleField.Hp, RoleField.MaxMp, RoleField.Mp, RoleField.Attack, RoleField.MagicPower, RoleField.Defense, RoleField.Dexterity, RoleField.FleeRate })
        {
            _document.SetRoleField(roleId, field, 999);
        }
        LoadSelectedRole();
        UpdateDirtyState();
    }

    private void AddMagic()
    {
        if (_document is null || SelectedRoleId is not int roleId)
        {
            return;
        }
        using var dialog = new ObjectPickerDialog(_document.Catalog, "添加法术");
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            RunUiAction(() =>
            {
                _document.AddMagic(roleId, dialog.SelectedId);
                LoadSelectedRole();
                UpdateDirtyState();
            });
        }
    }

    private void RemoveMagic()
    {
        if (_document is null || SelectedRoleId is not int roleId || _magicList.SelectedItem is not MagicChoice choice)
        {
            return;
        }
        _document.RemoveMagic(roleId, choice.Entry.Slot);
        LoadSelectedRole();
        UpdateDirtyState();
    }

    private void EditEquipment()
    {
        if (_document is null || SelectedRoleId is not int roleId || _equipmentGrid.CurrentRow?.Tag is not EquipmentEntry equipment)
        {
            return;
        }
        using var dialog = new ObjectPickerDialog(_document.Catalog, $"更换{EquipmentSlotNames[equipment.Slot]}", equipment.ItemId);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _document.SetEquipment(roleId, equipment.Slot, dialog.SelectedId);
            LoadSelectedRole();
            UpdateDirtyState();
        }
    }

    private void AddInventoryItem()
    {
        if (_document is null)
        {
            return;
        }
        using var picker = new ObjectPickerDialog(_document.Catalog, "添加物品");
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        using var amount = new NumericPromptDialog("添加物品", "数量：", 1);
        if (amount.ShowDialog(this) != DialogResult.OK) return;
        RunUiAction(() =>
        {
            _document.AddInventoryItem(picker.SelectedId, decimal.ToUInt16(amount.SelectedValue));
            RefreshInventory();
            UpdateDirtyState();
        });
    }

    private void EditInventoryItem()
    {
        if (_document is null || _inventoryGrid.CurrentRow?.Tag is not InventoryEntry entry)
        {
            return;
        }
        using var amount = new NumericPromptDialog($"编辑 {entry.DisplayName}", "数量：", entry.Amount);
        if (amount.ShowDialog(this) == DialogResult.OK)
        {
            _document.SetInventorySlot(entry.Slot, entry.ItemId, decimal.ToUInt16(amount.SelectedValue), Math.Min(entry.AmountInUse, decimal.ToUInt16(amount.SelectedValue)));
            RefreshInventory();
            UpdateDirtyState();
        }
    }

    private void RemoveInventoryItem()
    {
        if (_document is null || _inventoryGrid.CurrentRow?.Tag is not InventoryEntry entry)
        {
            return;
        }
        if (MessageBox.Show(this, $"从背包移除“{entry.DisplayName}”？", "移除物品", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
        {
            _document.ClearInventorySlot(entry.Slot);
            RefreshInventory();
            UpdateDirtyState();
        }
    }

    private void SetMiscValue(Action action)
    {
        if (_loadingControls || _document is null)
        {
            return;
        }
        action();
        UpdateDirtyState();
    }

    private void UpdateDirtyState()
    {
        _dirtyStatus.Text = _document?.IsDirty == true ? "● 有未保存修改" : "";
        _saveButton.Enabled = _document is not null;
    }

    private bool ConfirmDiscardChanges()
    {
        return _document?.IsDirty != true || MessageBox.Show(
            this,
            "当前存档有未保存修改，确定放弃吗？",
            "未保存修改",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) == DialogResult.Yes;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!ConfirmDiscardChanges())
        {
            e.Cancel = true;
        }
    }

    private void SelectRole(int roleId)
    {
        for (var i = 0; i < _roleCombo.Items.Count; i++)
        {
            if (_roleCombo.Items[i] is RoleChoice choice && choice.RoleId == roleId)
            {
                _roleCombo.SelectedIndex = i;
                return;
            }
        }
    }

    private int? SelectedRoleId => _roleCombo.SelectedItem is RoleChoice choice ? choice.RoleId : null;

    private NumericUpDown AddRoleField(RoleField field)
    {
        var numeric = SignedRoleFields.Contains(field) ? CreateInt16Numeric() : CreateUShortNumeric();
        _roleFields.Add(field, numeric);
        return numeric;
    }

    private static void AddNumericRow(TableLayoutPanel table, int row, string firstLabel, Control first, string secondLabel, Control second)
    {
        table.Controls.Add(new Label { Text = firstLabel, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        first.Dock = DockStyle.Fill;
        table.Controls.Add(first, 1, row);
        table.Controls.Add(new Label { Text = secondLabel, AutoSize = true, Anchor = AnchorStyles.Left }, 2, row);
        second.Dock = DockStyle.Fill;
        table.Controls.Add(second, 3, row);
    }

    private static NumericUpDown CreateUShortNumeric() => new()
    {
        Minimum = 0,
        Maximum = ushort.MaxValue,
        ThousandsSeparator = true,
        Width = 160,
    };

    private static NumericUpDown CreateInt16Numeric() => new()
    {
        Minimum = short.MinValue,
        Maximum = short.MaxValue,
        ThousandsSeparator = true,
        Width = 160,
    };

    private static NumericUpDown CreateUIntNumeric() => new()
    {
        Minimum = 0,
        Maximum = uint.MaxValue,
        ThousandsSeparator = true,
        Width = 180,
    };

    private static Button CreateButton(string text, EventHandler onClick)
    {
        var button = new Button { Text = text, AutoSize = true, Margin = new(3, 5, 3, 3) };
        button.Click += onClick;
        return button;
    }

    private static TextBox CreateInventorySearchBox()
    {
        var textBox = new TextBox { Dock = DockStyle.Fill };
#if !NETFRAMEWORK
        textBox.PlaceholderText = "筛选名称或编号…";
#endif
        return textBox;
    }

    private static DataGridView CreateGrid() => new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
        BackgroundColor = SystemColors.Window,
    };

    private void RunUiAction(Action action, string title = "操作失败")
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private sealed record FormatChoice(SaveFormat Format)
    {
        public override string ToString() => Format.GetDisplayName();
    }

    private sealed record RoleChoice(int RoleId, string Name)
    {
        public override string ToString() => $"{RoleId} - {Name}";
    }

    private sealed record PartyChoice(int PartyIndex, ushort RoleId, string Name)
    {
        public override string ToString() => $"{PartyIndex + 1}. {Name}（角色 {RoleId}）";
    }

    private sealed record FollowerChoice(int FollowerIndex, ushort SpriteId)
    {
        public override string ToString() => $"{FollowerIndex + 1}. {GetFollowerName(SpriteId)}（MGO {SpriteId}）";

        private static string GetFollowerName(ushort spriteId) => spriteId switch
        {
            12 => "天鬼皇",
            81 => "云姨",
            _ => "自定义随从",
        };
    }

    private sealed record FollowerPreset(string Name, ushort? SpriteId)
    {
        public override string ToString() => SpriteId is ushort id ? $"{Name}（MGO {id}）" : Name;
    }

    private sealed record MagicChoice(MagicEntry Entry)
    {
        public override string ToString() => $"{Entry.MagicId,4}  {Entry.DisplayName}";
    }
}
