# AE2 for Terraria

Terraria / tModLoader 模组：物品与怪物查询、ME 网络存储、自动合成与网络管理。

完整玩家说明见 [模组 README](AE2ForTerraria/README.md)。源码及版本号以 `AE2ForTerraria/` 为准。

## 仓库内容

- `AE2ForTerraria/`：模组源码、实际使用的资源、本地化、构建配置与第三方声明。
- `docs/`：架构与交付说明。
- `tools/`：资源检查、构建验证与版本同步工具。
- `validation/stage2/`：筛选保留的独立回归测试输入；这些历史 harness 不代表当前版本已经通过测试。
- `ui-preview/recipe-browser/FEATURE-PARITY.md`：Recipe Browser 功能对照。
- `AGENTS.md`：本项目开发、构建、验证与 Git 同步约定。

游戏存档、角色、个人配置、备份、日志、测试结果、提取的游戏预览素材与构建产物保留在本机，由 `.gitignore` 排除。模组实际需要的 PNG 和 `Assets/pinyin.dat` 属于项目输入，纳入版本控制。

## 开发与构建

模组面向 tModLoader；项目的 .NET 目标及语言版本见 `AE2ForTerraria/AE2ForTerraria.csproj`。
当前项目与部分工具仍使用开发机器的安装路径。换机器后需要配置 tModLoader 路径，不能直接假定可构建。
具体安装与实际模组环境验证流程见 `AGENTS.md`；构建产物不提交源码仓库。

## 版本同步

每次已授权更新完成后，先按项目约定构建安装和验证，再建立说明明确的 Git 提交并推送到私有仓库。
同步工具为 `tools/sync_git.ps1`；它按忽略规则收集项目内容，并在提交前检查禁止上传的文件和常见凭据格式。
推送前会确认目标仓库仍为私有。推送需要本机 GitHub 登录；当前使用 GitHub CLI 和系统凭据存储，不将凭据写入项目。
若验证存在限制，应如实记录；若推送失败，保留本地提交并报告原因，不宣称已上传。

## 第三方内容

第三方来源与授权声明见 `AE2ForTerraria/ThirdPartyNotices.txt`。
本仓库为私有版本管理仓库，目前没有为项目自身指定开源许可证。
