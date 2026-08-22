namespace PalSaveChecker.Core;

public sealed record RepairRunDecision(bool CanRepair, string? Warning);

public static class RepairRunPolicy
{
    public static RepairRunDecision Evaluate(bool isPalRunning) =>
        isPalRunning
            ? new RepairRunDecision(
                true,
                "检测到 PAL 游戏仍在运行。可以修复磁盘存档，但请不要在修复过程中保存游戏；游戏之后再次保存同一槽位时，可能会用内存中的旧数据覆盖修复结果。")
            : new RepairRunDecision(true, null);
}
