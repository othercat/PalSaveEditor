# 仙剑存档编辑器（PalSaveEditor）

面向经典《仙剑奇侠传》的 Windows 桌面存档编辑器。界面信息架构参考老玩家熟悉的 PalEdit：`主要角色 / 物品 / 杂项` 三页，但解析、格式识别和安全写入均为重新实现。

## 支持范围

| 游戏 | 存档结构 | 当前支持 |
|---|---|---|
| 仙剑奇侠传 98 柔情版 | Win95，14 字节对象记录 | 读取、编辑、备份、保存 |
| 仙剑奇侠传 DOS | DOS，12 字节对象记录 | 读取、编辑、备份、保存 |
| 仙剑梦幻 2.20 原版 | DOS 兼容结构，扩展事件区 | 读取、编辑、资源复核、备份、保存 |
| 梦幻2.2显血版（PALDLL Dream 2.20） | Win95，14 字节对象记录，扩展事件区 | profile 身份复核、读取、编辑、备份、保存 |
| PALDLL 魂牵梦萦 1.67 简单/困难 | Win95，14 字节对象记录，5,332 条事件记录 | active profile 身份复核、读取、编辑、备份、保存 |

梦幻 2.20 原版沿用 DOS 存档固定结构；梦幻2.2显血版的 PALDLL 支持包则把同一批剧情资源转换到仙剑 98/Win95 对象布局。两者都随梦幻版 `SSS.MKF` 保存更多 32 字节事件记录，但固定前缀分别为 12,864 与 14,064 字节。编辑器不会在二者之间转换对象区，也不会重建未知事件区。梦幻2.2显血版作者署名为 **主播粉丝、孙小柔、othercat**。

魂牵梦萦 1.67 原资源来自 SDLPal/DOS 布局，但 PALDLL 内容 profile 已把对象记录转换为 Win95 的 14 字节布局。当前 PALDLL 存档必须是 `14,064 + 5,332 × 32 = 184,688` 字节；原版 176,528 字节存档以及 183,488 字节的 DOS/SDLPal 存档都不能直接作为该 profile 的 Windows 存档。作者为 **千年女尸的爱**。

## 功能

- 自动区分 DOS、Win95、梦幻 2.20 DOS 版和梦幻2.2显血版 Win95 移植版；可手动复核格式。
- 内置不含私有实现细节的 `PAL98.PublicToolProfile.v1` 事实：`pal98.dream220.compat@1.0.18` 及其精确
  `pal98.dream220.compat.drawcard.<12位小写SHA前缀>@1.0.18` 派生家族、589 条文字、589 条资源对象、
  5,369 条事件和 185,872 字节存档；名称、profile/save namespace 或布局不一致时失败关闭。
- 读取相邻游戏目录的 `WORD.DAT` / `SSS.MKF`；PALDLL 启用内容配置档时，会先沿 `palmod/Profiles/current.json` 读取当前 staging 的 `resources`。自动处理 Win95 GBK 与 DOS/梦幻 Big5 名称。
- 调整正式队员和顺序，并可添加、修改、移除或排序最多 2 名随从；正式队员与随从按原版结构共享 5 条队列记录。随从直接填写 MGO 形象编号，内置天鬼皇（12）和云姨（81）常用项。
- 编辑六名角色的经验、等级、体力、真气、武术、灵力、防御、身法、吉运、抗毒、风雷水火土五系抗性、头像、战斗/地图形象、行走帧上界和合体法术；六种抗性按游戏原始有符号 16 位字段处理。
- 添加/移除法术，更换六个装备部位。
- 搜索、添加、修改或移除背包物品；写入后按游戏规则压紧背包槽。
- 编辑保存次数、场景、坐标、音乐、战斗音乐、金钱和灵葫值。
- 工具栏“保留原存档备份”默认勾选；覆盖保存时在原文件旁创建 `.bak-yyyyMMdd-HHmmss`，取消后不保留备份。
- 同目录临时文件落盘后再替换目标并复核；取消备份时仍使用临时回滚副本，成功后删除。对象表和事件尾部逐字节保留。

## 使用

1. 先退出游戏。
2. 打开 `1.RPG`～`5.RPG`（DOS 安装也可能在 `save` 子目录）。
3. 如果物品/法术只显示编号，点击“游戏资料目录”，选择包含 `WORD.DAT` 和 `SSS.MKF` 的游戏目录。
4. 修改后点击“保存”。默认确认同目录已经出现带时间戳的备份；若主动取消“保留原存档备份”，则确认保存成功后再进游戏读档验证。

