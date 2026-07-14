# 上下文、配置与日志

宿主在加载插件时注入 `IBotContext`，继承 `PluginBase` 后可以通过 `Context` 使用。

## 上下文能力

| 属性 | 用途 |
| --- | --- |
| `Context.Message` | 发送、回复、撤回、查询消息 |
| `Context.Group` | 群设置、群成员管理、请求处理 |
| `Context.Friend` | 好友请求、点赞、戳一戳等 |
| `Context.File` | 上传、下载、移动、删除文件 |
| `Context.System` | 登录信息、好友列表、群列表等 |
| `Context.Config` | 插件独立 TOML 配置 |
| `Context.WebHost` | 宿主 HTTP 服务与公开地址 |
| `Context.Updater` | 插件更新能力 |
| `Context.Render` | 可选图片渲染服务 |
| `Context.PluginDirectory` | 插件稳定数据目录 |
| `Context.OwnerList` | 宿主所有者列表 |
| `Context.AdminList` | 宿主管理员列表 |

权限判断：

```csharp
if (!Context.IsAdmin(message.SenderId))
{
    await Context.Message.ReplyAsync(message, "权限不足");
    return;
}
```

适配器可以只实现自己支持的服务方法。调用不支持的方法时会抛出 `NotSupportedException`，插件应按需要捕获并提供友好提示。

## 插件配置

定义配置类型：

```csharp
using ShiroBot.SDK.Config;

public sealed class HelloConfig
{
    [ConfigField("回复文本", Label = "Ping 回复", Placeholder = "pong")]
    public string ReplyText { get; set; } = "pong";

    [ConfigField("请求超时秒数", Min = 1, Max = 120)]
    public int TimeoutSeconds { get; set; } = 15;

    [ConfigField("是否启用", Type = "boolean")]
    public bool Enabled { get; set; } = true;
}
```

加载并监听配置：

```csharp
private HelloConfig _config = new();
private IDisposable? _configWatcher;

protected override Task LoadAsync()
{
    _config = Context.Config.Load<HelloConfig>();
    Context.Config.Save(_config);

    _configWatcher = Context.Config.Watch<HelloConfig>(updated =>
    {
        _config = updated;
        BotLog.Info("配置已热更新");
    });

    return Task.CompletedTask;
}

protected override Task OnUnloadAsync()
{
    _configWatcher?.Dispose();
    return Task.CompletedTask;
}
```

`Watch<T>()` 返回的订阅必须释放，否则文件监听器会阻止插件完整卸载。

`Config.Save<T>()` 和首次 `Config.Load<T>()` 生成文件时，会把顶层属性的 `ConfigFieldAttribute` 写到对应 snake_case 键上方。`Label`、`Description`、`Options`、`Min`/`Max` 和 `Placeholder` 会转换为合法 `#` 注释；重复保存不会重复堆叠注释。

只更新一个字段并尽量保留 TOML 注释和格式：

```csharp
Context.Config.SetValue("timeout_seconds", 30);
```

## 数据文件

不要把运行时数据写到当前工作目录。使用插件数据目录：

```csharp
var databasePath = Path.Combine(Context.PluginDirectory, "data.db");
var cacheDirectory = Path.Combine(Context.PluginDirectory, "cache");
Directory.CreateDirectory(cacheDirectory);
```

即使单 DLL 位于 `plugins` 根目录，宿主也会为它提供稳定的 `plugins/<插件 ID>` 数据目录。

## 日志

```csharp
BotLog.Info("普通信息");
BotLog.Success("操作成功");
BotLog.Warning("可恢复问题");
BotLog.Error("操作失败");
```

宿主会为插件设置日志作用域，日志中心可以按插件 ID 区分来源。避免记录访问令牌、API 密钥和用户隐私数据。

## 回复订阅

发送消息后可以等待用户回复：

```csharp
var sent = await Context.Message.SendGroupMessageAsync(groupId, "请回复 yes");

var subscription = Context.Message.SubscribeReply(
    sent.MessageSeq,
    "yes",
    TimeSpan.FromMinutes(1),
    async reply =>
    {
        await Context.Message.ReplyAsync(reply, "已确认");
    });
```

默认在匹配一次后自动释放。长期订阅应保存返回值，并在插件卸载时主动 `Dispose()`。
