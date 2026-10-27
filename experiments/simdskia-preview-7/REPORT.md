# SimdSkia 0.1.0-preview.7 替换实验

日期：2026-10-27。环境：Apple M4、macOS 27.0.1、arm64、.NET SDK 10.0.402。宿主基线：`d2233a7`，产品版本 1.0.2 / SDK ABI 1.3.0.0。

## 结论

**当前不建议替换正式宿主的 Avalonia/Skia 后端。** 默认接入不能运行已有卡片；提供静态字体、保留 Skia 供旧插件调用之后能出图，但画质、字体回退、局部布局与性能仍有差异。正式仓库未修改、未提交、未推送。

该包不是原版 libSkiaSharp 的同名二进制替代品。它注册另一套 Avalonia 绘图、字体与文本排版服务。ShiroBot 是 headless 宿主，所以实验通过 `UseSimdSkia()` 配合原有 `UseHeadless(UseHeadlessDrawing=false)` 接入，没有改用桌面窗口宿主；安装的是指定的 `SimdSkia.Avalonia.Desktop 0.1.0-preview.7` NuGet 包，而不是克隆仓库 HEAD 源码构建。

## 对照组

每组包含 21 种实际插件卡片控件 × 明/暗两种主题 = 42 个用例，192 DPI。成功用例连续渲染 3 次，取后两次耗时均值作为暖机后的样本。卡片与模型类型来自下载的原始发布 DLL，通过插件里的 AXAML 编译代码构造；固定文字、图片及模型集合，仅额外的测试探针插件负责注入数据，不改插件卡片源码。

| 组 | 卡片成功生成 | 观察 |
| --- | ---: | --- |
| 当前原版 Avalonia 12.1.2（保留现有混合补丁依赖） | 42/42 | 基线 |
| 原版 Avalonia 统一 12.1.4 | 42/42 | 与当前原版的 42 张 PNG 逐像素一致 |
| SimdSkia / 无 Skia / 默认系统字体 | 0/42 | MyParser 12 种控件无法加载（24 用例）；其余 18 用例因字体合成异常失败 |
| SimdSkia / 保留 Skia 兼容插件 / 默认系统字体 | 0/42 | 全部 11 个插件可加载；42 用例仍因粗体合成异常失败 |
| SimdSkia / 保留 Skia / 内嵌静态 Noto 字体 | 42/42 | 只表示能够生成 PNG，不表示画质或性能通过 |
| 原版 Skia / 相同静态 Noto 字体 | 42/42 | 排除字体选择变化的对照 |

## 已复现问题

1. **默认字体路径失败。** macOS 下原版选择 PingFang SC；SimdSkia 只发现部分静态 TrueType 字体，不能使用同样的系统字体路径。实际卡片粗体触发 `System.NotSupportedException: Synthetic bold/italic font outlines are not implemented.`。保留 Skia 包不能修复这个异常，因为实际绘制服务仍是 SimdSkia。
2. **MyParser 依赖原版 Skia。** 发布版在 `MediaBitmapDecoder` / `AvatarBitmapResizer` 中直接使用 `SkiaSharp.SKCodec`、`SKBitmap`、`SKSurface`。删除 Avalonia.Skia/SkiaSharp 后该插件加载失败；重新保留 Skia 后可加载，真实发布 DLL 的 JPEG 解码方法也能执行。因此目前无法做到所有插件不变且彻底移除 Skia 原生依赖。
3. **彩色 emoji 缺字。** `中文 ABC 123 ❤️ 😀` 基础图片中，原版绘出彩色笑脸，SimdSkia 绘成缺字方框。两张原图位于 `results/original/environment.png` 与 `results/simd/environment.png`。
4. **相同字体仍存在布局与像素差异。** 42 张同字体图片均不是逐像素一致。RSS GitEventCard 的亮/暗两张图原版为 1840×576，SimdSkia 为 1840×574；另外 40 张尺寸一致。字形栅格化、透明边缘、圆角/阴影等有差别，需要逐张审阅；RMSE 不是视觉验收门槛。

## 字体诊断方式

