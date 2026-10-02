# 安装与启动

## 选择发布包

前往 ShiroBot 的 [GitHub Releases](https://github.com/ShirokaProject/ShiroBot/releases)，压缩包名称格式为 `shirobot-host-<平台>-<运行时类型>.zip`。

### 平台

| 系统 | x64 | ARM64 |
| --- | --- | --- |
| Windows | `win-x64` | `win-arm64` |
| macOS | `osx-x64`（Intel 芯片） | `osx-arm64`（Apple 芯片） |
| Linux | `linux-x64` | `linux-arm64` |
| Alpine 等 musl 发行版 | `linux-musl-x64` | `linux-musl-arm64` |

### 运行时类型

- `self-contained`：自带 .NET 运行时，**推荐**，下载即可运行。
- `framework-dependent`：体积更小，但需要预装 [ASP.NET Core 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)。

::: tip 不确定怎么选？
- Windows 电脑：`win-x64-self-contained`
- Apple 芯片的 Mac：`osx-arm64-self-contained`
- 常见 x64 Linux 服务器：`linux-x64-self-contained`
- Alpine x64 服务器：`linux-musl-x64-self-contained`
:::

### Linux 系统依赖

图片渲染需要 fontconfig 和中文字体。缺少 fontconfig 时宿主仍能启动，但渲染服务不可用，依赖渲染的插件会加载失败。

Debian / Ubuntu：

```bash
sudo apt install libfontconfig1 fonts-noto-cjk
```

Alpine（额外需要 .NET 自身的依赖）：

```bash
sudo apk add libstdc++ libgcc icu-libs fontconfig font-noto-cjk
```

::: tip
不想处理依赖时可以直接使用 [Docker 镜像](#docker-部署)，镜像已包含上述依赖。
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

在 Dashboard「适配器」页点击「从文件安装」上传 `.dll` / `.zip`，或在「发现」中直接安装；安装后展开适配器，点击「添加实例」并在「配置」中填写连接信息，再打开实例开关。每个实例对应一个机器人账号，同一个适配器可以添加多个实例。也可以使用 [CLI](/adapter/deployment) 完成同样的操作。

开发时想直接加载未打包的 DLL，见[配置文件](/guide/configuration#开发时加载独立适配器-dll)中的 `protocols` 与 `--adapter`。

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
--adapter <path>       额外加载未安装的适配器 DLL（开发用）
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

- 数据保存在命名卷 `shirobot_shirobot-data` 中，挂载到容器的 `/data`：`config.toml`、`plugins/`、`adapters/` 都在这里，重建或升级容器不会删除。`compose.yaml` 固定了项目名 `shirobot`，在哪个目录启动都使用同一个卷。
- 首次启动会生成 `/data/config.toml` 和 API 鉴权密钥，密钥也会打印在日志里。
- 插件和适配器建议在 Dashboard 中安装、配置。需要手动修改配置文件时，在容器内编辑（以容器用户身份写入，宿主保存配置时不会遇到权限问题）：

  ```bash
  docker compose exec shirobot vi /data/config.toml
  ```

- Dashboard 默认只绑定本机 `http://127.0.0.1:7001/dashboard/`。远程访问请通过反向代理，并保留 API 鉴权。
- 固定镜像版本：设置环境变量 `SHIROBOT_IMAGE_TAG=0.9.7` 后再执行 `docker compose pull` 和 `docker compose up -d`。

备份数据卷：

```bash
docker run --rm -v shirobot_shirobot-data:/data:ro -v "$PWD":/backup alpine \
  tar czf /backup/shirobot-data.tgz -C /data .
```

### 从 `./docker-data` 迁移

0.9.7 之前的 `compose.yaml` 把数据挂载在本机的 `./docker-data` 目录。换用新的 `compose.yaml` 后会以全新数据启动，按下面步骤把旧数据拷进数据卷：

```bash
# 在旧的部署目录中停止旧容器
docker compose down

# 下载新的 compose.yaml 并创建数据卷（不启动）
curl -O https://raw.githubusercontent.com/ShirokaProject/ShiroBot/master/compose.yaml
docker compose up --no-start

# 拷贝旧数据并交给容器用户（UID 1654）
docker run --rm -v "$PWD/docker-data:/old:ro" -v shirobot_shirobot-data:/data alpine \
  sh -c 'cp -a /old/. /data/ && chown -R 1654:1654 /data'

docker compose up -d
```

确认运行正常后，可以删除 `./docker-data`。

不使用 compose 时：

```bash
docker run -d --name shirobot --restart unless-stopped \
  -p 127.0.0.1:7001:7001 -v shirobot-data:/data \
  ghcr.io/shirokaproject/shirobot:latest
```
