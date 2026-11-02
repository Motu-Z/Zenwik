# Zentwik 已实现但存疑清单（UFIX）

> 用途：记录**代码里已经存在、但怀疑有问题或属半成品**的地方。与 `docs/TODO.md` 的分工是：
> **TODO = 功能不存在**；**UFIX = 功能存在但存疑**。
>
> 生成时间：2026-11-01（批次 A-3）。证据均为只读 `grep` / `sed`，未运行 git 命令。
> 每条格式：文件路径 + 一句话简述 + 当前已知证据 + 影响范围 + 处置建议。

| # | 主题 | 类型 | 严重度 |
|---|---|---|---|
| 1 | `MaxEntries` 文档值与代码值不符 | 文档偏差 / 代码回退未清 | 中 |
| 2 | AOT 探针「26 项」表述不严谨 | 文档偏差 | 低 |
| 3 | `ProcessRefresher` STA→MTA 的历史与理由 | 决策记录缺失 | 低 |
| 4 | `MaskStore.Changed` 在 API 快照里的可检索性 | 契约检索陷阱 | 中 |
| 5 | `docs/` 下五份旧文档的去向（A-3 已处置） | 文档债 | 中 |
| 6 | `UI_Contract.md` §4.2 / §4.3 仍含电源字样 | 文档债（已裁决暂不清理） | 低 |
| 7 | A-2 删除电源类的三条连带影响 | 变更留痕 | 低 |
| 8 | WinUI 主题资源键的 WPF 重做 | 历史记录（不构成约束） | 低 |
| 9 | Worker 启动即真实写 SMU | 设计后果 / 调试陷阱 | 中 |
| 10 | `Global\` 互斥体在多用户场景下导致第二用户无法使用 | 架构缺陷（已确认单用户，暂不修） | 中 |
| 11 | 诊断客户端留在临时目录 | 工具位置 / 可复现性 | 低 |
| 12 | `LogService` 手工构造且不设 `Directory` 时日志落到 `%TEMP%`（批次四补入） | 易漏配置 / 日志不可追溯 | 中 |
| 13 | 任务计划与 ui.json 短暂不一致 | 产品缺陷 / 状态不一致 | 中 |
| 14 | UI 侧 IPC 超时 10 秒 vs Worker 侧 schtasks 最坏 30 秒 | 产品缺陷 / 超时不匹配 | 中 |
| 15 | `Zentwik.UI/Assets/Zentwik.ico` 未被 git 跟踪，CI 下 ISCC 编译失败、发布链断 | 发布链配置缺陷 / 已确认 | 高 |

---

## 1. `MaxEntries`：文档写 320，代码是 200

- **文件**：`Zentwik.Core/Services/LogOptions.cs`
- **简述**：`CHANGELOG.md`（1.0.4）声称日志条数上限从 500 收紧到 **320**（并给出「320 × 2KB ≈ 640KB 恒定」的内存推算），但代码实际是 200。
- **证据**：
  - `LogOptions.cs:26`：`public int MaxEntries { get; set; } = 200;`
  - `LogOptions.cs:65` 附近的 `Normalize()` 兜底同样是 **200**（不是 320）。
  - `CHANGELOG.md` [1.0.4] Changed：「日志内存三处收紧：条数上限 500 → **320**…（320 × 2KB ≈ 640KB 恒定）」。
  - 批次 A-1 已实测核对过一次，结论一致：代码 200，文档 320。
- **影响范围**：日志内存上限的实际值与文档差 120 条（约 240KB）；用户按文档推算内存会偏差 37%。不导致功能错误，只导致文档不可信。
- **待确认**：是**文档笔误**（当时就写成 200，CHANGELOG 写错），还是**代码回退未清**（改到 320 后又被某次合并覆盖回 200）。仓库无 git 可比对时，无法自证。
- **处置建议**：由人类确认产品预期值，然后**单向对齐**（要么改代码到 320，要么改 CHANGELOG 到 200）。确认前不要动。

## 2. AOT 探针「26 项检查全绿」表述不严谨

- **文件**：`CHANGELOG.md`（2.0.0-can2）、`build/Zentwik.AotProbe/Program.cs`、`docs/AotProbe-覆盖清单.md`
- **简述**：CHANGELOG 写「26 项检查全绿」，但探针里 `smu.construct` 与 `cpuset.topology` 各是 `try` / `catch` 互斥双分支，**不可能同时为真**，「26 项同时全绿」这个状态永不存在。
- **证据**：
  - `grep -c 'Check("' build/Zentwik.AotProbe/Program.cs` → **25**（A-2 删除 4 条 `power.*`、新增 3 条 `ipc.*` 之后）。
  - `grep -o 'Check("[a-z.A-Z]*"' … | sort | uniq -c` → `smu.construct` 出现 **2** 次、`cpuset.topology` 出现 **2** 次，其余各 1 次 → 唯一检查名 **23** 个。
  - 运行输出恒为 **23 行**、`failures=0`。批次 A-2 实测输出已贴进交付记录。
  - A-2 之前的同一口径是：24 项唯一检查名 / 26 个判定分支 / 输出 23 行。
- **影响范围**：后续判断「某条核心路径有没有 AOT 覆盖」时，会误以为 26 项可以同时全绿，从而把「23 行输出」当成缺了 3 项。
- **处置建议**：后续文档统一写「**N 项唯一检查、M 个判定分支**」，不写「N 项全绿」。当前值：25 个调用点 / 23 个唯一名 / 23 行输出。

## 3. `ProcessRefresher` 的 STA→MTA：语义对齐，不是修 bug

- **文件**：`Zentwik.Core/Services/ProcessRefresher.cs`
- **简述**：批次二把采样线程的单元状态从 STA 改成 MTA，但**实测两种都能跑**，改动的正当性不在「修 bug」，而在语义对齐。
- **证据**：
  - 当前实现：`ProcessRefresher.cs:371` `thread.SetApartmentState(ApartmentState.MTA);`
  - 批次 A-1 真机实测（临时改回 STA 的对照组）：两种单元状态**都能跑完采样、零异常、零 WARN**，帧数、进程数与遍历结果一致。
  - 因此可支持的结论只有「**MTA 不坏**」，不能推出「MTA 更好」。
- **真正的改动理由**：该线程**没有消息泵**。STA 线程在没有消息泵时调用 COM 会因跨单元封送而死锁；MTA 线程无此约束。将来若有人在这条采样线程上碰 COM（例如 `WmiLight` 之外的 WMI 对象、Shell API），无消息泵的 STA 会直接挂死而非报错。改成 MTA 是为了**把这种未来故障排除在架构层面之外**。
- **影响范围**：后续若有人质疑「为什么改 MTA」或「为什么不保留 STA」，此条可查。改动本身**不影响**当前任何功能路径。
- **处置建议**：保留 MTA，并在本文件留下记录，不再重复实测。

## 4. `MaskStore.Changed` 在 API 快照里的可检索性

> **【与任务书描述不同，已按实测改写】** 任务书假设「`MaskStore.Changed` 事件本身没进 API 快照」。实测**不成立**：事件声明在快照里有两处，只是没有 `MaskStore.Changed` 这种全限定写法，按全限定字符串搜索会 0 命中。

- **文件**：`Zentwik.Core/Helpers/IMaskStore.cs`、`Zentwik.Core/Helpers/MaskStore.cs`、`docs/Core-API-契约-批次二冻结.txt`、`docs/Core-契约变更-批次二.md`
- **简述**：批次 A-1 已把 `Helpers/` 并入快照扫描范围，`Changed` 事件的**签名**在快照里可以查到；但快照按「文件:行:成员声明」记录，**不做 `类型.成员` 全限定索引**，所以按 `MaskStore.Changed` 搜是空的。
- **证据（原始输出）**：

```
$ grep -n "MaskStore.Changed" docs/Core-API-契约-批次二冻结.txt
（无输出）
命中数: 0

