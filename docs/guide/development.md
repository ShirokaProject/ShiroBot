# 从源码构建

本页面向参与 ShiroBot 宿主开发的贡献者。只开发插件或适配器时不需要构建宿主，参见[创建第一个插件](/plugin/)与[创建适配器](/adapter/)。

## 仓库结构

- `Core`：ShiroBot 宿主本体，内置 Avalonia Headless 渲染集成。
- `SDK`：插件与适配器 SDK，内置 Avalonia 控件渲染契约和编译支持。
- `Models`：Discord、QQ、Telegram 平台契约，随宿主内置。
- `Templates`：`dotnet new` 插件与适配器项目模板。
- `Packaging`：`ShiroBot.SDK` NuGet 包。
- `Tests`：主仓库验证与共享契约探针。
- `docs`：本文档站（VitePress）。
- Dashboard：宿主 Web 面板前端，位于独立仓库 [Shirobot.Dashboard](https://github.com/ShirokaProject/Shirobot.Dashboard)。

NuGet 包版本由根目录 `Directory.Packages.props` 统一管理，项目文件只声明包引用。宿主、SDK 与 ABI 版本在 `Directory.Build.props` 中维护，对应关系见 [API 兼容性](/plugin/api-compatibility)。

插件和适配器在各自的独立仓库中开发，不纳入主仓库。宿主不扫描独立的 `models/` 目录，也不支持运行时安装第三方 Model；第三方平台能力应实现为适配器或插件。

## 构建与验证

需要 .NET SDK 10：

```bash
dotnet build Shirobot.slnx
dotnet run --project Tests/Verification/ShiroBot.Verification.csproj
```

构建宿主时会从 Shirobot.Dashboard 的 Release 下载 `ShirobotDashboardVersion` 对应的前端成品并嵌入程序集，不需要 Node.js。离线或该版本尚未发布时构建仍会成功，但 `/dashboard` 不可用；发布流程使用 `-p:RequireDashboard=true` 强制校验。联调未发布的前端改动时使用 `-p:DashboardDistPath=<本地 dist 目录>`。

宿主保持 `PublishTrimmed=false`：插件发现、配置 schema 和程序集加载依赖反射与 metadata，目前不具备安全裁剪条件。镜像与发布包也因此没有启用 NativeAOT。

## 本地构建镜像

```bash
docker build -t shirobot:local .
```

## 文档

```bash
cd docs
npm install
npm run dev     # 本地预览
npm run build   # 构建静态站点
```

## 发布

推送 `v*` tag 后，GitHub Actions 会发布各平台安装包、NuGet 包与 Docker 镜像。在 `.github/release-notes/<tag>.md` 中编写的内容会作为该版本 Release 的更新日志。