从 Google Fonts 官方 Noto Sans SC 可变字体生成只覆盖实验文本的静态子集，提供 400/500/600/700/800/900 六个真实字重，通过同一 `EmbeddedFontCollection` 注册给两个后端；没有修改系统字体目录，也没有通过把所有粗体改为普通体来绕过失败。原始 OFL 许可证保存在 `fixtures/fonts/OFL.txt`。这是额外的部署改动，不是默认替换即可得到的效果；子集只适合本次固定样本，不可直接用于生产用户内容。

## 性能样本

下表使用同字体、相同数据和同一宿主渲染路径的浅色卡片，包含控件创建、布局、RenderTargetBitmap 与 PNG 编码。宿主为 Debug 构建；这是一台机器上的少量暖机样本，不能当作多平台或长时间基准。42 对用例的“Simd/原版耗时比”中位数约 **2.15×**，部分简单卡片更快，但模糊/阴影密集卡片明显变慢。没有做可靠的跨进程常驻内存对照，因此不宣称节省内存。

| 卡片 | 原版 ms | Simd ms | Simd / 原版 |
| --- | ---: | ---: | ---: |
| github-view / DescriptionCard | 25.5 | 31.2 | 1.22× |
| jm-parser / PreviewCard | 50.7 | 328.1 | 6.47× |
| my-parser / BiliLiveCard | 35.0 | 685.9 | 19.61× |
| my-parser / DouyinCommentCard | 34.9 | 20.3 | 0.58× |
| my-parser / NetEaseMusicCard | 11.8 | 173.6 | 14.67× |
| my-parser / XCard | 18.5 | 12.6 | 0.68× |
| rss-subscriber / GitEventCard | 26.1 | 559.7 | 21.46× |
| rss-subscriber / PostCard | 83.3 | 1795.2 | 21.54× |
| my-parser / HeyboxCard | 40.3 | 988.1 | 24.51× |
| my-parser / HeyboxArticleDocument | 139.5 | 4053.7 | 29.06× |

## DebugAdapter 真实消息流程

除探针触发实际卡片控件外，还通过 DebugAdapter 对真实插件发出：

- `#rss add http://127.0.0.1:18199/feed.xml renderfixture`、`#rss latest renderfixture n=1`：本地固定 RSS/图片响应，经过真实拉取、卡片选择、渲染及 QQ Mock 发送。当前原版、同字体原版、同字体 Simd 三组都拿到最终 PNG。
- 群消息 `https://github.com/ShirokaProject/ShiroBot`：经过真实 GitHubView 插件查询与渲染，三组都回复图片。该路径使用在线 GitHub 数据，因而不作为严格固定数据的像素基线。
- `#shiro`：真实文本状态插件在三种后端能回复。
- `#lab decode`：反射调用发布版 MyParser 的 `DecodeImageFileForRender`，用同一 JPEG 验证插件本身对 Skia 的依赖；保留 Skia 的组成功，纯 Simd 组因插件未加载而无法调用。

DebugAdapter 默认只保留上传流摘要。实验副本增加了受 `RENDER_LAB_OUTPUT` 控制的 seekable PNG 流捕获，读取后恢复流位置，以便保存 RSS QQ 模拟上传的实际图片。这个改动仅在实验目录的适配器源码里。

本机 .NET `Process.Modules` 没有可靠列出动态加载的 dylib，记录中的 `modules: []` 不能解释为没有加载 Skia；兼容组的明确证据是包依赖保留、MyParser 原版 Skia 调用成功以及真实源码调用链。

## 插件范围

下载整个当前市场清单的 10 个最新发布包，另从源码构建无公开 Release 的 ShiroStatus，总计 11 个插件。下载 SHA-256、Release 元数据和源码提交均保留。这里只验证插件加载及与渲染有关的路径；不宣称已测完 AI 服务、WebDAV、视频转码、JM 外部服务或真实平台连接。