例如要制作“盖罗娇、血色蛇形灵儿、李逍遥，随从天鬼皇”的测试档：先在左侧把正式队员排为对应三个角色，再添加“天鬼皇（MGO 12）”；选中灵儿，将战斗形象、地图形象和行走帧上界分别设为 `5`、`512`、`3`。这些数值来自当前测试样本，其他内容包仍应按其配套资源复核。

编辑梦幻 2.20 时，建议直接打开梦幻目录或 `save` 目录下的存档，让程序同时用对象记录宽度和梦幻版事件区复核具体布局。梦幻2.2显血版不要手动选择“DOS 兼容布局”。

编辑 PALDLL 内容 profile（包括魂牵梦萦 1.67）时，直接从已激活 profile 的游戏根目录打开存档。程序优先读取并校验 `palmod/Profiles/current.json`、descriptor 及其 `WORD.DAT`/`SSS.MKF` 身份；指针损坏、资源哈希不符或事件记录数不一致时失败关闭，不回退到根目录 Classic 资源。切换剧情 profile 不能自动迁移已经推进的事件状态。

## 构建与测试

开发和构建需要 .NET 8 SDK（Windows）。测试程序同时覆盖 .NET 8 与 .NET Framework 4.7.2：

```powershell
dotnet run --project .\tests\PalSaveEditor.Core.Tests\PalSaveEditor.Core.Tests.csproj -c Release -f net8.0
dotnet build .\PalSaveEditor.slnx -c Release
.\tests\PalSaveEditor.Core.Tests\bin\Release\net472\PalSaveEditor.Core.Tests.exe
```

面向 Windows 7 SP1、Windows 10、Windows 11，以及 Windows 11 ARM x86 模拟环境的正式发布目标是 .NET Framework 4.7.2 x86。它不需要安装 .NET 8 Desktop Runtime；未安装 .NET Framework 4.7.2 或更高 4.x 版本的系统仍需先安装相应运行环境。发布目录中的 EXE、DLL 和 exe.config 必须一起分发：

```powershell
dotnet publish .\src\PalSaveEditor.WinForms\PalSaveEditor.WinForms.csproj `
  -c Release -f net472 -p:PlatformTarget=x86 `
  -p:DebugType=None -p:DebugSymbols=false `
  -o .\artifacts\win7-net472
```

如需开发期对照，也可运行 .NET 8 目标；该目标不是当前玩家覆盖包的发布格式：

```powershell
dotnet run --project .\src\PalSaveEditor.WinForms\PalSaveEditor.WinForms.csproj `
  -c Release -f net8.0-windows
```

测试程序不使用第三方测试框架，离线环境也可执行。安装了本仓库研究时使用的三类真实样本时，它还会自动运行真实格式、编码和资源边界回归；否则该部分明确显示跳过。

## 安全边界

- 程序不附带或分发游戏资源。
- 不在游戏运行中修改进程内存。
- 不猜测转换 DOS/Win95 的对象区，也不转换不同剧情版本的事件区。
- active profile 存在时，保存前必须同时满足 descriptor 身份、对象记录宽度和精确事件记录数；其他版本存档只读识别为不兼容，不允许靠补齐初始事件表伪造剧情进度。
- 正式队员和随从写入作为一次共享队列事务执行；超出 5 条总容量或 2 名随从上限时拒绝写入，队伍人数变化时完整迁移随从的 10 字节记录。
- 自动化测试证明的是解析、字段写入、可选备份、临时回滚和字节保留；最终游戏内效果仍应由玩家自行验收。重要存档建议保留默认备份。

格式证据、样本哈希和字段边界见 [docs/SAVE_FORMAT_EVIDENCE.md](docs/SAVE_FORMAT_EVIDENCE.md)；梦幻2.2显血版向公共工具开放的最小合同及发行边界见 [docs/DREAM220_VISIBLE_PUBLIC_PROFILE.md](docs/DREAM220_VISIBLE_PUBLIC_PROFILE.md)。

## 许可证

本项目以 [GNU General Public License version 2](LICENSE) 发布，SPDX 标识为 `GPL-2.0-only`。
