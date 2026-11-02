# Dashboard 页面

需要宿主 1.0.3（SDK ABI 1.4.0.0）。

插件配置适合开关、数字和列表这类设置。浏览和管理插件的数据（例如表情库、订阅列表、统计）放不进配置，可以给插件加 **Dashboard 页面**：插件实现 `IPluginDashboardProvider`，Dashboard 在插件的“配置”窗口中显示专用的“插件页面”区域，用通用组件把数据渲染成卡片、表格、列表或统计数字。

- Dashboard 不加载插件的 HTML 或脚本，所有页面外观一致。
- 数据、操作和图片都经过宿主已鉴权的 API，由宿主在进程内调用插件。插件**不需要开放网页路由，也不需要自己做鉴权**。
- 配置窗口左侧按“配置 → 插件页面 → 路由与监听”分成三个区块，用分隔线隔开；完整配置页保持同样布局。
- 页面描述不属于配置 schema，不伪装成配置分组。选中插件页面时不显示配置保存按钮，页面操作直接走页面 API；切回配置分组时保留未保存的配置草稿。

## 示例

```csharp
using ShiroBot.SDK.Dashboard;
using ShiroBot.SDK.Plugin;

[BotPlugin("notes", Name = "笔记")]
public sealed class NotesPlugin : PluginBase, IPluginDashboardProvider
{
    public IReadOnlyList<DashboardPage> Pages { get; } =
    [
        new("notes", "笔记", DashboardPageKinds.Table, Icon: "sticky_note_2",
            Description: "所有群里记下的笔记。",
            Columns: [new("group", "群"), new("author", "作者"), new("time", "时间")],
            Filters: [new("q", "搜索")],
            Actions: [new("delete", "删除", Confirm: "确定删除这条笔记？", Tone: "danger")])
    ];

    public async Task<DashboardPageData> QueryAsync(string pageId, DashboardQuery query, CancellationToken ct)
    {
        var (notes, total) = await _store.SearchAsync(query.Filter("q"), query.Page, query.PageSize, ct);
        return new DashboardPageData(
            notes.Select(note => new DashboardItem(note.Id, note.Text,
                Values: new Dictionary<string, string>
                {
                    ["group"] = note.GroupName, ["author"] = note.Author, ["time"] = note.Time.ToString("g")
                },
                Actions: ["delete"])).ToList(),
            Total: total,
            HasMore: query.Page * query.PageSize < total);
    }

    public Task<DashboardItem?> GetItemAsync(string pageId, string itemId, CancellationToken ct) =>
        _store.FindDashboardItemAsync(itemId, ct); // 返回当前条目及其支持的 Actions；不存在时返回 null

    public async Task<PluginActionResult> RunActionAsync(string pageId, string actionId, string itemId, CancellationToken ct)
    {
        var removed = await _store.RemoveAsync(itemId, ct);
        return new PluginActionResult(removed, removed ? "已删除" : "笔记不存在", Refresh: true);
    }
}
```

## 页面

`Pages` 每次打开插件时读取，可以随配置变化。

| 字段 | 说明 |
| --- | --- |
| `Id` / `Title` | 页面 ID（插件内唯一）和导航里的标题 |
| `Kind` | `DashboardPageKinds` 之一：`Gallery` 卡片网格（适合图片）、`Table` 表格、`List` 列表、`Stats` 统计数字 |
| `Icon` | Material Symbols 图标名，例如 `photo_library` |
| `Description` | 页面顶部的说明 |
| `Order` | 导航顺序，小的在前 |
| `Columns` | 表格的列，值取条目的 `Values` |
| `Filters` | 筛选项：`text` 文本输入，`select` 下拉选择（`Options`） |
| `PageSize` | 每页数量，默认 24；0 表示不分页 |
| `Actions` | 条目上的操作：`Confirm` 执行前的确认提示，`Tone` 为 `danger` 时显示为危险操作 |
| `Upload` | 可选文件上传能力，通过属性初始化器声明；为空时不显示按钮，也不允许上传 |

## 数据

`QueryAsync` 收到 `DashboardQuery`：`Page` 从 1 开始，`PageSize` 不超过 100，`Filters` 只包含页面声明过、而且非空的筛选值，用 `query.Filter("q")` 读取。

返回 `DashboardPageData`：

