# 插件互相调用示例

两个最小插件，演示 `Context.Lifetime`、按名称调用的服务和事件总线。它们只引用 ShiroBot SDK，**互相之间没有任何引用，也没有共享程序集**。

| 插件 | ID | 做什么 |
| --- | --- | --- |
| [Greeter](Greeter/GreeterPlugin.cs) | `sample-greeter` | 提供 `greeter.greet` 服务；每次问候后发布 `greeter.greeted` 事件；用 `SetInterval` 定时打日志 |
| [GreeterClient](GreeterClient/GreeterClientPlugin.cs) | `sample-greeter-client` | 收到 `#greet 名字` 时调用 `greeter.greet` 并回复；订阅 `greeter.greeted` 打日志 |

两个插件都没有 `OnUnloadAsync`：服务、订阅和计时器都登记在 `Context` 上，卸载时由宿主注销和停止。

## 试一试

```sh
dotnet build Templates/Examples/PluginInterop/Greeter/ShiroBot.Samples.Greeter.csproj
dotnet build Templates/Examples/PluginInterop/GreeterClient/ShiroBot.Samples.GreeterClient.csproj
```

把两个 `bin/Debug/net11.0/*.dll` 放进宿主的 `plugins` 目录后，在群里发 `#greet 小白`，会回复“你好，小白！”。
然后只卸载 `sample-greeter`，再发 `#greet`，会回复“问候服务没有加载。”；重新加载后不需要重新加载调用方就能恢复。

`Tests/Verification` 中的 `PluginInteropVerification` 会在真实的可回收 ALC 中自动跑一遍这个流程，并检查两个插件卸载后程序集都能回收：

```sh
dotnet run --project Tests/Verification -- --plugin-interop
```
