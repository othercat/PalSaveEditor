# 梦幻2.2显血版公共存档合同

状态：`PAL98.PublicToolProfile.v1` / `pal98.dream220.compat@1.0.18`，以及从该精确版本生成的
`pal98.dream220.compat.drawcard.<12位小写SHA前缀>@1.0.18`。

此文件只定义 GPLv2 公共存档编辑器和检查工具安全识别该支持包所需的最小事实，不公开 PALDLL Hook、固定地址、调用约定、认证材料、原始资源或本机路径。

## 身份与署名

- 显示名：`梦幻2.2显血版`
- 别名：`Dream2.20`、`仙剑梦幻2.20`、`主播粉丝梦幻2.2显血版`
- 基底 profile / save namespace：`pal98.dream220.compat`
- PalDrawCard 派生 profile / save namespace：二者必须相同，且只能是
  `pal98.dream220.compat.drawcard.<12位小写十六进制>`；显示名必须是 `梦幻2.2显血版 + 抽卡`
- profile 版本：`1.0.18`
- 作者（按顺序）：主播粉丝、孙小柔、othercat

## 存档与资源事实

| 字段 | 值 |
|---|---:|
| 存档布局 | PAL98 / Win95 |
| 对象记录宽度 | 14 字节 |
| 存档对象槽 | 600 |
| 配套资源对象记录 | 589 |
| `WORD.DAT` | 5890 字节（589 条） |
| 事件记录 | 5369 条，每条 32 字节 |
| 存档总长度 | 185872 字节 |

工具只有在 active profile descriptor 的名称、版本、profile/save namespace、资源身份和上述布局全部一致时才允许按此 profile 写入；任一项不符都失败关闭。派生身份只复用公开存档布局事实，不授权未知前缀、非 12 位/非小写 SHA 前缀或其他版本。早期 PALDLL Dream 现场的 565 条文字表只保留为通用旧格式探测，不属于 1.0.18 的公共合同。

## 公共发行边界

- `PalSaveEditor.exe`、`仙剑98存档检查工具.exe` 及其配套 DLL 均属于本 GPL-2.0-only 项目的构建产物。
- 对外分发二进制时，应同时提供 `LICENSE` 和与该二进制对应的完整源代码；本地未提交候选不能用旧 Git commit 冒充精确源码身份。
- 发行包不得捆绑游戏资源、梦幻内容包、私有 PALDLL/ConfigTool 源码、私有 runtime contract 或本机 profile staging。
- 自动化验证只证明身份、长度、布局、备份/回滚和未知事件区保留；最终游戏内存读档仍需人工验收。
