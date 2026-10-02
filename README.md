<div align="center">

<a href="https://github.com/ShirokaProject/ShiroBot">
  <img src="./.github/assets/shirobana.webp" alt="ShiroBot" width="220" />
</a>

<p><strong><span style="font-size: 2.2em;">ShiroBot</span></strong></p>

<p><em>一个轻量的、基于 C# / .NET 10 实现的机器人框架。</em></p>

<p>🚧 <strong>项目仍在积极开发中</strong> · 在 <code>1.0</code> 正式发布前，不保证插件 ABI 的向后兼容性,如需使用请保证插件和本体始终保持最新。</p>

<p><a href="https://docs.shiroka.org">文档</a> · <a href="https://github.com/ShirokaProject/ShiroBot/releases">下载</a> · <a href="https://github.com/ShirokaProject/awesome-shirobot">插件与适配器</a></p>

</div>

快速开始

从 [Releases](https://github.com/ShirokaProject/ShiroBot/releases) 下载对应平台的安装包，解压后运行 `ShiroBot`，再放入适配器和插件即可。详见[安装与启动](https://docs.shiroka.org/guide/installation)。

或者使用 Docker：

```bash
curl -O https://raw.githubusercontent.com/ShirokaProject/ShiroBot/master/compose.yaml
docker compose up -d
```

启动后访问 Web控制面板 ：`http://127.0.0.1:7001/dashboard/`，密钥见启动日志。

## 开发插件与适配器

参考[文档](https://docs.shiroka.org/guide/development)

欢迎到 [awesome-shirobot](https://github.com/ShirokaProject/awesome-shirobot) 提交 PR 收录你的插件或适配器。

## 参与开发

仓库结构、源码构建与文档预览见[从源码构建](https://docs.shiroka.org/guide/development)。

## 交流群

🌸 **白花交流群** `(569448734)` ૮ ˶ᵔ ᵕ ᵔ˶ ა

有任何问题都欢迎来群里一起交流～  
不管是提问、讨论，还是闲聊都可以的！(｡･ω･｡)ﾉ♡

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="./.github/assets/qq-group-dark.webp" />
  <img src="./.github/assets/qq-group-light.webp" alt="白花交流群 QQ 群二维码，群号 569448734" width="240" />
</picture>

欢迎加入我们～ ✧*｡٩(ˊᗜˋ*)و✧*｡

## 许可证

本项目使用 GNU General Public License v3.0，详见 [LICENSE](./LICENSE)。
