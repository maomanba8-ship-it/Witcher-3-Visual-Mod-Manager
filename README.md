# 巫师3 Mod 管理器 (Witcher 3 Visual Mod Manager)

一个为巫师3次世代版设计的桌面 Mod 管理工具，支持拖拽安装、智能冲突检测、预设管理和中英双语。

## 功能

- 🗂️ **拖拽安装** — 支持 .rar / .zip / .7z，自动识别 mods / dlc / bin 目录结构
- 🔴🟡🔵🔗 **智能冲突检测** — 文件级冲突、潜在覆盖、Bundle Mod、共享资源四种分类
- 🛠️ **脚本合并** — 一键调起 Witcher Script Merger
- 💾 **预设管理** — 保存/加载 Mod 组合
- 📦 **一键备份** — 打包所有已启用 Mod
- 🌐 **中英双语** — 运行时热切换

## 系统要求

- Windows 10/11 (x64)
- [.NET 10.0 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [7-Zip](https://www.7-zip.org/)（解压 Mod 压缩包）
- 巫师3：狂猎（次世代版）

## 构建

```powershell
cd Witcher3ModManager
dotnet build -c Release
```

发布：

```powershell
# 框架依赖版（需安装 .NET 10 Runtime）
dotnet publish -c Release -o publish_framework

# 自包含版（无需安装 .NET）
dotnet publish -c Release --self-contained true -r win-x64 -o publish_selfcontained
```

## 技术栈

.NET 10 / WPF / SQLite / 7-Zip

## 许可证

MIT License