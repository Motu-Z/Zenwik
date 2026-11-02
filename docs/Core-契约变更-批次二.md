# Core 事件回调线程契约（批次二冻结）

> 适用范围：`Zentwik.Core` 向外界暴露的**全部** `public event`。
> 权威性：批次四（`Zentwik.UI`，WPF）订阅这些事件时，**以本文件为唯一依据**。
> 生成时间：2026-11-01。依据为当前工作区源码，逐条标注了触发点行号。

---

## 0. 总则（一句话）

**Core 不持有任何 UI 调度器。所有事件都在「触发它的那个线程」上同步调用，没有任何一个事件做过跨线程封送。**

批次二已经删除了 `IUiDispatcher` / `SynchronizationContext` 与全部 `Post` 分支，因此：

| 结论 | 说明 |
|---|---|
| 事件回调线程 = 触发动作的线程 | 谁改了数据，谁就在自己的线程上把回调跑完 |
| 订阅方**必须**自行封送 | 只要回调里要碰 WPF 控件、`ObservableCollection`、`INotifyPropertyChanged` 属性，就**必须**切回 UI 线程 |
| 封送落在订阅方一侧 | 用 `Dispatcher.BeginInvoke`（WPF）、`Control.BeginInvoke`（WinForms）、或宿主自己的 `SynchronizationContext.Post` |
| 一律用 `BeginInvoke`，不用 `Invoke` | `Invoke` 会阻塞触发线程。触发线程可能是 WMI 回调线程或采样线程，阻塞它等于让整个监听链路停摆，且有与 Core 内部锁互等的死锁风险 |

---

## 1. 逐事件清单

### 1.1 `LogService.EntryAdded`

| 项 | 内容 |
|---|---|
| 声明 | `Services/LogService.cs:115` — `public event Action<LogEntry>? EntryAdded;`（接口同名成员 `ILogService.cs:21`） |
| 触发点 | `LogService.cs:432` — `AddEntry()` 内部 |
| 触发线程 | **调用日志写方法的那个线程**。可能是：主线程（启动流程）、WMI 回调线程（`ProcessWatcher` 记录绑定结果）、采样线程（`ProcessRefresher` 记录采样失败）、线程池线程（`Task.Run` 的清理任务） |
| 是否封送 | **否，订阅方必须自行封送** |
| 注意事项 | ① `Debug` 级别**不触发**本事件（`Debug` 只落盘，不进内存缓冲），订阅方不要指望它能看到全部日志；② `Paused` 为真时除 `Critical` 外的写入全部丢弃，也不会触发；③ 本事件在 `_entriesGate` 锁**外**触发，回调里可以安全调用 `Snapshot()` |

订阅方写法（WPF）：

```csharp
log.EntryAdded += entry =>
{
    if (!Dispatcher.CheckAccess())
    {
        Dispatcher.BeginInvoke(() => OnEntry(entry));
        return;
    }

    OnEntry(entry);
};
```

---

### 1.2 `ScenarioService.Changed`

| 项 | 内容 |
|---|---|
| 声明 | `Services/ScenarioService.cs:146` — `public event Action? Changed;` |
| 触发点 | `ScenarioService.cs:208`（`Load`）、`:224`（`SetEnabled`）、`:241`（`Save`/`AddOrUpdate`）、`:256`（`Delete`） |
| 触发线程 | **调用这些方法的那一方**。`Load` 在启动流程（主线程 / Worker 启动线程）；`Save` / `Delete` / `SetEnabled` 在 UI 操作线程；**`Load` 也可能被 `ScenarioMonitor` 的重新加载路径从后台线程调用** |
| 是否封送 | **否，订阅方必须自行封送** |
| 注意事项 | 事件在 `Sync` 锁**外**触发，回调里可以安全调用 `Rules`、`Resolve`、`Enabled`；但**不要在回调里再调用 `Save`/`Delete`** —— 会形成「事件里改数据 → 再触发事件」的递归，界面表现为列表闪两下 |

---

### 1.3 `ProfileService.Changed`

