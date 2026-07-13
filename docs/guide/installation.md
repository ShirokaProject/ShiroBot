# 安装与启动

## 选择发布包

前往 ShiroBot 的 [GitHub Releases](https://github.com/ShirokaProject/ShiroBot/releases)，按照三个维度选择压缩包：

1. 操作系统与架构：`win-x64`、`win-arm64`、`linux-x64`、`linux-arm64`、`osx-x64` 或 `osx-arm64`。
2. 功能版本：需要 Avalonia 渲染选 `full`，否则选 `lite`。
3. 运行时类型：
   - `self-contained`：自带 .NET 运行时，推荐普通用户使用。
   - `framework-dependent`：体积更小，但系统需要预装 .NET 10 Runtime。

::: tip 不确定怎么选？
大多数 Windows 电脑选择 `win-x64-full-self-contained`；Apple Silicon Mac 选择 `osx-arm64-full-self-contained`；常见 x64 Linux 服务器选择 `linux-x64-lite-self-contained`。
:::

## 准备目录

解压后建议保持以下结构：

```text
ShiroBot/
├── ShiroBot              # Linux / macOS
├── ShiroBot.exe          # Windows
├── config.toml
├── adapters/
│   └── MyAdapter/
│       ├── MyAdapter.dll
│       └── config.toml
└── plugins/
    ├── HelloPlugin.dll
    └── OtherPlugin/
        └── OtherPlugin.dll
```

`config.toml`、`adapters` 和 `plugins` 不存在时，宿主会按启动进度自动创建。没有适配器时宿主无法连接机器人协议。

## 安装适配器

从 [awesome-shirobot](https://github.com/ShirokaProject/awesome-shirobot) 选择与你的机器人实现端匹配的适配器。

适配器支持两种放置方式：

```text
adapters/MyAdapter.dll
```

或带依赖的目录形式：

```text
adapters/MyAdapter/MyAdapter.dll
adapters/MyAdapter/其他依赖文件
```

然后在 `config.toml` 中设置：

```toml
protocol = "MyAdapter"
```

根目录单 DLL 适配器的配置位于 `adapters/MyAdapter.toml`；目录适配器的配置位于 `adapters/MyAdapter/config.toml`。

## 安装插件

单 DLL 插件可以直接放入 `plugins`：

```text
plugins/HelloPlugin.dll
```

目录插件的入口 DLL 名称应与目录一致：

```text
plugins/HelloPlugin/HelloPlugin.dll
```

插件内如果包含 native NuGet 依赖清单，首次加载时宿主会联网下载当前平台所需的 native 文件。下载结果缓存在插件目录下的 `.shirobot/native`。

## 第一次启动

Windows：

```powershell
.\ShiroBot.exe
```

Linux / macOS：

```bash
chmod +x ./ShiroBot
./ShiroBot
```

framework-dependent 发布包仍然直接运行 `ShiroBot` / `ShiroBot.exe`，只是启动时会使用系统已经安装的 .NET 10 Runtime。

首次启动后检查以下内容：

- `config.toml` 已生成并填写 `protocol`、所有者账号等信息。
- 适配器自己的 TOML 配置已经填写。
- 日志中出现“加载适配器成功”。
- 日志中出现插件加载列表。

## 命令行参数

```text
--config, -c <path>    指定核心配置文件
--adapter <path>       指定适配器 DLL
--plugin-dir <path>    指定插件目录
--no-console           禁用控制台交互输入
```

例如：

```bash
./ShiroBot \
  --config /etc/shirobot/config.toml \
  --adapter /opt/shirobot/adapters/MyAdapter/MyAdapter.dll \
  --plugin-dir /opt/shirobot/plugins \
  --no-console
```

服务化部署时建议使用绝对路径，并通过 systemd、Docker 或进程守护器管理宿主生命周期。
