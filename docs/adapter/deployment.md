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
adapters/ExampleAdapter.toml
```

### 目录部署

```text
adapters/ExampleAdapter/ExampleAdapter.dll
adapters/ExampleAdapter/config.toml
adapters/ExampleAdapter/Protocol.Client.dll
adapters/ExampleAdapter/runtimes/...
```

## 宿主选择适配器

核心配置：

```toml
protocol = "ExampleAdapter"
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

未配置或路径不存在时，宿主会尝试选择 `adapters` 下找到的第一个 DLL。生产环境不要依赖这个回退行为。

## 发布目录适配器

适配器当前不使用插件的自动 native 延迟下载流程。最稳妥的方式是发布完整目录：

```bash
dotnet publish -c Release -o ./publish
```

复制到：

```text
ShiroBot/adapters/ExampleAdapter/
```

宿主已经提供 `ShiroBot.SDK.dll` 和 `ShiroBot.Model.dll`，部署时应避免在适配器目录携带不同版本的这两个共享程序集。

## 适配器单 DLL

如果适配器只有 managed 依赖，可以自行使用 ILRepack 合并，或者在发布流程中加入合并目标。不要直接启用插件自动打包目标，因为适配器没有 `BotPluginAttribute`，生命周期和 native 加载策略也与插件不同。

包含 native 库的适配器推荐使用目录部署，并保留 `.deps.json` 与 `runtimes` 结构，让 `AssemblyDependencyResolver` 按正常 .NET 规则解析。

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

当前 `IBotAdapter` 只有 `StartAsync()`，没有热卸载或 `StopAsync()` 接口。适配器通常与宿主进程同生命周期：

- `StartAsync()` 完成初始连接和鉴权。
- 后台事件循环自行处理取消、断线和重连。
- 致命错误应抛出，让宿主明确启动失败。
- 可恢复错误应记录后退避重试。

如果适配器启动了 Webhook HTTP 监听器，要避免与宿主 API 端口冲突，并在进程退出时响应运行时取消信号。

## 发布检查清单

- `Name`、`Metadata.Name` 和程序集命名清晰稳定。
- `Metadata.Version` 与发布版本一致。
- 默认配置不包含真实凭据。
- 至少完成登录信息与消息发送的端到端测试。
- 每种声称支持的事件都有协议样本测试。
- Windows、Linux、macOS 所需 native 资产均包含在发布目录。
- 不携带与宿主冲突的 SDK、Model 和 Avalonia 运行时副本。