| 项 | 内容 |
|---|---|
| 声明 | `Services/ProfileService.cs:137` — `public event Action? Changed;` |
| 触发点 | `ProfileService.cs:177`（`Load`）、`:230`（`Save`）、`:255`（`Delete`）、`:306`（`Apply`） |
| 触发线程 | **调用方线程**。**自动切换路径上是「线程池线程」**，不是 UI 线程，也不是 WMI 线程本身 —— 中间隔着一层 300 ms 防抖定时器，实测调用链见下方 §1.3.1 |
| 是否封送 | **否，订阅方必须自行封送**。这是最容易漏的一条 —— 手动切换方案时它在 UI 线程，自动切换时它在线程池线程，同一份订阅代码会在「用了十分钟之后突然崩」 |
| 注意事项 | 事件在 `Sync` 锁**外**触发；`Apply` 的内部顺序是「先写 SMU（可能耗时数百毫秒）→ 再更新 `_items` 与 `ActiveId` → 再触发 `Changed`」，因此回调被调用时硬件已经写完，回调里读 `ActiveId` 拿到的一定是新值 |

#### 1.3.1 自动切换的完整调用链（逐跳带行号）

```
ProcessWatcher（WMI EventArrived 线程 / 降级轮询线程池）
  └─ ProcessSetChanged?.Invoke                     ProcessWatcher.cs
       └─ ScenarioMonitor.OnProcessSetChanged()    ScenarioMonitor.cs:183
            └─ Schedule()                          ScenarioMonitor.cs:169
                 └─ _debounce.Change(300ms)        ScenarioMonitor.cs:178-179
                      ══════ 跨线程：Timer 到期 ══════
                      └─ Evaluate()   【线程池线程】 ScenarioMonitor.cs:203
                           └─ _scenarios.Notify()  ScenarioMonitor.cs:243
                                └─ _profiles.Apply() ScenarioService.cs:306
                                     └─ Changed?.Invoke()  ProfileService.cs:306
```

电源路径同理：`PowerStatusWatcher.Changed`（`SystemEvents` 隐藏 STA 线程，或 30 s 轮询的线程池线程）→ `ScenarioMonitor.OnPowerChanged`（`:185`）→ 同一个 `Schedule()` → 同一个防抖 Timer。

**结论**：无论事件从哪条线进来，`ProfileService.Changed` 的自动切换分支最终都在 **`System.Threading.Timer` 的回调线程（线程池，MTA）** 上触发。它是 MTA，但这不等于「可以碰控件」—— WPF 只认 `Dispatcher` 所属的那一条线程。

---

### 1.4 `ProcessRefresher.Refreshed`

| 项 | 内容 |
|---|---|
| 声明 | `Services/ProcessRefresher.cs:319` — `public event Action<IReadOnlyList<Entry>>? Refreshed;` |
| 触发点 | `ProcessRefresher.cs:572` — `Sample()` 内部 |
| 触发线程 | **命名后台线程 `Zentwik.ProcessSampler`**（MTA，`ThreadPriority.BelowNormal`）。这是本类唯一的设计执行线程 |
| 是否封送 | **否，订阅方必须自行封送** |
| 注意事项 | ① `Sample()` 是 `public` 的，注释已声明「只应在采样线程上调用」；若有调用方在别的线程直接调它，事件就会在**那个线程**上触发 —— 契约要求调用方自行保证，Core 不做校验；② 事件参数 `IReadOnlyList<Entry>` 是本轮新建的列表，订阅方可以安全持有，不会在下一轮被改写；③ 回调在采样线程上同步执行，**回调里做重活会直接拖慢采样周期**（WPF 侧尤其不要在这里做布局或查询） |

订阅方写法（WPF，列表刷新）：

```csharp
refresher.Refreshed += list =>
{
    // 采样线程上只做一次数组拷贝 + 一次投递，全部 UI 工作留给 UI 线程
    if (!Dispatcher.CheckAccess())
    {
        Dispatcher.BeginInvoke(() => ApplyList(list));
        return;
    }

    ApplyList(list);
};
```

---

### 1.5 `ProcessWatcher.ProcessSetChanged`（经接口 `IProcessWatcher` 暴露）