$ grep -n "event Action" docs/Core-API-契约-批次二冻结.txt
515:Zentwik.Core/Helpers/MaskStore.cs:79:        public event Action? Changed;
631:Zentwik.Core/Helpers/IMaskStore.cs:21:         event Action? Changed;
```

  - 实现类在 `MaskStore.cs:79`，接口在 `IMaskStore.cs:21`，**两条都进了快照**（段 1 与段 2 各一条，与 Services 下接口的既有双计口径一致）。
  - `Core-契约变更-批次二.md` §1.7 另有散文说明「按契约判断」，但那份文件不是签名清单。
- **影响范围**：批次四写 `IMaskStore.Changed += …` 时，若按 `MaskStore.Changed` 搜索快照会误判为「契约里没有」，进而去读实现类、绑到具体类型上，破坏面向接口注入。**订阅应面向 `IMaskStore`，签名以 `IMaskStore.cs:21` 为准。**
- **处置建议**：不必改快照格式（改格式会破坏与批次二的 diff 可比性）。在快照头部「用法说明」里补一句「本快照按 `文件:行` 记录，引用成员时请搜成员名而非 `类型.成员`」，或由批次四的 Agent 自觉按成员名搜索。

## 5. `docs/` 下五份旧文档的去向（A-3 已处置）

- **文件**（处置前路径）：`docs/全局交接文档.md`、`docs/项目规范与开发手册.md`、`docs/UI-设计规范.md`、`docs/代码审查报告-v1.0.4.md`、`docs/AI与开发者上手指南.md`
- **简述**（处置前状态）：五份文档分属三个时代（旧名 Zenwik 的 WPF 单进程期、WinUI3 期、当前重构期），其中至少四份与双进程架构不符，但都没有明确作废标记，也没有归档。
- **证据（逐份）**：

| 文件 | 首行标题 | 实际状态 |
|---|---|---|
| `全局交接文档.md` | Zenwik 全局交接文档（**v1.2.1**） | 旧名 `Zenwik`；解决方案写 `Zenwik.slnx`（`Zenwik` + `Zenwik.Core`）；**WPF 单进程架构**。无一处提到双进程或 IPC |
| `项目规范与开发手册.md` | Zenwik 项目规范与开发手册 (v1.2.1) | 旧名，版本停在 1.2.1，早于 1.0.4 之后的全部改动 |
| `UI-设计规范.md` | Zenwik UI 设计规范与实现标准 (v1.0.2 Final) | 文件自己已声明「**本规范已废弃（WPF 时代）**」，并给出与当前实现不符的具体项（强调色 `#38BDF8`、卡片圆角 10、侧栏 208px） |
| `代码审查报告-v1.0.4.md` | 代码审查报告 · v1.0.4-rc1 | 历史报告，审查对象是 tag `v1.0.4-rc1` 的单进程代码；审查环境为 Linux 沙箱，无 Windows 运行验证 |
| `AI与开发者上手指南.md` | Zentwik 上手与开发指南 | **型号最新（2026-11-01 校准），但内容仍是 WinUI3 路线**：第 14 行「形态：WinUI 3 原生桌面应用」，第 23 行「UI 框架 WinUI 3（WindowsAppSDK 2.6.4）」 |

