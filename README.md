# AE2 for Terraria

把物品查询、ME 网络存储与自动合成带进 Terraria 的 tModLoader 模组。玩家可以查询物品的配方、用途与掉落，集中存取物品，在合成终端管理制作任务和保持库存，并查看网络权限与状态。

当前源码版本为 **0.27.11**，唯一版本来源是 [build.txt](AE2ForTerraria/build.txt)。本仓库用于私有版本管理，当前提供源码，未建立公开下载或自动发布流程。

## 主要功能

| 模块 | 可以做什么 |
| --- | --- |
| 物品与怪物查询 | 名称、拼音、模组与图标分类；盔甲组件套装说明、配方组通用名称、NPC 与掉落；十条持久历史、Alt 收藏与可拖动的库存材料树 |
| ME 存储 | 驱动器与磁盘、固定容量存储单元、分区过滤、词缀和自定义物品身份 |
| 旧访问终端（隐藏） | 保留旧物品、方块及远程绑定，不再提供新制作和研究入口 |
| 合成终端 | 同窗存取与合成，内置任务、工作站、缺料分析和保持库存；新网络无需额外 CPU 或装配室 |
| 网络管理 | 线缆、可启停的无线桥接、命名、权限、物品锁定、审计与网络转移 |
| 自动收集 | 掉落收集器、可配置的随身磁铁、原版箱子导入与导出 |

存储总线仅支持原版箱子系统，这是既定设计。查询功能以 Recipe Browser 为对照目标，尚未完成全功能对等验收。特殊合成回调可能要求玩家手动制作；不会通过忽略其规则来自动合成。

## 文档导航

| 文档 | 阅读目的 |
| --- | --- |
| [玩家指南](AE2ForTerraria/README.md) | 初次搭建、设备用途、容量、按键和配置 |
| [需求与验收](docs/requirements.md) | 功能边界、应满足的行为及验收场景 |
| [最早获取来源核对](docs/acquisition-sources.md) | 自然采集、掉落、商店、矿物、微光与提炼来源的证据和判定方法 |
| [设计说明](docs/design.md) | 模块分工、数据保存、接口与操作流程 |
| [维护手册](docs/maintenance.md) | 构建安装、测试、版本同步和故障排查 |
| [项目记忆](docs/project-memory.md) | 已确认决策、历史坑、当前状态和后续重点 |
| [Recipe Browser 功能对照](ui-preview/recipe-browser/FEATURE-PARITY.md) | 逐项对照范围与历史预览边界 |

`docs/ARCHITECTURE-0.22.md`、`docs/UI-0.23.0.md` 等带版本号的文件是历史记录；当前架构以设计说明和源码为准。文档随对应功能、接口与验证结果更新。

## 环境与依赖

- 使用 Terraria 的 tModLoader 分支，不适用于原版 Terraria 直接加载。
- 本轮核对的历史验证环境：Terraria 1.4.4.9、tModLoader 2026.08.3.0、Windows、.NET 8。
- 模组项目目标为 `net8.0`，使用 C# 12，并依赖安装目录中的 `tMLMod.targets`。
- 独立历史测试 harness 使用 `net9.0`，仅运行这些 harness 时才额外需要相应 SDK。
- Calamity、Fargo、BossChecklist 等属于实际测试模组包或可选数据来源，不是本模组 `build.txt` 声明的强制依赖。

已验证的版本组合不是对其他 tModLoader 版本的兼容承诺。开发与验收必须使用游戏实际启用模组列表，不能用固定数量替代列表核对。

## 安装与启动

### 已有模组包

1. 启动 tModLoader，在模组菜单中打开实际使用的 `Mods` 文件夹，确认它属于本次游戏使用的存档位置。
2. 将可信的 `AE2ForTerraria.tmod` 直接放入打开的 `Mods` 文件夹。
3. 在模组管理中启用 **AE2 for Terraria**，重新加载模组；覆盖更新包后重启正在运行的游戏。
4. 进入世界，按默认 **Tab** 打开物品浏览器；设备搭建见[玩家指南](AE2ForTerraria/README.md)。

多人游戏中，服务器和客户端均须使用一致的模组版本；当前源码协议版本为 \(9\)，不应混用旧客户端。

### 从源码构建

获得私有仓库访问权后，克隆仓库并在仓库根目录操作。

```powershell
git clone https://github.com/S4kuzy0/AE2ForTerraria.git
Set-Location AE2ForTerraria
```