| 项 | 内容 |
|---|---|
| 声明 | `Services/IProcessWatcher.cs:26` — `event Action? ProcessSetChanged;`（实现在 `ProcessWatcher`） |
| 触发线程 | **WMI 的 `EventArrived` 回调线程**（`ManagementEventWatcher` 由 WMI 投递，MTA）；降级轮询路径下为**线程池线程**（`Task.Run` 的 `LoopAsync`） |
| 是否封送 | **否，订阅方必须自行封送** |
| 注意事项 | 事件频率与系统进程创建/退出频率同阶，在游戏启动时会连续触发几十次。订阅方若在回调里直接刷新列表，必须自己做合并（`ScenarioMonitor` 已有 300 ms 防抖窗口，UI 侧应复用同一思路） |

---

### 1.6 `PowerStatusWatcher.Changed`

| 项 | 内容 |
|---|---|
| 声明 | `Services/PowerStatusWatcher.cs:66` — `public event Action? Changed;` |
| 触发点 | `PowerStatusWatcher.cs:135` 的 `OnPowerModeChanged`，由 `SystemEvents.PowerModeChanged` 驱动 |
| 触发线程 | **.NET `SystemEvents` 的专用消息泵线程**（`SystemEvents` 内部建一个隐藏窗口 + STA 消息循环，所有 `SystemEvents` 事件都在该线程上派发）。轮询兜底路径下为计时器 / 线程池线程 |
| 是否封送 | **否，订阅方必须自行封送** |
| 注意事项 | 不要把「`SystemEvents` 的线程是 STA」误解成「它等于 UI 线程」—— 它是 .NET 自己起的隐藏线程，与 WPF 的 `Dispatcher` 无关，在它上面碰任何控件同样抛 `InvalidOperationException` |

---

### 1.7 `MaskStore.Changed`（`Helpers/MaskStore.cs:79`）

| 项 | 内容 |
|---|---|
| 触发线程 | **调用 `Save` / `Delete` 的那一方**（当前只有 UI 操作线程） |
| 是否封送 | 当前**实际不需要**，但按同一契约**仍要求订阅方判断 `Dispatcher.CheckAccess()`** —— 一旦将来出现后台路径调用 `Save`，缺判断就是崩溃 |

---

## 2. 汇总表（给批次四直接照抄）

| 事件 | 触发线程 | 订阅方必须封送 | 触发时是否持有 Core 内部锁 |
|---|---|---|---|
| `LogService.EntryAdded` | 写入方线程（主 / WMI / 采样 / 线程池） | **是** | 否 |
| `ScenarioService.Changed` | 调用方线程（启动线程 / UI 线程 / 后台） | **是** | 否 |
| `ProfileService.Changed` | 调用方线程；**自动切换时是防抖定时器的线程池线程** | **是** | 否 |
| `ProcessRefresher.Refreshed` | 采样线程 `Zentwik.ProcessSampler`（MTA） | **是** | 否 |
| `ProcessWatcher.ProcessSetChanged` | WMI `EventArrived` 线程 / 线程池 | **是** | 否 |
| `PowerStatusWatcher.Changed` | `SystemEvents` 隐藏 STA 消息泵线程 | **是** | 否 |
| `MaskStore.Changed` | 调用方线程 | 按契约判断（当前实际安全） | 否 |

**「是否需要封送」一列的结论全部为「是」，不存在例外。** 任何在订阅回调里直接操作 WPF 控件的代码，都会在「场景自动切换」或「后台采样」这两条路径上崩在非 UI 线程。

---

## 3. 两条衍生约束（批次三 / 批次四都适用）

1. **不要用 `Dispatcher.Invoke` 同步回 UI 线程。** 触发线程可能是 WMI 回调线程或采样线程；同步封送会把 UI 线程的忙闲反向传回监听链路，最坏情况是与 Core 内部锁互相等待。
2. **不要在事件回调里抛异常。** Core 的 `SampleOnce`、`Notify` 等调用点只捕获自己关心的异常类型，订阅方抛出的异常会穿过 Core 的循环体。WPF 侧应在回调入口用一个统一的 `SafeInvoke` 包一层 try/catch 并记日志。