- **影响范围**：批次四的 Agent 若把 `AI与开发者上手指南.md` 当权威上手入口，会按 WinUI3 的 XAML 方言与 `WMC0011` 类告警规则写 WPF 代码，产出无法编译的 XAML。前四份的危害小一些（明显是旧名/旧版本），但同样会误导「当前架构是什么」。
- **处置结果（A-3 收尾已执行）**：五份的去向如下。
  - `docs/AI与开发者上手指南.md` → **已移至 `Legacy/docs/`**。其中与 WinUI3 路线无关、对双进程架构仍然适用的段落已抽取为 **`docs/开发指南.md`**（新文件），后续以 `docs/开发指南.md` 为入口，不再引用原文件。
  - `docs/全局交接文档.md` → **已移至 `Legacy/docs/`**。
  - `docs/UI-设计规范.md` → **已移至 `Legacy/docs/`**。随「现有 UI 设计不强求」的新约束调整定位：从「已废弃但仍在 `docs/`」改为历史参考，与其余历史文档同处一处；文件内容未改。
  - `docs/项目规范与开发手册.md` → **保留在 `docs/` 未动**（工程规范类，尚未逐节评估）。
  - `docs/代码审查报告-v1.0.4.md` → **保留在 `docs/` 未动**（历史报告，无路径引用问题）。

## 6. `UI_Contract.md` §4.2 / §4.3 仍含电源字样

- **文件**：`UI_Contract.md`
- **简述**：批次 A-2 已删除电源类并把 §2.3、§4.1 改成状态说明，但 §4.2「同步的草稿语义」与 §4.3「步进命令」里仍有描述性的「电源策略 / 核心停放」字样。
- **证据**：`UI_Contract.md:192`（「一次读全：SMU 核心状态 + 电源策略」）、`:198`（覆盖范围含「核心停放滑杆、电源计划 / 升频模式下拉」）、`:200`（`IsDraftDirty` 依赖「SMU 与电源策略都读到真值」）、`:225`（步进表「核心停放 ±5%」行）、`:235`（停放步进受 `IsCoreParkingSupported` 闸门约束）。
- **影响范围**：这五处**不点名已删的类**，属描述性文字；照字面实现会绑到不存在的属性上。但文件顶部已有「状态说明（批次 A-2 后）」，且 §2.3 / §4.1 已声明该能力移除，误读风险有限。
- **处置建议**：**本轮不清理**（已裁决）。逐字清理性价比低，待批次四 UI 重写时整份重新定义契约。

