# 配置与部署

## 使用宿主注入的配置

宿主在调用 `StartAsync()` 前设置 `Config`：

```csharp
public IConfigContext Config { get; set; } = null!;

public async Task StartAsync()
{
    var config = Config.Load<ExampleAdapterConfig>();
    Config.Save(config);

    await ConnectAsync(config);
}
```

适配器配置文件位置取决于部署结构。

### 根目录单 DLL

```text
adapters/ExampleAdapter.dll
adapters/config.toml
```

宿主按入口 DLL 所在目录寻找 `config.toml`。多个根目录单 DLL 适配器会共用 `adapters/config.toml`，因此需要独立配置时使用目录部署。

### 目录部署

```text
adapters/ExampleAdapter/ExampleAdapter.dll
adapters/ExampleAdapter/config.toml
adapters/ExampleAdapter/Protocol.Client.dll
adapters/ExampleAdapter/runtimes/...
```

## 同一个 DLL 配置多个实例

以下管理功能从 v0.9.7 开始提供。安装 DLL/ZIP 只安装适配器包，不会自动创建实例。在 Dashboard「适配器」页展开一个包，点击「添加实例」，填写全局唯一 ID。新实例默认停用，打开配置填写连接信息后启动。

同一包的实例共用程序集文件，拥有独立适配器对象和可回收程序集上下文。实例清单、名称、启用状态和各自的连接配置全部保存在该包目录的 `config.toml`：

```text
adapters/milky/ShiroBot.Adapter.Milky.dll
adapters/milky/config.toml
```

包 ID、入口 DLL、名称和版本由程序集元数据识别，扫描结果缓存在宿主 `cache/adapters/` 下。缓存可删除；缺失、损坏或 DLL 变化时会重新扫描，不影响配置和实例开关。旧的包目录 `adapter.json` 在成功识别后自动清理。

```toml
[[instances]]
id = "qq-work"
name = "工作机器人"
enabled = true

[instances.config]
# 按该适配器的配置字段填写连接信息。

[[instances]]
id = "qq-home"
name = "家庭机器人"
enabled = false

[instances.config]
# 第二个账号的连接信息。
```

ID 在所有适配器包之间唯一，忽略大小写，最多 64 个字符，允许英文字母、数字、点、横线和下划线。name 可省略，enabled 默认 false。`instances = []` 表示只安装程序集，不启动实例。手动修改清单或启用状态后重启生效；运行实例的连接配置支持 SDK 配置监听。WebUI 和 CLI 写回同一份文件。ZIP 入口在子目录时，配置仍保存在适配器包根目录，不随入口 DLL 路径变化。

适配器总开关只保存在宿主主配置的 `protocols`，实例开关只保存在适配器 `config.toml` 的 `[[instances]]` 中。旧 `.shirobot-adapter-state.json`、根配置实例清单和 `.instances/` 注册表不再读取或迁移。单实例平铺配置的开关使用该文件根级 `enabled`；添加第二个实例时才展开为 `[[instances]]`。备份时保留宿主主配置和整个 `adapters/`。

更新程序集处理该包下全部运行实例，保留所有实例的配置和启用状态；无法热替换时暂存到下次重启。删除实例只删除该条实例及连接配置，最后一个实例删除后仍保留包；「删除适配器包」才删除程序集和全部实例。

适配器应通过 `Config.Load<T>()`、`Config.Save()`、`Config.SetValue()` 和 `Config.Watch<T>()` 访问配置，宿主自动限定到当前实例的 config 表。`Config.ConfigPath` 是共享文件路径，直接反序列化整份文件的旧适配器需要改用上述 SDK 方法。连接、取消令牌、监听器等资源应属于实例，每个监听实例使用不同端口。

