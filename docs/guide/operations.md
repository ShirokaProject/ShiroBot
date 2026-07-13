# 运行与维护

## 控制台命令

交互模式下输入 `help` 可以查看命令。

| 命令 | 说明 |
| --- | --- |
| `plugins` | 显示已加载插件、程序集文件大小和宿主进程工作集 |
| `load <插件名或路径>` | 热加载插件 |
| `unload <插件 ID>` | 热卸载插件 |
| `restart` | 启动替代进程并退出当前进程 |
| `/api` | 显示或设置 API 鉴权信息 |
| `update` | 查看或处理待确认更新 |
| `path` | 打开程序目录 |
| `log` | 开关普通日志输出 |
| `clear` | 清空控制台 |
| `exit` / `quit` | 正常退出 |

`owner_list` 中的用户也可以通过好友私聊执行 `help`、`plugins`、`load`、`unload`、`update` 和 `api` 命令。

## 热加载与热卸载

把插件 DLL 放入插件目录后，可以执行：

```text
load HelloPlugin
```

卸载时使用插件 ID，而不是显示名称：

```text
unload HelloPlugin
```

宿主会先停止为插件路由新事件，等待正在执行的事件结束，再调用插件清理逻辑并尝试回收程序集加载上下文。

如果日志提示程序集未完全卸载，通常表示插件仍有以下引用：

- 未停止的后台任务或计时器。
- 未释放的配置监听、事件订阅或回调。
- 静态字段持有插件对象或插件类型。
- native 库或第三方框架保存了托管回调。

插件应在 `OnUnloadAsync()` 中释放自己创建的资源。

## native 依赖缓存

自动下载的 native 文件位于：

```text
plugins/<插件数据目录>/.shirobot/native/<包名>/<版本>/<RID>/
```

缓存包含 `.complete` 校验标记。删除对应版本目录后，下次加载会重新下载和校验。下载过程中的临时 `.nupkg` 会在成功或失败后删除。

## 内存指标

`plugins` 命令中的“程序集文件大小”是磁盘 DLL 大小，不是运行时内存。进程工作集包含：

- ShiroBot 宿主与 .NET 运行时。
- 适配器和所有插件。
- GC 堆、JIT 代码和线程栈。
- Avalonia、Skia、HarfBuzz 等 native 内存。

插件使用独立 ALC，但不使用独立 GC 堆或独立进程，因此宿主无法通过普通运行时 API 精确拆分每个插件的完整工作集。

## 更新前备份

升级宿主、插件或适配器前，建议备份：

```text
config.toml
adapters/**/config.toml
adapters/*.toml
plugins/**/config.toml
plugins/**/其他业务数据
```

`.shirobot/native` 是可重新生成的缓存，通常不需要备份。
