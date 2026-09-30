# 安装与启动

## 选择发布包

前往 ShiroBot 的 [GitHub Releases](https://github.com/ShirokaProject/ShiroBot/releases)，按照两个维度选择压缩包：

1. 操作系统与架构：`win-x64`、`win-arm64`、`linux-x64`、`linux-arm64`、`linux-musl-x64`、`linux-musl-arm64`、`osx-x64` 或 `osx-arm64`。Alpine 等使用 musl 的 Linux 发行版选择 `linux-musl-*`。
2. 运行时类型：
   - `self-contained`：自带 .NET 运行时，推荐普通用户使用。
   - `framework-dependent`：体积更小，但系统需要预装 ASP.NET Core 10 Runtime。

::: tip 不确定怎么选？
大多数 Windows 电脑选择 `win-x64-self-contained`；Apple Silicon Mac 选择 `osx-arm64-self-contained`；常见 x64 Linux 服务器选择 `linux-x64-self-contained`；Alpine x64 服务器选择 `linux-musl-x64-self-contained`。
:::

::: warning Linux 系统依赖
图片渲染需要 fontconfig 和中文字体，例如 Debian / Ubuntu 执行 `apt install libfontconfig1 fonts-noto-cjk`。Alpine 还需要 .NET 自身的依赖：`apk add libstdc++ libgcc icu-libs fontconfig font-noto-cjk`。缺少 fontconfig 时宿主仍能启动，但渲染服务不可用。
:::

也可以直接使用 [Docker 镜像](#docker-部署)。

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
protocols = ["MyAdapter"]
```

根目录单 DLL 适配器的配置位于 `adapters/config.toml`；目录适配器的配置位于 `adapters/MyAdapter/config.toml`。多个根目录单 DLL 适配器会共用同一配置文件，建议用目录形式部署。

## 安装插件

单 DLL 插件可以直接放入 `plugins`：

```text
plugins/HelloPlugin.dll
```

目录插件的入口 DLL 放在目录顶层；目录通常使用插件 ID 命名，DLL 文件名可以不同：

```text
plugins/HelloPlugin/ShiroBot.Plugin.Hello.dll
```

宿主会检查目录顶层的 DLL 并识别插件元数据。通过 Dashboard 上传单 DLL 或 ZIP 安装时，宿主统一创建 `plugins/<插件 ID>/`，将入口 DLL 和依赖放在其中。ZIP 的入口 DLL 必须位于包根目录或唯一的顶层文件夹中。插件替换时保留原有 `config.toml`。

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

## Docker 部署

每个版本都会发布 `linux/amd64` 与 `linux/arm64` 镜像：`ghcr.io/shirokaproject/shirobot:<版本>`、`:<主.次>` 与 `:latest`。镜像已包含渲染所需的 fontconfig 与中文字体。

使用仓库根目录的 [`compose.yaml`](https://github.com/ShirokaProject/ShiroBot/blob/master/compose.yaml)：

```bash
docker compose pull
docker compose up -d
docker compose logs -f shirobot
```

- 首次启动会在 `docker-data/config.toml` 生成容器配置和 API 鉴权密钥，密钥也会打印在日志里。
- `docker-data/plugins/` 与 `docker-data/adapters/` 分别保存插件和适配器，重建容器不会删除。
- Dashboard 默认只绑定本机 `http://127.0.0.1:7001/dashboard/`。远程访问请通过反向代理，并保留 API 鉴权。
- 固定镜像版本：设置环境变量 `SHIROBOT_IMAGE_TAG=0.9.4` 后再执行 `docker compose pull` 和 `docker compose up -d`。

不使用 compose 时：

```bash
docker run -d --name shirobot --restart unless-stopped \
  -p 127.0.0.1:7001:7001 -v "$PWD/docker-data:/data" \
  ghcr.io/shirokaproject/shirobot:latest
```
