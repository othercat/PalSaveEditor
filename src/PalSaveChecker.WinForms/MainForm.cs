using System.Diagnostics;
using System.Text;
using PalSaveChecker.Core;

namespace PalSaveChecker.WinForms;

internal sealed class MainForm : Form
{
    private readonly Button _checkButton = new() { Text = "检查", AutoSize = true };
    private readonly Button _repairButton = new() { Text = "修复", AutoSize = true };
    private readonly CheckBox _keepBackupCheckBox = new()
    {
        Text = "保留原存档备份",
        Checked = true,
        AutoSize = true,
        Margin = new Padding(12, 6, 3, 3),
    };
    private readonly RichTextBox _output = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        DetectUrls = false,
        Font = new Font("Microsoft YaHei UI", 10F),
        BackColor = SystemColors.Window,
    };
    private readonly SaveCompatibilityService _service = new();
    private readonly string _gameRoot;

    public MainForm()
    {
        Text = "仙剑98存档检查工具";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(680, 440);
        ClientSize = new Size(820, 560);
        Font = new Font("Microsoft YaHei UI", 9F);

        _gameRoot = GameDirectoryLocator.Resolve(AppContext.BaseDirectory);
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(10, 9, 10, 6),
            WrapContents = false,
        };
        buttonPanel.Controls.Add(_checkButton);
        buttonPanel.Controls.Add(_repairButton);
        buttonPanel.Controls.Add(_keepBackupCheckBox);
        Controls.Add(_output);
        Controls.Add(buttonPanel);

        _checkButton.Click += (_, _) => CheckSaves();
        _repairButton.Click += (_, _) => RepairSaves();
        Shown += (_, _) => BeginInvoke(CheckSaves);
    }

    private void CheckSaves()
    {
        SetBusy(true);
        try
        {
            SaveCheckReport report = _service.Check(_gameRoot);
            _output.Text = FormatReport(report);
            _repairButton.Enabled = report.CanRepair;
        }
        catch (Exception ex)
        {
            _output.Text = $"检查失败：{ex.Message}";
            _repairButton.Enabled = false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void RepairSaves()
    {
        bool palRunning = IsPalRunning();
        bool keepBackup = _keepBackupCheckBox.Checked;
        RepairRunDecision runDecision = RepairRunPolicy.Evaluate(palRunning);

        SaveCheckReport current = _service.Check(_gameRoot);
        if (!current.CanRepair)
        {
            _output.Text = FormatReport(current);
            MessageBox.Show(this,
                current.ReferenceError is null ? "当前没有可自动修复的污染存档。" : current.ReferenceError,
                "无法修复", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string confirmation = "将按当前 DefaultPatch 的 SSS.MKF 修复受污染的对象记录。";
        confirmation += keepBackup
            ? "每个原存档都会保留带时间戳的备份。"
            : "不会保留备份；落盘复核完成前仍会使用临时回滚副本。";
        if (!string.IsNullOrWhiteSpace(runDecision.Warning))
        {
            confirmation += $"\n\n{runDecision.Warning}";
        }
        confirmation += "\n\n是否继续？";

        if (MessageBox.Show(this,
            confirmation,
            "确认修复", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        SetBusy(true);
        try
        {
            SaveRepairReport result = _service.Repair(_gameRoot, keepBackup);
            var text = new StringBuilder(FormatReport(result.After));
            text.AppendLine().AppendLine("修复结果：");
            foreach (SaveRepairItem item in result.Results)
            {
                text.Append(item.Success ? "[成功] " : "[失败] ")
                    .Append(item.FileName).Append("：").AppendLine(item.Message);
                if (!string.IsNullOrWhiteSpace(item.BackupPath))
                {
                    text.Append("        备份：").AppendLine(item.BackupPath);
                }
            }
            _output.Text = text.ToString();
            _repairButton.Enabled = result.After.CanRepair;

            if (result.HasFailures)
            {
                MessageBox.Show(this, "至少一个存档无法安全修复；详情见主窗口，未通过复核的文件不会被保留为修复结果。",
                    "无法完全修复", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                string message = keepBackup
                    ? "修复及落盘复核完成。原存档备份保留在游戏目录。"
                    : "修复及落盘复核完成。按所选设置未保留备份。";
                if (palRunning)
                {
                    message += "\n\n游戏仍在运行：请直接读入修复后的存档；若之后保存同一槽位，游戏可能重新写入内存中的旧数据。";
                }
                MessageBox.Show(this, message,
                    "修复完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"无法修复：{ex.Message}", "无法修复",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        UseWaitCursor = busy;
        _checkButton.Enabled = !busy;
        _keepBackupCheckBox.Enabled = !busy;
        if (busy)
        {
            _repairButton.Enabled = false;
        }
    }

    private static bool IsPalRunning()
    {
        Process[] processes = Process.GetProcessesByName("PAL");
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static string FormatReport(SaveCheckReport report)
    {
        var text = new StringBuilder();
        text.Append("游戏目录：").AppendLine(report.GameRoot);
        if (report.ReferenceError is not null)
        {
            text.Append("参考资源：[失败] ").AppendLine(report.ReferenceError);
        }
        else
        {
            text.Append("参考资源：").AppendLine(report.ReferenceDescription);
        }
        text.AppendLine();

        foreach (SaveCheckItem item in report.Saves)
        {
            string label = item.Status switch
            {
                SaveCheckStatus.Missing => "未找到",
                SaveCheckStatus.Clean => "正常",
                SaveCheckStatus.Polluted => "疑似污染",
                SaveCheckStatus.Unreadable => "无法检查",
                _ => item.Status.ToString(),
            };
            text.Append('[').Append(label).Append("] ").Append(item.FileName).Append("：").AppendLine(item.Risk);
            if (item.DefinitionMismatchCount > 0 || item.InvalidScriptCount > 0)
            {
                text.Append("        异常字段：定义 ").Append(item.DefinitionMismatchCount)
                    .Append("，脚本索引 ").AppendLine(item.InvalidScriptCount.ToString());
            }
            if (!string.IsNullOrWhiteSpace(item.Error))
            {
                text.Append("        原因：").AppendLine(item.Error);
            }
        }

        text.AppendLine();
        text.AppendLine("说明：检查只比较当前补丁中应稳定的对象定义，并验证会随剧情推进的脚本索引是否仍在有效范围内。未报告异常不等于覆盖所有存档损坏类型。 ");
        return text.ToString();
    }
}