| 插件 | 版本 | 渲染覆盖 |
| --- | --- | --- |
| ai-chat | v1.4.1 | 加载检查；无 Avalonia 卡片，不等于完整业务验证 |
| file-link | v1.2.2 | 加载检查；无 Avalonia 卡片，不等于完整业务验证 |
| github-view | v1.1.1 | 1 种卡片、明暗主题 + 真实群链接 |
| jm-parser | v1.3.4 | 1 种卡片、明暗主题；固定模型，不请求外部内容 |
| my-list | v1.4.2 | 加载检查；无 Avalonia 卡片，不等于完整业务验证 |
| my-parser | v0.6.15 | 12 种卡片、明暗主题 + JPEG 解码 |
| onebot-server | v0.2.3 | 加载检查；无 Avalonia 卡片，不等于完整业务验证 |
| rss-subscriber | v0.2.2 | 7 种卡片、明暗主题 + 本地 RSS 真实消息流程 |
| video-previewer | v1.1.0 | 加载检查；无 Avalonia 卡片，不等于完整业务验证 |
| video-tool | v0.1.3 | 加载检查；无 Avalonia 卡片，不等于完整业务验证 |
| shiro-status | 源码 0.1.0 | 源码构建 + #shiro 文本回复 |

MyList 因 DebugAdapter 不提供真实 QQ 群文件服务而跳过其完整业务初始化，仍可发现/加载；这是模拟能力限制。AIChat 未提供 API key，本次没有验证真实模型调用。所有配置均在实验目录创建，未复制生产配置或密钥；适配器仅监听回环地址，自动更新关闭。

## 查看与复现

- 打开 `report.html`：切换卡片、明暗主题、左右后端；可看叠加对比、差异图、完整失败堆栈以及真实插件输出。
- `comparison-sheet.png`：代表性卡片缩略对比。
- `comparison.json`：全部像素/尺寸/耗时数据；不同尺寸的两张不硬算 RMSE。
- `results/<组>/`：PNG、每用例耗时/分配/工作集快照、插件列表、错误 JSON、DebugAdapter 记录。`real-plugin-smoke/` 为真实插件处理结果。
- `downloads-manifest.json` / `downloads/*/release.json` / `environment.json`：版本与哈希；市场源码记录在 `plugins-manifest.json`。
- `integration.patch`：实验宿主改动，需同时保留 `host/Core/LabFonts` 字体文件。宿主副本是本地 clone，原仓库没有改动。
- `scripts/prepare-fixtures.py` 创建初始 fixture/控件清单（目前 `cards.json` 额外补入 2 个没有 x:DataType 的 Heybox 控件）；`prepare-fonts.py` 创建静态字体；`probe/` 是测试探针。
- 现有独立 runtime 上可执行：`.venv/bin/python scripts/run-comparison.py original skia-12.1.4 simd simd-compat simd-fonts skia-fonts`。
- 真实流程：`.venv/bin/python scripts/real-plugin-smoke.py original simd-fonts skia-fonts`；随后运行 `scripts/analyze.py`、`scripts/create-report.py`。
- 所有启动的测试宿主和本地 RSS 服务均已停止。重新运行请确认 18101–18106、18188–18193、18199 端口空闲。

## 上游资料

- https://www.nuget.org/packages/SimdSkia.Avalonia.Desktop/0.1.0-preview.7
- https://github.com/hnlyf168/SimdSkia （固定 Avalonia 12.1.4；声明字体/整页/API 的兼容缺口；UseSimdSkia 不是同名强名称 SkiaSharp 程序集替换）
- `SimdSkia/src/SimdSkia.Avalonia/Fonts.cs`：字体合成与字体格式限制。源码副本仅用于阅读；实际运行包来源提交为 1490f164084c24c1bd3f3dbaf727d7598a14abee，克隆 HEAD 另记在 environment.json。

## 追加验证：显式加入 Emoji 字体

使用同一 NuGet preview.7，显式注册 Arial.ttf 与系统 Apple Color Emoji.ttc，后端初始化直接抛出：

```text
System.NotSupportedException: Color/bitmap/SVG font rendering requires its own color glyph pipeline.
```

因此 Apple 彩色 Emoji 不能仅靠添加字体解决。复现项目在 `emoji-probe/`，完整日志在 `logs/explicit-emoji-font.log`。静态单色轮廓 Emoji 字体可能覆盖部分字符，但不能据此保证彩色外观和组合 Emoji 与原版一致。文字抗锯齿差别和 2px 高度变化在这组卡片中很轻微，单凭这些差异不足以否定视觉可用性；更主要的替换障碍仍是彩色字形支持、直接 Skia 依赖以及复杂卡片耗时。
