# 插件操作与控制台

实现 `IPluginActionProvider`，向宿主控制台和已鉴权的 Dashboard 提供同一组操作。

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

## 显示详细结果

`Message` 显示简短结果：Dashboard 用顶部提示，控制台用单行输出。报告、生成的值或检查结果等详细内容放在 `Details`；标题用可选的 `Title`，默认取操作的 `Label`：

```csharp
"report" => Task.FromResult(new PluginActionResult(true, "报告已生成")
{
    Title = "运行报告",
    Details = $"处理消息：{count} 条\n命令命中：{hits} 次\n最近错误：{lastError ?? "无"}"
}),
```

设置了 `Details` 时，Dashboard 在屏幕中间弹出对话框，先显示 `Message`，下面是保留换行的 `Details`，内容过长时可滚动；控制台在输入行上方浮出提示，日志继续在它上面滚动，按 Esc 或执行下一条命令后关闭；内容超过半屏时只显示开头，完整内容写入日志。`Message` 照常输出到日志。没有 `Details` 时仅显示 `Message`。

## 说明

`PluginActionDescriptor.Id` 在同一插件内应唯一；`Label` 和 `Description` 用于显示。`RequiresConfirmation` 与 `ConfirmationText` 控制确认提示。`PluginActionResult.Ok` 和 `Message` 表示执行结果，`Refresh` 提示 Dashboard 刷新相关信息。示例里的处理分支应替换成实际业务逻辑，并在异步操作中使用传入的 `CancellationToken`。操作抛出异常时，Dashboard 和控制台都会显示“插件操作执行失败”及异常信息，完整异常写入日志；控制台不会因此停止响应。

## 从控制台执行

```text
plugin actions
plugin action HelloPlugin refresh-cache
```

`plugin actions` 列出插件操作，输入时逐词补全插件和操作。带 `RequiresConfirmation` 的操作在执行前要求输入 `y`。控制台使用已加载插件的 ID 和操作 ID 定位操作；插件卸载后，这些命令不再可用。

宿主控制台命令不会通过 QQ 或其他平台的私聊执行。`owner_list` 只供插件的 `Context.IsOwner` 与 `Context.IsAdmin` 判断权限；它不授予远程控制台访问。宿主控制台命令见[运行与维护](/guide/operations#控制台命令)。

## 从 Dashboard 执行

Dashboard 使用 `GET /api/v1/plugins/{id}/actions` 获取描述，使用 `POST /api/v1/plugins/{id}/actions/{actionId}` 执行。接口需要 Bearer 鉴权。调用受插件活动执行保护；热卸载会等待正在运行的操作完成。控制台确认提示与 Dashboard 的确认界面由各自前端处理。