`BotAdapterAttribute.Id` 仍是包 ID。宿主为上报事件写入实际实例 ID，插件自动回复按来源实例路由，详见[实例路由与后台发送](/plugin/apis#适配器实例与后台发送)。

### 没有 WebUI 时

先安装包，再添加实例：

```bash
./ShiroBot adapter install ./ShiroBot.Adapter.Milky.dll
./ShiroBot adapter create milky qq-work --name "工作机器人"
./ShiroBot adapter config qq-work
# 编辑输出的 config.toml 中 id=qq-work 的 [instances.config]。
./ShiroBot adapter enable qq-work
./ShiroBot adapter list
./ShiroBot --no-console
```

离线命令修改文件，应在宿主停止时使用。也可直接编辑上述 `[[instances]]` TOML，无需 WebUI。`adapter remove <实例 ID>` 只删除实例；`adapter remove-package <包 ID>` 删除包及全部实例。运行中的交互式控制台支持 `adapter create`、`adapter config`、`adapter start`、`adapter stop` 和 `adapter remove`。

主配置 `protocols` 是已安装适配器包的总开关，使用包 ID，例如 `protocols = ["qq-official"]`。实例是否启动还取决于适配器配置中的 `enabled`。`protocols` 也可包含开发用独立 DLL 路径，`--adapter` 可用于加载独立 DLL，但不绕过已安装包的总开关。

## 开发时加载独立 DLL

核心配置：

```toml
protocols = ["ExampleAdapter"]
```

宿主依次尝试：

```text
adapters/ExampleAdapter.dll
adapters/ExampleAdapter/ExampleAdapter.dll
```

也可以在启动时直接指定：

```bash
./ShiroBot --adapter /opt/shirobot/adapters/ExampleAdapter/ExampleAdapter.dll
```

Dashboard 安装的包和新增实例按保存的启用状态加载。配置路径不存在时记录错误并跳过，宿主仍能以无适配器模式启动，以便通过 Dashboard 修复。

## 发布目录适配器

有 managed 或 native 依赖时，推荐发布完整目录：

```bash
dotnet publish -c Release -o ./publish
```

复制到：

```text
ShiroBot/adapters/ExampleAdapter/
```

宿主已经内置 `ShiroBot.SDK.dll` 和 Discord、QQ、Telegram 平台 Model。部署时不要在适配器目录携带不同版本的共享程序集。

## 适配器单 DLL

当前模板启用 SDK 的单 DLL 打包目标；没有额外文件依赖时可以直接部署构建出的入口 DLL。宿主也会读取适配器中嵌入的 native NuGet 依赖清单，并按当前 RID 准备资源。若依赖协议客户端配置文件或其他不能嵌入的文件，仍应使用目录部署并保留所需文件。

## 日志

宿主会注入带适配器来源的 logger：

```csharp
public IConsoleLogger Logger { get; set; } = null!;

Logger.Info("开始连接协议端");
Logger.Success("登录成功");
Logger.Warning("连接断开，5 秒后重试");
Logger.Error("鉴权失败");
```

不要自行替换全局日志器。日志中避免输出 access token、Cookie 或完整鉴权请求。

## 连接生命周期

`IBotAdapter` 提供默认 no-op 生命周期；这些方法由宿主调用，需要连接或后台任务的适配器再重写：

- `StartAsync()` 完成初始连接和鉴权。
- `StopAsync()` 取消事件循环并关闭协议连接。
- 没有初始化和清理工作的适配器可以不实现这两个方法。
- 后台事件循环自行处理取消、断线和重连。
- 致命错误应抛出，让宿主明确启动失败。
- 可恢复错误应记录后退避重试。

如果适配器启动了 Webhook HTTP 监听器，要避免与宿主 API 端口冲突，并在进程退出时响应运行时取消信号。

## 发布检查清单

- `BotAdapterAttribute.Id`、显示名称和程序集命名清晰稳定。
- `BotAdapterAttribute.Version` 与发布版本一致。
- 默认配置不包含真实凭据。
- 至少完成登录信息与消息发送的端到端测试。
- 每种声称支持的事件都有协议样本测试。
- Windows、Linux、macOS 所需 native 资产均包含在发布目录。
- 不携带与宿主冲突的 SDK、Model 和 Avalonia 运行时副本。