## 7. A-2 删除电源类的三条连带影响

> 由 `docs/AotProbe-覆盖清单.md` 末尾「A-2 删除电源类的影响记录」搬入（第 1~3 条），原文保留在该清单并已加「已作为 UFIX 条目搬入 docs/UFIX.md」标注。

1. **`ScenarioService` 与电源类完全解耦，删除不波及场景逻辑。** `ScenarioRule` 只由 `Trigger`（`ProcessRunning` / `OnBattery` / `OnAcPower`）、`Argument`、`ProfileId`、`Priority`、`Enabled` 构成，命中后统一走 `ProfileService.Apply(target)`，**不存在任何电源动作分支**。因此 A-2 任务书里「把电源动作改成 TODO 分支加日志」当时没有可改对象；`PowerStatusWatcher` 与 `OnBattery` / `OnAcPower` 两个 trigger 均保留。
2. **`UI_Contract.md` 的三处电源类引用已删除，整份契约待批次四重写。** 删除位置：原第 24 行、原第 102 行、原第 138 行。§2.3「电源策略」整节依赖这些条款，删除后已改写为状态说明（未留空章节）；§4.1 同法处理；文件顶部另加「状态说明（批次 A-2 后）」。该文件自 A-2 起仅作历史参考。
3. **AotProbe 的 25 个判定分支正常输出 23 行、全部 PASS。** `smu.construct` 与 `cpuset.topology` 各是 `try` / `catch` 互斥双分支，每次运行只走一条，故调用点 25、输出 23 行、`failures=0`。判据以「25 个调用点全绿且 `failures=0`」为准。

## 8. WinUI 主题资源键的 WPF 重做（历史记录，不构成约束）

- **文件**：`Legacy/docs/AI与开发者上手指南.md` §3.3「滑块尺寸」表（原 `docs/AI与开发者上手指南.md` 第 122-128 行）
- **简述**：原 UI 用 5 个 **WinUI 原生主题资源键**覆盖滑块外观。这些键是 WinUI 控件模板的资源覆盖点，WPF 没有同名键，也无法用同一机制改滑块外观；要复刻得整份重写控件模板。

| 键 | 值 | WinUI 中的作用 |
|---|---|---|
| `SliderTrackThemeHeight` | 2 | 轨道高度 |
| `SliderHorizontalThumbWidth` / `Height` | 14 | 滑块尺寸 |
| `SliderPreContentMargin` / `PostContentMargin` | 12 | 轨道两端留白 |

- **影响范围**：仅当批次四要复刻旧滑动条的视觉密度时才有参考意义。
- **新约束**：**现有 UI 设计不强求，批次四 UI 从零设计，不要求对齐这些数值。**
- **处置建议**：不做任何迁移。**此条仅作历史参考，不构成约束。** 批次四若需要滑块外观，按新设计自行定义。

## 9. Worker 启动会真实写 SMU（`ApplyTuningOnStartup = true`）

- **文件**：`Zentwik.Worker/BackendHostService.cs`（`StartAsync` 第 4 步）、`Zentwik.Core/Services/TuningApplyService.cs:69`（`ApplyFromStore`）、`Zentwik.Core/Helpers/UiSettingsStore.cs`（`ApplyTuningOnStartup` 默认 `true`）
- **简述**：Worker **每次启动**都会执行 `TuningApplyService.ApplyFromStore()`。在 `ApplyTuningOnStartup` 为 `true`（默认值）且 `tuning.json` 里有已保存值时，这是**真实写入 SMU**（逐核 Curve Optimizer + FMax），不是只读探测。
- **证据（批次三验收实测，`%AppData%\Zentwik\Logs\worker-20261101.log`）**：

```
[INFO] 启动应用调校：读取到已保存配置，CO 16 核，FMax 4750 MHz
[INFO] CO 写入开始：16 核，每 CCD 8 核，SMU 型号 TYPE_CPU4，版本 0x4621A00
[INFO]   核  0 → core=0 ccd=0 ccx=0 mask=0x00000000 值=-18 接受=True
...
[INFO] 启动应用调校：CO 16 核写入并回读一致
[INFO] FMax 写入：请求 4750，SMU 接受 True，回读 4750（基线 4750）
```

