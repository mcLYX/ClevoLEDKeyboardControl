# ClevoRGBControl

ClevoRGBControl 是一个面向 Clevo 兼容机型的键盘 RGB 灯效控制程序，采用 Windows 服务 + 托盘控制的方式运行。

程序通过 `InsydeDCHU.dll` 控制键盘灯，所以需要配套的厂商 DLL。

## 功能

- Windows 服务后台运行
- 托盘菜单快速控制
- 固定颜色、RGB 循环、单色呼吸、色彩序列、音乐模式、关闭灯效
- 全局亮度、速度控制
- 音乐模式跟随系统输出音量变化
- 空闲降亮和简单时间计划
- 提供自包含的 Windows x64 安装器

## 安装

1. 从 Releases 下载 `ClevoRGBControlSetup.exe`
2. 以管理员身份运行
3. 将 `InsydeDCHU.dll` 复制到：

```text
C:\Program Files\ClevoRGBControl\Service
```

服务名称：

```text
ClevoRGBControlService
```

## 卸载

可以在 Windows 设置 > 应用中卸载，或再次运行 `ClevoRGBControlSetup.exe` 选择卸载。

命令行卸载：

```powershell
ClevoRGBControlSetup.exe /uninstall
```

## 构建

环境要求：

- Windows 10/11 x64
- .NET SDK

构建源码：

```powershell
dotnet build .\ColorfulLedKeyboard.slnx -c Release
```

生成安装器：

```powershell
.\scripts\publish.ps1
```

安装器输出：

```text
publish\ClevoRGBControlSetup.exe
```

## 致谢

本项目基于 [moshuiD/Colorful-Keyborad-Led-Color-Setting](https://github.com/moshuiD/Colorful-Keyborad-Led-Color-Setting) 的硬件控制思路。

原项目确认了使用 `InsydeDCHU.dll` 和 `SetDCHU_Data` 控制键盘灯的方式，ClevoRGBControl 在此基础上扩展为 Windows 服务、托盘程序、安装器和更多灯效。

## 许可证

GPL-3.0，见 [LICENSE](LICENSE)。
