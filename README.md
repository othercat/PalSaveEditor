# 仙剑存档编辑器（PalSaveEditor）

面向经典《仙剑奇侠传》的 Windows 桌面存档编辑器。界面信息架构参考老玩家熟悉的 PalEdit：`主要角色 / 物品 / 杂项` 三页，但解析、格式识别和安全写入均为重新实现。

## 支持范围

| 游戏 | 存档结构 | 当前支持 |
|---|---|---|
| 仙剑奇侠传 98 柔情版 | Win95，14 字节对象记录 | 读取、编辑、备份、保存 |
| 仙剑奇侠传 DOS | DOS，12 字节对象记录 | 读取、编辑、备份、保存 |
| 仙剑梦幻 2.20 | DOS 兼容结构，扩展事件区 | 读取、编辑、资源复核、备份、保存 |

梦幻 2.20 不是另一种凭空假设的 sidecar 格式。它沿用 DOS 存档固定结构，但随梦幻版 `SSS.MKF` 保存更多 32 字节事件记录。编辑器不会把普通 DOS 存档硬改成梦幻存档，也不会重建未知事件区。

## 功能

- 自动区分 DOS、Win95 和已验证的梦幻 2.20 存档；可手动复核格式。
- 读取相邻游戏目录的 `WORD.DAT` / `SSS.MKF`，自动处理 Win95 GBK 与 DOS/梦幻 Big5 名称。
- 调整 1～5 人队伍和顺序。
- 编辑六名角色的经验、等级、体力、真气、武术、灵力、防御、身法、吉运、抗毒和合体法术。
- 添加/移除法术，更换六个装备部位。
- 搜索、添加、修改或移除背包物品；写入后按游戏规则压紧背包槽。
- 编辑保存次数、场景、坐标、音乐、战斗音乐、金钱和灵葫值。
- 覆盖保存前自动在原文件旁创建 `.bak-yyyyMMdd-HHmmss` 完整备份。
- 同目录临时文件落盘后再替换目标；对象表和事件尾部逐字节保留。

## 使用

1. 先退出游戏。
2. 打开 `1.RPG`～`5.RPG`（DOS 安装也可能在 `save` 子目录）。
3. 如果物品/法术只显示编号，点击“游戏资料目录”，选择包含 `WORD.DAT` 和 `SSS.MKF` 的游戏目录。
4. 修改后点击“保存”。确认同目录已经出现带时间戳的备份，再进游戏读档验证。

编辑梦幻 2.20 时，建议直接打开梦幻目录或 `save` 目录下的存档，让程序同时用梦幻版资源复核格式。

## 构建与测试

需要 .NET 8 SDK（Windows）：

```powershell
dotnet run --project .\tests\PalSaveEditor.Core.Tests\PalSaveEditor.Core.Tests.csproj -c Release
dotnet build .\PalSaveEditor.slnx -c Release
dotnet run --project .\src\PalSaveEditor.WinForms\PalSaveEditor.WinForms.csproj -c Release
```

生成无需预装 .NET 的单文件版本：

```powershell
dotnet publish .\src\PalSaveEditor.WinForms\PalSaveEditor.WinForms.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None -p:DebugSymbols=false `
  -o .\artifacts\win-x64
```

测试程序不使用第三方测试框架，离线环境也可执行。安装了本仓库研究时使用的三类真实样本时，它还会自动运行真实格式、编码和资源边界回归；否则该部分明确显示跳过。

## 安全边界

- 程序不附带或分发游戏资源。
- 不在游戏运行中修改进程内存。
- 不猜测转换 DOS/Win95 的对象区，也不转换不同剧情版本的事件区。
- 自动化测试证明的是解析、字段写入、备份和字节保留；最终游戏内效果仍应由玩家用备份可回滚地验收。

格式证据、样本哈希和字段边界见 [docs/SAVE_FORMAT_EVIDENCE.md](docs/SAVE_FORMAT_EVIDENCE.md)。
