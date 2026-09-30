<div align="center">

<a href="https://github.com/ShirokaProject/ShiroBot">
  <img src="./shirobana.webp" alt="ShiroBot" width="220" />
</a>

<p><strong><span style="font-size: 2.2em;">ShiroBot</span></strong></p>

<p><em>一个轻量的、基于 C# / .NET 10 实现的机器人框架。</em></p>

<p><a href="https://docs.shiroka.org">文档</a> · <a href="https://github.com/ShirokaProject/ShiroBot/releases">下载</a> · <a href="https://github.com/ShirokaProject/awesome-shirobot">插件与适配器</a></p>

</div>

## 快速开始

从 [Releases](https://github.com/ShirokaProject/ShiroBot/releases) 下载对应平台的安装包，解压后运行 `ShiroBot`，再放入适配器和插件即可。详见[安装与启动](https://docs.shiroka.org/guide/installation)。

或者使用 Docker：

```bash
curl -O https://raw.githubusercontent.com/ShirokaProject/ShiroBot/master/compose.yaml
docker compose up -d
```

启动后访问 Dashboard：`http://127.0.0.1:7001/dashboard/`，API 密钥见 `docker compose logs shirobot`。

## 开发插件与适配器

```bash
dotnet new install ShiroBot.Templates
dotnet new shirobot-plugin -n MyPlugin
dotnet new shirobot-adapter -n MyAdapter --platform qq
```

生成的项目自带 `dev.sh` / `dev.ps1`，一条命令即可在本地宿主中运行。详见[创建第一个插件](https://docs.shiroka.org/plugin/)与[创建适配器](https://docs.shiroka.org/adapter/)。

欢迎到 [awesome-shirobot](https://github.com/ShirokaProject/awesome-shirobot) 提交 PR 收录你的插件或适配器。

## 参与开发

仓库结构、源码构建与文档预览见[从源码构建](https://docs.shiroka.org/guide/development)。

## 许可证

本项目使用 GNU General Public License v3.0，详见 [LICENSE](./LICENSE)。
