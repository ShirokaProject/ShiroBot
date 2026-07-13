# ShiroBot 文档站

本目录是一个独立的 VitePress 工程，包含站点配置、Markdown 内容和 Node 依赖清单。

## 本地开发

```bash
cd docs
npm install
npm run dev
```

## 构建

```bash
cd docs
npm run build
```

静态产物位于 `docs/.vitepress/dist`。

## Cloudflare Pages

连接仓库后使用以下构建配置：

| 配置 | 值 |
| --- | --- |
| Root directory | `docs` |
| Build command | `npm run build` |
| Build output directory | `.vitepress/dist` |
| Node.js | `22` |

Cloudflare Pages 的 `pages.dev` 域名和自定义域名都部署在站点根路径，不需要设置 `DOCS_BASE`。