- **影响范围**：
  1. 任何「起 Worker 测一下」的调试、验收、冒烟都会改动真实硬件状态。批次三验收期间因此实际写入了 7 次（值与原配置相同，回读一致，硬件状态未改变）。
  2. 若机器上的 `tuning.json` 是旧配置，启动 Worker 会把它重新写进 SMU，**覆盖用户之后手动做的调整**。
  3. 没装 PawnIO 时，`ApplyFromStore` 会走 `ISmuReader.WaitForAvailability(60)` 的轮询路径，最长阻塞 60 秒；该调用在 `BackendHostService.StartAsync` 内同步执行，会连带把管道监听（下一个 HostedService）一起推迟。**本机 PawnIO 可用，此路径未实测**，结论来自代码阅读。
- **处置建议**：测试/调试时临时把 `%AppData%\Zentwik\ui.json` 的 `ApplyTuningOnStartup` 改为 `false`；后续可考虑给 Worker 加 `--dry-run` 启动参数（只 Load 不写 SMU）。**本轮不改**——这是产品行为，不是缺陷。

## 10. `Global\` 互斥体在多用户场景下导致第二用户无法使用（批次三收尾补入）

- **文件**：`Zentwik.Core/Ipc/WorkerPipeContract.cs:62`（`WorkerMutexName = @"Global\Zentwik.Worker.SingleInstance"`）、`Zentwik.Worker/Program.cs`（`!createdNew` 分支）
- **简述**：A 用户先起 Worker 持有 `Global\Zentwik.Worker.SingleInstance`，B 用户登录后 B 的 UI 连不上 A 的管道，B 拉起 Worker 又因 Global mutex 已存在而退出，B 用户完全无法使用。
- **根源**：**`Global\` 前缀使互斥体跨用户可见，与管道 DACL 机制无关。** `CurrentUserOnly` 和显式 `PipeSecurity` 在跨用户场景下效果相同——两者都只允许当前用户 SID 连接，B 用户都连不上 A 的管道。换哪种 DACL 机制都不解决跨用户问题。
- **两个问题必须分开看**：
  - 显式 DACL 解决的是「**同用户、跨完整性级别**」——提权 Worker ← 非提权 UI。**已解决**（见 `docs/项目事实卡.md` §6.5）。
  - `Global\` 互斥体问题是「**跨用户**」。**未解决**。
  - 两者独立：把 DACL 换回 `CurrentUserOnly` 不会让多用户场景变好，只会把「同用户跨完整性级别」也一起弄坏。
- **证据**：批次三实测只覆盖同一用户（本机单用户会话）。跨用户路径**未实测**，本条由代码（`Global\` 前缀）与 Windows 对象命名空间语义推出。
- **处置建议**：已确认单用户，优先级低（`docs/项目事实卡.md` §3.4 第 11 条）。如未来要支持多用户，需要**三处联动改**：互斥体命名（去掉 `Global\` 或加用户维度）、管道命名（当前 `Zentwik.Worker`，不含用户区分）、任务计划注册逻辑（当前单一任务名）。只改一处会得到「A 能用、B 完全不能用」的半成品。

## 11. 诊断客户端留在临时目录（批次三收尾补入）

- **路径**：`C:\Users\ZMotu\AppData\Local\Temp\zwclient\`（`zwclient.exe`）
- **能力**：`zwclient.exe ping` / `shutdown` / `kill`。`kill` 需以提权方式运行——非提权的 `Stop-Process` 对提权 Worker 无效（`Access denied`）。
- **用途**：批次三管道权限矩阵实验与验收复验用的临时工具，**不在仓库内**，不受版本管理。
- **风险**：`%TEMP%` 可能被清理工具或系统维护删除，该路径不可依赖。
- **处置建议**：批次四 Agent 需要复验管道时**自己写脚本**（`NamedPipeClientStream` 连 `Zentwik.Worker`、写一行 JSON、读一行），不要假设该路径存在。

## 12. `LogService` 手工构造且不设 `Directory` 时日志落到 `%TEMP%`（批次四补入）

> **【与任务书描述不同，已按实测改写】** 任务书假设「任何新宿主忘记设置都会踩这个坑，建议在 `AddZentwikCore` 的默认配置里把 `Directory` 设为 `AppPaths().Logs`」。实测**不成立**：`AddZentwikCore` 已经这么做了。本条的真实波及面比任务书描述窄，但**依然存在**，且恰好命中了批次四的 UI 进程。

- **文件**：`Zentwik.Core/Services/LogOptions.cs:63`、`Zentwik.Core/Services/LogService.cs:99`、`Zentwik.Core/DependencyInjection/ServiceCollectionExtensions.cs`（`TryAddSingleton<LogService>` 工厂）、`Zentwik.UI/App.xaml.cs`（`Log` 静态字段）
- **简述**：`LogOptions.Normalize()` 在 `Directory` 为空时回落到 `Path.GetTempPath()`，而 `LogService` 构造函数直接使用 `Normalize()` 的结果（`LogDirectory = _options.Directory`）。`%TEMP%` 会被系统清理工具 / 存储感知删除，日志丢失后不可追溯。
- **实测的三条链路，只有第三条会踩坑**：

| 宿主 | 构造方式 | 实际落盘 | 结论 |
|---|---|---|---|
| Worker | `AddZentwikCore(cfg => cfg.FilePrefix = "worker-")`，**未设 `Directory`** | `%AppData%\Zentwik\Logs\worker-*.log` | ✅ 安全 |
| AotProbe / 其他 DI 宿主 | 同 Worker，走 `AddZentwikCore` | `%AppData%\Zentwik\Logs\` | ✅ 安全 |
| 批次四 UI | `public static LogService Log { get; } = new(new LogOptions { FilePrefix = "zentwik-" })` —— **绕过 DI 手工 new** | 若只写 `FilePrefix`：`%TEMP%` | ⚠️ 会踩坑 |

- **根因**：`AddZentwikCore` 的工厂里有这一句兜底（`ServiceCollectionExtensions.cs`）：
  ```csharp
  if (string.IsNullOrWhiteSpace(options.Directory))
      options.Directory = provider.GetRequiredService<IAppPaths>().Logs;
  ```
  所以**走 DI 的宿主永远不会落到 `%TEMP%`**。坑只存在于「手工 `new LogService(...)`」这一条路上 —— 而 `LogOptions.Normalize()` 是 public 的，`LogService` 也是 public 的，任何宿主都可以绕过 DI 直接 new（UI 的静态字段就是这么写的：静态字段初始化早于容器构建，拿不到 `IAppPaths` 服务实例）。
- **证据**：批次四实测，UI 日志 `zentwik-20261102.log` 与 Worker 日志 `worker-20261102.log` 同处 `%AppData%\Zentwik\Logs`，前提是 UI 侧显式写了 `Directory = new AppPaths().Logs`。未写的那一版未实测（按 `Normalize()` 代码推定落 `%TEMP%`）。
- **影响范围**：日志丢失后，用户报障时拿不到现场；且「两个进程日志同目录」这条排障前提被破坏，而表现是**静默的**（日志照写，只是写错地方）。
- **处置建议（三条，按性价比排序）**：
  1. **文档侧（已做）**：任何绕过 DI 手工构造 `LogService` 的宿主，必须显式 `Directory = new AppPaths().Logs`。批次四 UI 已按此实现。
  2. **代码侧（推荐，未做）**：把 `LogOptions.Normalize()` 的兜底从 `Path.GetTempPath()` 改成 `new AppPaths().Logs`，让「忘了设」的默认结果也是产品目录，而不是临时目录。改动面 1 行，但会让 `LogOptions` 依赖 `AppPaths`（同程序集、同 `Helpers` 命名空间，无新增引用），且现有测试若断言了临时目录回落会一起红。
  3. **不推荐**：在 `AddZentwikCore` 里再写一次 —— 那里已经有了，重复写不会覆盖到手工 new 的路径。

## 13. 任务计划与 ui.json 短暂不一致（批次五补入）

- **文件**：`Zentwik.UI/MainWindow.xaml.cs`（`OnAutoStartChanged`）、`Zentwik.Core/Helpers/UiSettingsStore.cs`（`Save`）、`Zentwik.Worker/PipeServerService.cs`（`RunAutoStartAsync`）
- **简述**：IPC 返回 `Success=true`（任务已注册）但 `UiSettingsStore.Save()` 抛异常时，内存和 `CheckBox` 回滚了，任务计划还在。用户看到开关是关的，但任务计划是开的；下次启动 UI 读 `ui.json` 的 `AutoStart: false`，不会自动注销任务，用户以为没开自启但开机时 Worker 还是会起来。
- **证据**：批次五实现，`MainWindow.xaml.cs` 的 `catch (Exception saveEx)` 分支只做「回滚内存 + 回滚 CheckBox + `Log.Warn` + MessageBox」，没有任何撤销任务计划的动作。该分支在批次五验收中**未被触发**（`Save()` 正常），属代码审查结论而非实测。
- **影响范围**：三处状态（内存 / ui.json / 任务计划）不一致，且不一致的方向是「用户以为关了，实际开着」—— 比反过来更难发现，因为界面上完全看不出异常，只有开机时 Worker 自己起来。
- **修复方向（三个）**：
  ① `Save()` 失败时发反向 IPC 撤销注册。改动中等，前提是 Worker 侧 `Disable()` 可靠（它已经过批次五验收：注册→注销各一轮，`Get-ScheduledTask` 确认任务消失）。
  ② 改为先写文件成功后再发 IPC。与当前顺序相反，需要评估 IPC 失败时的回滚成本（此时任务未建，但 ui.json 已写 `true`）。
  ③ `Save()` 失败时异步重试一次。`Save()` 失败可能是暂时的——文件被锁、磁盘满、权限短暂不足；重试一次成功则三处状态一致，不需要撤销任务计划；重试仍失败再走方向 ①。
- **推荐方向 ③**：改动最小，且不改变当前「IPC 优先」的顺序。
- **实现方向 ③ 时注意**：`Save()` 失败后**不要立即回滚**——先延迟重试（如 200ms），重试成功则保持 `desired` 不回滚；重试失败再走方向 ① 并回滚。

## 14. UI 侧 IPC 超时 10 秒 vs Worker 侧 schtasks 最坏 30 秒（批次五补入）

- **文件**：`Zentwik.UI/MainWindow.xaml.cs`（`SendAsync` 的 `TimeSpan.FromSeconds(10)`）、`Zentwik.Core/Services/AutoStartService.cs`（`Enable()` / `RunSchtasks`）
- **简述**：`AutoStartService.Enable()` 内部 `schtasks /Create` 15 秒 + 回查 15 秒，最坏 30 秒。UI 侧 `SendAsync` 超时 10 秒，极端情况下 UI 先报失败、任务实际已建，用户看到「设置失败」弹窗但任务已注册，开关回滚到关，实际是开的。
- **证据**：`AutoStartService.cs` 的 `RunSchtasks` 超时常量为 15 秒（超时后返回 `(-1, "schtasks 超时（15s）未返回")`），`Enable()` 内先 `/Create` 再 `IsEnabled()` 回查，两次都可能各耗 15 秒；UI 侧固定 10 秒。批次五正常路径实测约 1 秒内返回（`01:24:01` 创建、`01:24:06` 删除，每次操作间隔 5 秒含人工操作时间），最坏路径未复现。
- **影响范围**：与第 13 条同源——都是「界面状态与任务计划真实状态不一致」，但触发条件是超时而不是保存失败。用户看到失败弹窗后重试，可能得到「任务已存在」的幂等成功，状态才重新对齐。
- **修复方向（两个）**：
  ① UI 侧超时改为 35 秒。
  ② Worker 侧 `Enable()` 改为异步返回 + 后续查询命令（需要新增 IPC 命令）。

---

## 15. `Zentwik.UI/Assets/Zentwik.ico` 未被 git 跟踪，CI 下发布链断（批次五补入，2026-11-02 核实）

- **文件**：`Zentwik.UI/Assets/Zentwik.ico`（`build/installer/Zentwik.iss` 的 `SetupIconFile` 指向它）
- **简述**：安装包图标在磁盘上存在（批次五第六件从 `Legacy/WPF/Zentwik/Assets/Zentwik.ico` 复制，32571 字节），但**未被 git 跟踪**。CI 干净 checkout 后该文件不在工作树 → `Zentwik.UI.csproj:41` 的 `ApplicationIcon` 指向空路径 → MSBuild 把它作为 `Win32Icon` 传给 C# 编译器（`Microsoft.CSharp.CurrentVersion.targets:319`）→ 编译器报错（`Error opening icon file`）→ `release.yml:45-47` 的 `Publish UI` 步骤失败 → **便携 zip 链当前断在此处**（已确认，非假设）。**CI 不运行 ISCC**，所以断的不是安装包编译，而是便携 zip。
- **证据**：
  - 磁盘存在：`ls -la Zentwik.UI/Assets/Zentwik.ico` → 32571 字节（批次五第六件复制产物，交付物五有记录）。
  - 未跟踪：`git ls-files --error-unmatch Zentwik.UI/Assets/Zentwik.ico` → `error: pathspec ... did not match any file(s) known to git`。
  - `Zentwik.iss:36`：`SetupIconFile=..\..\Zentwik.UI\Assets\Zentwik.ico`，路径相对于 `build/installer/`，ISCC 读源码树——这是**本地编译安装包**时对同一文件的第二处依赖，与 CI 断点无关。
  - 根 `.gitignore`（562 字节）第 34–40 行仅忽略 `*.png/*.jpg/*.jpeg/*.bmp/*.gif`，且 `!Assets/**` 反而放行 `Assets/` 下一切；**无 `*.ico` 忽略规则、无排除 `Assets/` 的规则** → 「`.gitignore` 排除」原因可排除。
  - 结论：未跟踪的原因是批次五 Agent 按红线「不执行 git 命令」复制后**未 `git add`**，属原因 ①，非原因 ②。
- **影响范围**：CI / 他人 clone 后 `dotnet publish Zentwik.UI` 直接失败，**便携 zip 与安装包都出不来**；本地开发机因文件已在磁盘而能正常编译，掩盖了问题（典型「本地能过、CI 红」）。与事实卡 §1.3 第 ① 项对应。
- **修复方式（先确认原因再动手）**：
  1. `git check-ignore -v Zentwik.UI/Assets/Zentwik.ico` 确认无忽略规则（已核实为无）。
  2. 直接 `git add Zentwik.UI/Assets/Zentwik.ico` 并提交即可，**无需 `-f`、无需改 `.gitignore`**。
  3. 若未来 `git check-ignore` 真命中某条规则，再按规则决定改 `.gitignore` 还是 `git add -f`。
  - **此修复为便携 zip 链的第一优先级。** 批次六收尾已授权执行 `git add Zentwik.UI/Assets/Zentwik.ico`（退出码 0），**待本次 commit/push 后 CI 生效**。

- **【批次六核实补充】安装包链不在 CI 内：`release.yml` 不产 `dist/portable/`、不调 ISCC（属已裁决的设计状态）**（本条原先只覆盖 ico 一处，容易让人以为补完 `git add` 就能出安装包——实际不能，安装包需本地出）：
  - **文件**：`.github/workflows/release.yml`、`build/installer/Zentwik.iss:61`、`build/zip_portable.py`
  - **简述**：`release.yml` 的两条 `dotnet publish` 都输出到 `-o ./publish`（`:47`、`:51`），随后 `Compress-Archive -Path ./publish/*`（`:55`）打便携 zip。**没有任何步骤把产物写进 `dist/portable/`，也没有任何步骤调用 ISCC。** 而 `Zentwik.iss:61` 的 `Source: "..\..\dist\portable\*"` 与 `build/zip_portable.py` 的 `SRC = "dist/portable"` 都以该目录为输入——该目录当前只有 `.gitkeep`。因此即使 ico 补齐，CI 仍不产出安装包；本地编译 `.iss` 前必须先手工把 `publish/*` 填进 `dist/portable/`，否则得到的是不含任何 exe 的空壳。
  - **证据**：
    - `grep -niE "iscc|inno|choco|winget|setup" .github/workflows/release.yml` → 唯一命中是 `:28 - name: Setup .NET`，**无 ISCC / Inno / choco / winget**。
    - `runs-on: windows-latest`（`:19`）——浮动标签，未 pin 镜像版本，镜像是否预装 Inno Setup **无法从仓库侧确认**。
    - `ls -la dist/portable/` → 仅 `.gitkeep`（165 字节），无 exe / dll。
    - `build/zip_portable.py:7` `SRC = "dist/portable"`、`:24` `OUT = "dist/Zentwik-" + VERSION + "-portable-win-x64.zip"` → 该脚本**读目录、写 zip**，本身不填充目录。
  - **影响范围**：`dist/portable/` 是「publish 产物」与「zip / iss 输入」之间的中转目录，无人填充使两端都落空——这是**安装包链**的问题（便携 zip 走 `./publish/*`，不经该目录）。与事实卡 §1.3「两处断点」表第 ② 行对应。
  - **裁决（批次六收尾）**：CI **只**产出便携 zip（`./publish/*` → zip），**不编译安装包**；安装包本地出——手工把 `publish/*` 复制到 `dist/portable/`，再本地运行 ISCC 编译 `Zentwik.iss`。`release.yml` **不加 staging 步骤、不调用 ISCC**。因此本条记录的 `dist/portable/` 为空属**已裁决的设计状态，非缺陷**。ico 已 `git add`，待本次 commit/push 后 CI 生效。
