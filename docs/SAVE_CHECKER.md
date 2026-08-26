# 仙剑98存档检查工具

这是与存档编辑器分离的 Windows Target，发布文件名为 `仙剑98存档检查工具.exe`。部署到游戏目录的 `Tools` 子目录后，双击会自动把上层目录识别为游戏目录并执行一次检查，也可随时点击“检查”或“修复”。

检查链路：

1. 如果存在 `palmod/Profiles/current.json`，先校验 pointer、profile descriptor 及 descriptor 声明的 `WORD.DAT`/`SSS.MKF` 长度和 SHA-256，并使用 active profile staging 中的 `SSS.MKF`；`pal98.dream220.compat@1.0.18` 及其精确的 `.drawcard.<12位小写SHA前缀>` 派生家族还必须满足公开 `PAL98.PublicToolProfile.v1` 的名称、profile/save namespace、589 条文字/对象和 5,369 条事件合同；该链损坏时失败关闭，不回退到 Classic 资源；
2. 没有 active profile 时，读取上层 `config.ini` 的 `[Patch] DefaultPatch`，从 `patches/<DefaultPatch>.zip`（或同名目录）读取 `SSS.MKF`；`DefaultPatch` 为空时使用游戏根目录的 `SSS.MKF`；
3. 检查同目录严格命名的 `1.RPG` 至 `5.RPG`，先验证 Win95 固定区加当前 SSS0 事件记录区的精确总长度；
4. 仅在版本/流程布局一致后，比较当前 profile/补丁中应稳定的对象定义字段，并验证允许随剧情推进的脚本索引仍落在当前脚本表内；
5. 扫描存档事件区：对象必须处于启用状态、触发方式为接触触发 `4..8`，且入口脚本为 `0` 或对应 `SSS4` 的 8 字节记录全零，才报告“空入口接触触发”。

修复只处理已经判定为污染的字段。玩家对象出现稳定字段污染时，恢复首末异常稳定字段之间的连续区段，并另外恢复所有越界脚本索引；这样可以修复精灵数据覆盖形成的“范围内错误脚本”，同时保留最后一条部分覆盖记录之外的正常剧情状态。物品、法术、敌人和毒对象只恢复应稳定的字段及越界脚本索引。空入口接触触发只把对应 32 字节事件记录中偏移 `+14` 的触发方式改为 `0`，入口脚本、坐标、状态及其余 30 字节保持原样。界面的“保留原存档备份”默认勾选，此时每次写入用 `File.Replace` 创建 `.bak-yyyyMMdd-HHmmss` 完整备份；取消勾选后不留下备份，但在落盘复核完成前仍使用临时回滚副本。候选结果仍不一致时不写，落盘复核失败时恢复原存档。

事件区长度不一致属于“版本/流程不匹配”，不是可修复的对象字段污染。例如魂牵梦萦 1.67 PALDLL profile 要求 `184,688 = 14,064 + 5,332 × 32` 字节；原版 176,528 字节存档少 255 条事件记录。工具不会把当前 SSS 初始事件表直接附加到旧存档，因为那不能重建该存档已经推进到的剧情状态。

游戏进程运行期间也允许修复磁盘上的存档，并会在确认框和完成提示中明确警告：不要在修复过程中保存游戏；修复后应直接读入该存档。如果游戏之后再次保存同一槽位，仍可能用当前内存中的旧数据覆盖磁盘上的修复结果。

构建、测试与面向 Windows 7 SP1、Windows 10 / 11（含 Windows 11 ARM 的 x86 模拟）的 .NET Framework 4.7.2 x86 发布：

```powershell
$env:PAL98_DREAM220_RUNTIME_GAME='D:\path\to\game'
dotnet run --project .\tests\PalSaveChecker.Core.Tests\PalSaveChecker.Core.Tests.csproj -c Release -f net8.0
dotnet publish .\src\PalSaveChecker.WinForms\PalSaveChecker.WinForms.csproj `
  -c Release -f net472 -p:PlatformTarget=x86 `
  -p:DebugType=None -p:DebugSymbols=false `
  -o .\artifacts\save-checker-win7-net472
```

该发布目标不需要安装 .NET 8 Desktop Runtime。未安装 .NET Framework 4.7.2 或更高 4.x 版本的系统仍需先安装相应运行环境；发布目录中的 EXE、DLL 和 exe.config 必须一起分发。

“正常”只表示没有命中本工具覆盖的对象定义/脚本索引污染，不代表对任意损坏文件作完整证明。结构不符、参考补丁缺失或候选修复无法通过复核时，工具会显示风险并拒绝自动修复。
