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

以下管理功能属于 v0.9.6 之后的开发版本。安装 DLL/ZIP 后生成默认实例，ID 与 `BotAdapterAttribute.Id` 相同，继续使用原入口 DLL 所在目录的 `config.toml`；旧安装无需迁移。

在 Dashboard「适配器」页选择已安装实例，点击「添加实例」：

1. 输入宿主内唯一的实例 ID，例如 `qq-work`，显示名称可选。
2. 创建后保持停用，打开该实例的「配置」填写账号、令牌及协议端地址。
3. 保存配置后启动；各实例可以独立停止、重载和删除。

新增实例从空配置开始，配置表单可以按 schema 展示默认值，不会复制其他机器人的凭据。所有实例复用同一份已安装 DLL/依赖文件，但使用独立可回收程序集上下文和适配器对象：

```text
adapters/milky/adapter.json
adapters/milky/ShiroBot.Adapter.Milky.dll
adapters/milky/config.toml                 # 默认实例 milky
adapters/.instances/qq-work/config.toml    # 实例 qq-work
adapters/.instances/qq-home/config.toml
```

ZIP 的入口在子目录时，默认配置也在入口所在目录；新增实例的配置位置不变。重启按各实例的启用状态恢复。根 `config.toml` 的 `adapter_instances` 保存实例清单、名称与启用状态；备份根配置和整个 `adapters/`（包含隐藏的 `.instances/`）。旧的 adapter.json/instance.json 实例记录会在首次启动时迁移，连接配置不变。

更新包统一处理该包下全部运行实例，保留每份配置和启用状态；无法热替换时暂存到下次宿主重启。删除一个实例不删除其他实例使用的 DLL；最后一个实例删除时才卸载包。删除默认实例后，下次启动不会重新生成它。

适配器必须通过注入的 `Config.ConfigPath` 读取配置，不要硬编码 DLL 目录下的 `config.toml`。连接、取消令牌、监听器等资源应属于实例，每个监听实例使用不同端口。是否允许同平台账号同时建立多条连接，由平台协议决定。

`BotAdapterAttribute.Id` 仍是包 ID，不需要根据账号修改。宿主给上报事件写入实际实例 ID，日志和插件调用也按实例区分。插件侧用法见[实例路由与后台发送](/plugin/apis#适配器实例与后台发送)。

### 没有 WebUI 时

先安装一次包，再创建实例，不必复制 DLL：

```bash
./ShiroBot adapter install ./ShiroBot.Adapter.Milky.dll --no-enable
./ShiroBot adapter create milky qq-work --name "工作机器人"
./ShiroBot adapter config qq-work
# 编辑上一步输出的 config.toml，填入协议连接信息。
./ShiroBot adapter enable qq-work
./ShiroBot adapter list
./ShiroBot --no-console
```

这些离线命令修改文件，运行前应停止宿主；enable/disable 在下次启动生效。`adapter remove <实例 ID>` 删除一个实例，最后一个实例删除时卸载包；`adapter remove-package <包 ID>` 删除整包及其全部实例。正在运行的交互式控制台支持 `adapter create <包 ID> <实例 ID>`、`adapter config <实例 ID>`、`adapter start|stop|reload|remove <实例 ID>`，会即时执行。

也可直接编辑宿主旁的根 `config.toml`：

```toml
# 一份 DLL 两份配置，不启用默认实例 milky。
[[adapter_instances]]
id = "qq-work"
package_id = "milky"
name = "工作机器人"
enabled = true

[[adapter_instances]]
id = "qq-home"
package_id = "milky"
name = "家庭机器人"
enabled = false
```

包必须已经安装；实例 ID 唯一，name 可省略，enabled 默认 false。额外实例配置固定在 `adapters/.instances/<实例 ID>/config.toml`，先创建并填写该文件，或使用 CLI 的 config 命令创建空文件。默认实例的 ID 等于包 ID 时，继续使用入口 DLL 目录的 config.toml。

`adapter_instances` 存在时为已安装包的唯一实例清单：未列出的默认实例不会额外启动；空数组 `adapter_instances = []` 表示不加载任何已安装实例。手动修改清单后重启生效，移除清单条目仅停止下次加载，不删除磁盘配置；要删除文件使用 remove 命令或 WebUI。WebUI/CLI 创建、启停、删除会写回同一份清单，无需维护两套状态。`protocols` 和 `--adapter` 保留用于直接加载未安装的独立 DLL，不应重复声明已安装包的实例。

## 宿主选择适配器

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