项目文件目前写死开发机器的 `tMLMod.targets` 导入位置。换机器后，先将 `AE2ForTerraria/AE2ForTerraria.csproj` 中的 `Import Project` 指向自己的 tModLoader 安装目录；输入下面的安装路径不会自动修改该导入位置。

```powershell
$TmlInstall = Read-Host '请输入 tModLoader 安装目录的完整路径'
$ModSource = (Resolve-Path -LiteralPath './AE2ForTerraria').Path
$SaveDirectory = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'My Games\Terraria\tModLoader'

if (!(Test-Path -LiteralPath (Join-Path $TmlInstall 'tModLoader.dll'))) {
    throw '未找到 tModLoader.dll，请检查安装路径'
}
Push-Location $TmlInstall
try {
    & (Join-Path $TmlInstall 'dotnet\dotnet.exe') (Join-Path $TmlInstall 'tModLoader.dll') -build $ModSource -tmlsavedirectory $SaveDirectory -nosteam
    if ($LASTEXITCODE -ne 0) { throw '构建失败，请查看构建输出' }
} finally {
    Pop-Location
}
Get-Item -LiteralPath (Join-Path $SaveDirectory 'Mods\AE2ForTerraria.tmod') | Select-Object Length, LastWriteTime
```

这里使用 Windows 默认文档存档位置；若游戏使用自定义存档目录，必须把 `$SaveDirectory` 改成游戏的实际位置。构建会直接覆盖该目录中的安装包，先保护已有包和存档。`-build` 必须接源码完整路径，运行目录必须是 tModLoader 安装目录。上述构建允许 `-nosteam`；实际模组包的加载与游戏验证必须保留 Steam 创意工坊发现。

构建后，按“已有模组包”步骤启用并启动。具体测试与故障处理见[维护手册](docs/maintenance.md)。

## 验证状态与版本管理

0.27.5 为已核验可合成物品添加左上角加号；掉落收集器只保留开关、可输入半径和黑白名单模式三个顶部控件，名单改为可搜索物品网格。半径超过 \(256\) 格显示无限并收集全世界现存掉落物，过滤、拾取延迟和容量规则保留。磁盘仍按完整身份分别占类，存档字段和通信格式保持不变。具体场景、结果和验证边界见[维护手册](docs/maintenance.md#已知验证基线)。

完成项目更新后，复查差异和上传范围，建立有实际内容的提交并推送私有仓库。文档维护同步提交；纯文档变更不递增模组版本、不重新覆盖安装包。同步工具为 [sync_git.ps1](tools/sync_git.ps1)，推送前检查仓库仍为私有，并核对远程提交。

源码、必要资源、通用文档、工具及筛选后的测试输入纳入 Git。存档、个人配置、助手指令文件、备份、构建产物、测试结果、提取的游戏预览素材和本地凭据保留本机，受 `.gitignore` 与上传检查保护。新增通用说明使用变量表示安装位置；既有项目与脚本中的固定路径仍待参数化。

第三方来源与授权声明见 [ThirdPartyNotices.txt](AE2ForTerraria/ThirdPartyNotices.txt)。项目目前没有指定自身的开源许可证。

0.27.6 将同种物品的全部词缀、自定义数据共计一个类别，并共用该类型的数量额度；真实身份独立保存和取出。排序增加正倒序，获取阶段采用分组标题与悬停依据，排序菜单可打开个人 Boss 顺序编辑页。加号改为细线。当前依据及验证范围见维护手册。

0.27.8 补充直接钓鱼、对应生物群系宝匣和渔夫任务来源，蛙腿按 Boss 前钓获判定；困难模式和地牢门槛仍保留。来源核对方法见[专题](docs/acquisition-sources.md)。

0.27.7 将正倒序移至独立箭头，略过无新增物品或配方的阶段并保留纯掉落节点；获取阶段综合自然生成、早期宝箱及可确认容器路线，生命水晶按天然来源归入 Boss 前。

0.27.9 自查非钓鱼途径，修正粉史莱姆变体、小怪、自然生长、商店和陨石来源；直接微光转换与提炼机参与依赖传播，Boss／事件掉落保留附加条件及后期成员门槛。

0.27.10 继续核对未识别来源：适配当前 Fargo 的少数困难模式奖励条件，补陆龟与红魔鬼等来源，恢复恶魔之心的宝藏袋推导，并将材料修正传播至翅膀等配方。

0.27.11 补充小动物捕捉及白蜡木采集来源，获取阶段标题可折叠，Boss 顺序支持拖动与明确的上调／下调按钮，并按 NPC 身份排除火把神等重复推断检查点。
