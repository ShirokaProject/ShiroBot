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

解压后，安装组件并运行宿主时，常见目录结构如下。主程序按平台选择 `ShiroBot` 或 `ShiroBot.exe`，并非同时包含两者；运行时目录会按需生成。

```text
ShiroBot/
├── ShiroBot / ShiroBot.exe   # Linux、macOS / Windows 主程序
├── config.toml              # 宿主配置、API 令牌、权限及插件路由
├── adapters/
│   └── MyAdapter/            # 适配器包，同一包可配置多个实例
│       ├── MyAdapter.dll
│       └── config.toml       # 实例清单及各实例的连接配置
├── plugins/
│   └── HelloPlugin/          # Dashboard 安装时按插件 ID 创建
│       ├── HelloPlugin.dll
│       ├── config.toml       # 插件提供配置时生成
│       ├── data/             # 插件持久数据（具体目录由插件决定）
│       └── .shirobot/native/ # 按需下载的 native 依赖缓存
└── cache/
    ├── host.lock            # 单实例运行锁，退出后释放锁但保留文件
    ├── adapters/            # 可重建的适配器包元数据
    ├── plugin-marketplace.v1.json # 插件市场缓存，按需生成
    └── plugin-temp/         # 插件调用 CreateTempDirectory 创建的临时目录
```

`config.toml`、`adapters/`、`plugins/` 和 `cache/` 由宿主按需创建，插件内部的数据目录由插件决定。没有已配置并运行的适配器实例时，宿主仍可启动 Dashboard，但不能连接机器人协议。

配置和插件持久数据需要保留。`cache/plugin-temp/` 由宿主按保留时长清理，下次启动时也会清理遗留文件；其他缓存不采用这一清理规则。运行期间不要删除 `cache/host.lock`，否则可能破坏重复启动保护。

单 DLL 插件也可以手动放在 `plugins/HelloPlugin.dll`，宿主仍为其提供 `plugins/<插件 ID>/` 数据目录。带私有依赖或静态资源的组件可以包含其他文件，不需要另行放入宿主共享的 SDK / Model DLL 或发布用 PDB。

上述结构不要求存在 `backup/` 或 `diagnostics/`；自行保存的备份和诊断文件应单独保管。Docker 的主程序和缓存位于 `/app`，配置、适配器与插件持久数据位于挂载的数据卷 `/data`，详见[Docker 部署](#docker-部署)。

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

- `config.toml` 已生成并配置 `protocols`、所有者身份及适配器实例等信息。
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

- 数据保存在命名卷 `shirobot_data` 中，挂载到容器的 `/data`：`config.toml`、`plugins/`、`adapters/` 都在这里，重建或升级容器不会删除。`compose.yaml` 显式指定卷名 `shirobot_data`，在哪个目录启动都使用同一个卷。
- 容器以非 root 用户运行；`/app/cache` 是可写的临时缓存目录，重建容器时可以丢弃，配置和插件数据仍保存在 `/data`。
- 首次启动会生成 `/data/config.toml` 和 API 鉴权密钥，密钥也会打印在日志里。
- 插件和适配器建议在 Dashboard 中安装、配置。需要手动修改配置文件时，在容器内编辑（以容器用户身份写入，宿主保存配置时不会遇到权限问题）：

  ```bash
  docker compose exec shirobot vi /data/config.toml
  ```

- Dashboard 默认只绑定本机 `http://127.0.0.1:7001/dashboard/`。远程访问请通过反向代理，并保留 API 鉴权。
- 固定镜像版本：设置环境变量 `SHIROBOT_IMAGE_TAG=1.0.0` 后再执行 `docker compose pull` 和 `docker compose up -d`。
- 可以直接在 Dashboard 中更新主程序：新版本下载到数据卷的 `/data/host`，先在当前容器中试运行，能正常启动才会安装并重启。新版本需要更新的 .NET 运行时或系统依赖时，面板会提示执行 `docker compose pull && docker compose up -d` 更新镜像。启动时如果镜像中的主程序不旧于 `/data/host` 中的版本，会删除 `/data/host` 并使用镜像版本。

备份数据卷：

```bash
docker run --rm -v shirobot_data:/data:ro -v "$PWD":/backup alpine \
  tar czf /backup/shirobot-data.tgz -C /data .
```




不使用 compose 时：

```bash
docker run -d --name shirobot --restart unless-stopped \
  -p 127.0.0.1:7001:7001 -v shirobot-data:/data \
  ghcr.io/shirokaproject/shirobot:latest
```
