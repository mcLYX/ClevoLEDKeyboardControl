# Changes

本文件记录本 fork 相对上游 [xuha233/ClevoRGBControl](https://github.com/xuha233/ClevoRGBControl) 的修改，遵循 GPL-3.0 第 5(a) 条要求。

## 2026-06-10 — Fork 起点

- 基线提交：`90d2438`（"Clean repository metadata"）。
- 重命名产品为 `ClevoLEDKeyboardControl`，涉及：
  - `ColorfulLedKeyboard.Core/AppPaths.cs`（服务名、显示名、ProgramData 目录名）。
  - `ColorfulLedKeyboard.Installer/Program.cs`（产品名、安装目录、注册表键、安装包文件名、legacy 服务名兼容）。
  - `ColorfulLedKeyboard.Tray/*`（关于框、设置框标题、托盘文本、消息框标题、Restart-Service 命令）。
  - `ColorfulLedKeyboard.Tray/UpdateChecker.cs`（GitHub Releases URL、User-Agent）。
  - `ColorfulLedKeyboard.Installer/app.manifest`、安装器 `AssemblyName`。
  - `scripts/install-service.ps1`、`scripts/restart-service.ps1`、`scripts/uninstall-service.ps1`、`scripts/publish.ps1`。
  - `README.md`、`RELEASE_NOTES.txt`。
- 安装器同时识别旧服务名 `ClevoRGBControlService` 与 `ColorfulLedKeyboardService`，保证老用户升级时自动清理。
- 移除随仓库分发的厂商驱动 `assets/driver/InsydeDCHU.dll`，改为在 README 与安装提示中说明从 OEM Control Center 获取。`.gitignore` 屏蔽 `assets/driver/*.dll`，构建脚本仍可在本地放置 DLL 后打包。
- 新增 `NOTICE`、`CHANGES.md`，明确 fork 来源、修改日期与第三方组件授权状态。
- .NET 命名空间和项目目录暂时保留 `ColorfulLedKeyboard` 前缀，避免一次性大规模重命名带来的构建风险，后续重构再统一调整。