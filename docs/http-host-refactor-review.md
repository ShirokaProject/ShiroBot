# HTTP 宿主与依赖组装重构审阅说明

原有 `global.json` 删除和 `global.json.bak` 为此前的本地 SDK 状态，不属于本次重构。

## 主要变化

- `HostHttpServer` 从接收 15 个业务参数的静态工厂，改为 DI 构造的 `IHostedService`。启动/停止接口只接收 `CancellationToken`，启动失败会清理已经创建的 WebApplication，停止和释放可重复调用。
- 路由实际注册逻辑分到系统、插件目录、插件安装、插件管理、插件页面、组件管理、诊断模块。模块持有自己的长期依赖，不增加一层只转发原 15 参数调用的门面。插件页面测试可以直接构造只需 Options 和 PluginManager 的模块。
- `Program` 是依赖组装位置：现有进程对象使用 `AddSingleton(instance)` 注册，为借用而不是转移所有权；容器负责新建的 HTTP 服务、路由模块及自动更新执行器。DI 在构建时验证依赖。`AutoUpdateRunner` 原先冗长的构造调用也由 DI 解析。
- API 配置通过 `IOptions<ApiHostConfig>` 注入，引用现有 TOML 热重载正在更新的对象。没有创建第二份配置，没有引入第二个文件监听器。令牌仍即时生效，监听地址/启用状态仍在下次启动生效。

## 生命周期边界

这里采用 IHostedService 契约，但没有将整个进程迁移到另一个 Generic Host，也没有把现有启动顺序改成并发的 HostedService 启动。原有 Program 在原阶段调用 StartAsync/StopAsync，POSIX/控制台信号、目录锁、Unix exec 重启仍使用原流程，避免新增信号处理器、重复资源释放或改变插件加载时序。

启动依然是：读取并应用配置/暂存更新 → 创建基础设施与渲染 → 配置及插件文件监听 → 加载适配器 → HTTP → 自动更新调度 → 后台初始插件加载。

停止依然是：取消更新调度 → 标记插件停机 → 停止组件和配置监听 → 停 HTTP 并释放其容器 → 停适配器 → 停渲染 → 释放临时文件 → 外层释放日志和目录锁 → 执行需要的重启。HTTP 容器释放失败会记录警告并继续后续停机。

此处没有将自动更新调度器强行改成 BackgroundService；其现有 TimeProvider 定时器、热更新设置和停机取消语义保持原样。

## 精简的封装与保留的边界

- 删除单次使用、只拼接 `.disable` 后缀的 `GetDisabledPluginPath`，直接在文件停用操作处表达。
- 合并 Updater 中 `GetLatestReleaseAsync → GetLatestReleaseFromApiAsync` 的纯异步转发层。
- 删除 Updater 两个原样转发通知委托的私有重载，在调用处显式传入是否需要回复处理器。
- 路由测试/基准改为直接调用内部路由模块，去掉用于注册页面/目录端点的私有方法反射。
- 保留权限校验、文件路径约束、配置 Schema 转换、资源作用域和插件卸载边界；这些即使调用次数少，也有明确的错误处理或语义，不能仅按调用次数内联。
- `HostCommandHandler` 与适配器桥接存在初始化阶段的依赖关系，现有两阶段绑定保留，避免适配器启动时的事件触发半初始化命令服务。

## 兼容性与验证

SDK、Models 的公开 API 和插件契约没有修改；HTTP 路径、请求/响应格式和权限策略保持一致。配置管理器只新增内部路径只读属性，避免路由模块再传递独立的字符串路径。

新增 `HostHttpLifecycleVerification`（可用 `--http-lifecycle` 单独运行），覆盖禁用 API 不绑定端口、端口占用启动失败后的清理与重试、预取消启动、重复启动/停止、真实 TOML 热重载轮换令牌、监听设置不立即重绑定、容器释放后日志仍可用以及端口可再次绑定。

验证结果：解决方案构建 0 警告/0 错误；Rider 对解决方案中的 209 个 C# 文件分析结果为 0 提示（另 13 个独立 fixture/模板文件不在当前解决方案内）；公开 API 基线与 QQ 取消契约通过；标准验证、内存回收、配置热重载、插件 action/Web/优先级专项通过；22 项更新集成场景通过；Linux PTY 下真实启动、原 PID exec 重启、锁重取与 HTTP 关机通过。没有修改 SDK/Models 源码。审阅可先看 Program 的组装/停机、HostHttpServer 的生命周期、Endpoints 的模块注册，再用 `git diff -w` 查看路由正文是否发生业务变化；路由迁入嵌套模块造成了较多缩进差异。

## 提交前复审

使用 Roslyn 语法树对比迁移前后的 11 个路由注册正文：排除读取构造注入的 Options、配置路径与目录服务的局部声明后，所有正文 token 均一致。新增模块直接承载路由实现；IHostApiEndpoints 有多组实现，统一 DI 注册由生产启动及测试共用。未发现需要额外保留的纯转发门面。
