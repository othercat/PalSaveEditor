# 仙剑98存档检查工具

这是与存档编辑器分离的 Windows Target，发布文件名为 `仙剑98存档检查工具.exe`。部署到游戏目录的 `Tools` 子目录后，双击会自动把上层目录识别为游戏目录并执行一次检查，也可随时点击“检查”或“修复”。

检查链路：

1. 读取上层 `config.ini` 的 `[Patch] DefaultPatch`；
2. 从 `patches/<DefaultPatch>.zip`（或同名目录）读取 `SSS.MKF`；`DefaultPatch` 为空时使用游戏根目录的 `SSS.MKF`；
3. 检查同目录严格命名的 `1.RPG` 至 `5.RPG`；
4. 比较当前补丁中应稳定的对象定义字段，并验证允许随剧情推进的脚本索引仍落在当前脚本表内。

修复只处理已经判定为污染的字段。玩家对象出现稳定字段污染时，恢复首末异常稳定字段之间的连续区段，并另外恢复所有越界脚本索引；这样可以修复精灵数据覆盖形成的“范围内错误脚本”，同时保留最后一条部分覆盖记录之外的正常剧情状态。物品、法术、敌人和毒对象只恢复应稳定的字段及越界脚本索引。每次写入前用 `File.Replace` 创建 `.bak-yyyyMMdd-HHmmss` 完整备份，写入后重新读取复核；候选结果仍不一致时不写，落盘复核失败时从备份恢复。游戏进程运行期间 GUI 拒绝修复。

构建、测试与单文件发布：

```powershell
$env:PAL98_DREAM220_RUNTIME_GAME='D:\path\to\game'
dotnet run --project .\tests\PalSaveChecker.Core.Tests\PalSaveChecker.Core.Tests.csproj -c Release
dotnet publish .\src\PalSaveChecker.WinForms\PalSaveChecker.WinForms.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None -p:DebugSymbols=false `
  -o .\artifacts\save-checker-win-x64
```

“正常”只表示没有命中本工具覆盖的对象定义/脚本索引污染，不代表对任意损坏文件作完整证明。结构不符、参考补丁缺失或候选修复无法通过复核时，工具会显示风险并拒绝自动修复。
