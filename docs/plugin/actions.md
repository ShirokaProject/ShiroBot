# 插件操作与控制台

插件实现 `IPluginActionProvider` 后，同一组操作会出现在宿主控制台和已鉴权的 Dashboard 中。插件不需要注册独立的控制台解析器，也不需要直接接触 `HttpContext`。

## 定义操作

```csharp
using ShiroBot.SDK.Plugin;

public sealed class Main : PluginBase, IPluginActionProvider
{
    public IReadOnlyList<PluginActionDescriptor> Actions { get; } =
    [
        new("refresh-cache", "刷新缓存", "重新拉取远端数据", Tone: "primary"),
        new("clear-cache", "清空缓存", "删除插件缓存", Tone: "danger",
            RequiresConfirmation: true, ConfirmationText: "确认清空缓存？")
    ];

    public Task<PluginActionResult> ExecuteActionAsync(
        string actionId,
        CancellationToken cancellationToken = default) => actionId switch
        {
            "refresh-cache" => Task.FromResult(new PluginActionResult(true, "缓存已刷新", Refresh: true)),
            "clear-cache" => Task.FromResult(new PluginActionResult(true, "缓存已清空", Refresh: true)),
            _ => Task.FromResult(new PluginActionResult(false, "未知操作"))
        };
}
```

`PluginActionDescriptor.Id` 在同一插件内应唯一；`Label` 和 `Description` 用于显示。`RequiresConfirmation` 与 `ConfirmationText` 控制确认提示。`PluginActionResult.Ok` 和 `Message` 表示执行结果，`Refresh` 提示 Dashboard 刷新相关信息。示例里的处理分支应替换成实际业务逻辑，并在异步操作中使用传入的 `CancellationToken`。

## 从控制台执行

```text
actions
action HelloPlugin refresh-cache
```

`help` 会列出宿主命令和插件操作，`actions` 只列出插件操作，并支持命令补全。带 `RequiresConfirmation` 的操作在执行前要求输入 `y`。控制台使用已加载插件的 ID 和操作 ID 定位操作；插件卸载后，这些命令不再可用。

宿主控制台命令不会通过 QQ 或其他平台的私聊执行。`owner_list` 只供插件的 `Context.IsOwner` 与 `Context.IsAdmin` 判断权限；它不授予远程控制台访问。宿主控制台命令见[运行与维护](/guide/operations#控制台命令)。

## 从 Dashboard 执行

Dashboard 使用 `GET /api/v1/plugins/{id}/actions` 获取描述，使用 `POST /api/v1/plugins/{id}/actions/{actionId}` 执行。接口需要 Bearer 鉴权。调用受插件活动执行保护；热卸载会等待正在运行的操作完成。控制台确认提示与 Dashboard 的确认界面由各自前端处理。