| 字段 | 说明 |
| --- | --- |
| `Items` | 当前页的条目 |
| `Total` | 总数；不知道时留空 |
| `HasMore` | 是否还有下一页 |
| `Metrics` | 统计页的数字：`Label`、`Value`、`Hint` |

`DashboardItem` 的 `Title`、`Description`、`Tags`、`Meta` 在卡片和列表里显示；`Values` 是表格各列的值；`Actions` 是这个条目支持的操作 ID；`ImageFileId` 见下文。

## 操作

声明操作的插件必须实现 `GetItemAsync(pageId, itemId, ct)`，返回当前条目（包括 `Actions`）；默认返回 null，拒绝操作。宿主按 ID 检查条目存在、ID 一致，并支持该操作，不扫描分页数据。检查与执行之间数据仍可能变化，插件应在存储操作中再次验证条目状态。

`RunActionAsync` 只会收到页面上声明过、而且条目支持的操作，`itemId` 是条目 ID。返回的 `PluginActionResult` 和[插件操作](./actions)相同：`Message` 提示给用户，`Refresh` 为 true 时重新读取数据，`Details` 在居中的对话框里显示较长的内容。

## 图片和文件

条目的 `ImageFileId` 不为空时，Dashboard 调用 `OpenFileAsync` 读取图片：

```csharp
public Task<DashboardFile?> OpenFileAsync(string pageId, string fileId, CancellationToken ct) =>
    Task.FromResult(_store.Find(fileId) is { } meme ? DashboardFile.FromPath(meme.FilePath, meme.Mime) : null);
```

`DashboardFile.FromPath` 交出磁盘上的文件，`FromBytes` 交出内存中的内容。宿主在插件调用结束前打开文件流（允许删除共享），或复制内存内容，响应结束时释放流。插件热卸载删除文件后，已经打开的响应仍可读完。

## 文件上传

在页面上声明 `Upload`，配置窗口中的对应插件页面会显示上传按钮，支持多选、逐个处理和取消：

```csharp
new DashboardPage("library", "表情库", DashboardPageKinds.Gallery)
{
    Upload = new DashboardUpload("上传图片", ["image/png", "image/jpeg", "image/gif", "image/webp"],
        MaxBytes: 5_000_000, Description: "上传后自动去重并刷新表情库。")
};
```

插件实现 `UploadAsync(string pageId, DashboardUploadFile file, CancellationToken ct)`。
`file.FileName` 是显示用的文件名，`ContentType` 是客户端声明的 MIME，`Content` 是宿主读取的文件字节。
插件应按真实内容识别格式，用自己生成的文件名保存，不使用上传文件名作为磁盘路径。

宿主统一做 Bearer 鉴权、声明检查和流式大小限制，单文件最多 20,000,000 字节，页面可设置更小的上限。
未声明 `Upload` 的页面拒绝上传，空文件和不支持的 MIME 返回 400，超限返回 413；插件默认实现也拒绝上传。
接收文件和调用插件都在插件调用保护范围内，支持请求取消和热卸载等待。

返回 `PluginActionResult`：`Ok` 和 `Message` 提示处理结果，`Refresh = true` 表示入库后需要刷新数据。
前端多文件按顺序发送，不把图片写进 `config.toml`，也不依赖配置保存按钮。上传完成后清空筛选并返回第一页。

## 出错时

- 插件抛出异常时，Dashboard 显示失败原因，不影响宿主和其他插件。
- 插件正在卸载时，页面提示稍后再试。
- 找不到页面、操作或文件时，Dashboard 显示“未找到”。

## API

Dashboard 通过以下接口访问页面，都需要 API 密钥，也可以用于自己的工具：

| 接口 | 说明 |
| --- | --- |
| `GET /api/v1/plugins/{id}/pages` | 页面列表 |
| `GET /api/v1/plugins/{id}/pages/{page}/data?page=1&page_size=24&筛选项=值` | 一页数据 |
| `POST /api/v1/plugins/{id}/pages/{page}/actions/{action}?item=条目ID` | 执行操作 |
| `GET /api/v1/plugins/{id}/pages/{page}/files?id=文件ID` | 读取文件 |
| `POST /api/v1/plugins/{id}/pages/{page}/upload?name=文件名` | 上传一个文件，正文为原始二进制字节，Content-Type 为文件 MIME；无需 multipart |
