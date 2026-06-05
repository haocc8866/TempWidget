# TempWidget 🖥️🌡️

一个 Windows 桌面小工具：实时监控 CPU / GPU 温度，常驻屏幕角落，支持横/竖版切换、磁吸、托盘菜单、开机自启。

![platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)
![.net](https://img.shields.io/badge/.NET-8.0-purple)
![license](https://img.shields.io/badge/license-MIT-green)

## 截图

- **竖版 (50×90)**：紧凑两行
- **横版 (160×50)**：带进度条

## 功能

- 🌡️ **CPU / GPU 实时温度**（基于 [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)）
- 🪟 **玻璃拟态悬浮窗** —— 圆角、半透明、Win11 风格
- 🎨 **三档报警色**：绿 (正常) → 橙 (警告) → 红 (危险)
- 🔄 **横/竖版切换** —— 托盘菜单一键切换，300ms 旋转动画
- 🧲 **边缘磁吸** —— 拖到屏幕边缘自动贴边（阈值 100 DIP），200ms 缓动
- 📌 **常驻任务栏托盘** —— 右键菜单（显示 / 切换布局 / 开机启动 / 退出）
- 🚀 **开机自启开关** —— 写 HKCU 注册表，不需要 admin
- 🔒 **单实例** —— 第二次启动自动激活已存在窗口

## 系统要求

- Windows 10 / 11 (x64)
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- **重要**: 装 [PawnIO 内核驱动](https://pawnio.eu/) 才能读到 Intel 11代+ 的 CPU 温度（Win11 默认锁了 MSR 寄存器路径）

## 从源码构建

```powershell
# 1. clone
git clone https://github.com/haocc8866/TempWidget.git
cd TempWidget

# 2. publish
dotnet publish TempWidget\TempWidget.csproj -c Release -r win-x64 -p:Platform=x64 -o publish
```

## 目录结构

```
TempWidget/
├── TempWidget/                    # 主项目
│   ├── MainWindow.xaml            # UI (含竖版 + 横版双布局)
│   ├── MainWindow.xaml.cs         # 窗口逻辑 (磁吸/动画/托盘)
│   ├── ViewModels/MainViewModel.cs
│   ├── Services/                  # 单实例 / 开机启动
│   ├── Converters/                # 颜色 / 文本转换
│   └── app.manifest               # requireAdministrator
├── LibreHardwareMonitor/          # 子模块源码 (已修改兼容 .NET 8)
│   └── LibreHardwareMonitorLib/
├── TempWidgetDump/                # 独立控制台 sensor dump 工具
├── .gitignore
└── README.md
```

## 关于 LibreHardwareMonitor

本仓库包含 LibreHardwareMonitorLib 的源码（`LibreHardwareMonitor/`），做了 3 处修改以兼容 .NET 8 SDK：

1. `LibreHardwareMonitorLib.csproj` —— 去掉 `net9.0` / `net10.0` target（避免 NETSDK1045）
2. `Sensor.cs:82` —— 把 C# 13 的 `field` 关键字改成显式 backing field
3. `Hardware/Controller/Arctic/ArcticFanController.cs:103` —— `?.Value=` 改成 if 块

`PawnIO` 驱动让 LHM 在 ring0 读 EC 寄存器，**没装的话 CPU 温度全 null**（你的 i9-11900K + Z590 + Win11 就是这种场景）。

## 许可

MIT
