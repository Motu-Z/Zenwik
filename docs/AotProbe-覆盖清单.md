# Zentwik.AotProbe 覆盖清单

- 生成时间：2026-11-01（批次 A-2：A4 删电源类 → A6-1 补 IPC 覆盖）
- 依据文件：`build/Zentwik.AotProbe/Program.cs`
- 行号以本批次改动后的文件为准；改动 Program.cs 后本清单必须同步更新

## 总量口径

| 口径 | 数量 | 说明 |
|---|---|---|
| Check 调用点（判定分支） | **25** | 源码里 `Check(...)` 的静态调用点数量 |
| 运行时输出行数 | **23** | `smu.construct` 与 `cpuset.topology` 各有 success / exception 两个互斥分支，每次运行只走其中一条 |
| 实际删除 | 4 条（含 1 条 Skip 分支） | 见文末「已删除的检查项及删除理由」 |

> 26（A4 前） − 4 + 3（A6-1 新增 IPC）= 25。这个 25 是**调用点**数量，不是输出行数；
> 把「25 个 Check 全部 PASS」理解成「输出 25 行」会与 try/catch 双分支的结构冲突。

## 检查项清单

| # | 名称 | 行号 | 覆盖的核心路径 |
|---|---|---|---|
| 1 | `json.roundtrip` | 93 | `AppJsonContext.Default.UiSettings` 源生成序列化往返，验证 source-gen 上下文未被裁剪 |
| 2 | `json.pascalCase` | 99 | 落盘键名保持 PascalCase —— 老配置能被读出来的形状前提 |
| 3 | `log.counters` | 124 | `LogService` 分级计数：Info 5 / Warn 1 / Error 1 / Critical 1 |
| 4 | `log.capacity` | 128 | 内存条数上限（`MaxEntries = 200`）生效 |
| 5 | `log.file` | 140 | 后台写盘线程产出 `.log` 文件（Flush 后轮询 40×25 ms 容忍尾部延迟） |
| 6 | `log.snapshot` | 146 | `Snapshot()` 条数与 `Count` 一致（只读快照语义） |
| 7 | `ipc.roundtrip` | 170 | `PipeRequest` 经 `IpcJsonContext.Default.PipeRequest` 序列化再反序列化，`Version` / `Command` 往返一致 |
| 8 | `ipc.versionMatch` | 178 | `ValidateVersion(ProtocolVersion)` 返回 true 且 `error` 为空串 |
| 9 | `ipc.versionMismatch` | 194 | `ValidateVersion(0 / 2 / -1)` 全部拒绝，且三条文案都等于 `VersionMismatchMessage` |
| 10 | `power.priorityLabel` | 206 | `ProcessPriority.FromBasePriority` 文案映射（与电源无关，名称沿用历史） |
| 11 | `process.snapshot` | 229 | `ProcessRefresher.Sample()`：Toolhelp32 进程快照 + 源生成 P/Invoke |
| 12 | `process.nameMarshaling` | 232 | 进程名封送：定长缓冲区 → `string` |
| 13 | `process.pathMarshaling` | 235 | `QueryFullProcessImageNameW` 路径解析成功率 |
| 14 | `process.pathShape` | 240 | 路径形状（`\\?\` 前缀或绝对路径）；仅当存在已解析路径样本时执行 |
| 15 | `di.paths` | 258 | `IAppPaths` 注册与根目录解析 |
| 16 | `di.log` | 262 | `ILogService` 注册并成功写入一条 |
| 17 | `di.maskStore` | 264 | `IMaskStore` 加载（masks.json） |
| 18 | `di.bindingStore` | 266 | `IBindingStore` 加载（bindings.json） |
| 19 | `di.tuningStore` | 267 | `ITuningStore` 加载（tuning.json） |
| 20 | `di.uiSettings` | 268 | `IUiSettingsStore` 加载（ui.json） |
| 21 | `di.autoStart` | 274 | `AutoStartService` 仅构造（覆盖 `GetOEMCP` / `ConsoleEncoding` 静态初始化），不调 `IsEnabled` 以免拉起 schtasks |
| 22 | `smu.construct`（成功分支） | 282 | `SmuReader` 构造成功：PawnIO 驱动可打开 |
| 23 | `smu.construct`（异常分支） | 287 | 同一构造的异常路径：类型被裁剪 / 驱动不可用时在此暴露 |
| 24 | `cpuset.topology`（成功分支） | 295 | `CpuSetBinder.ResetTopology()` + `IsCcdAvailable(0)` |
| 25 | `cpuset.topology`（异常分支） | 299 | 同一路径的异常分支 |

## 双分支说明（不能同时全绿）

`smu.construct`（22 / 23）与 `cpuset.topology`（24 / 25）各自的两条分支是 **success / exception 互斥**关系：

- 每次运行只有一条分支执行，另一条永远不会出现；
- 因此运行输出恒为 23 行，而调用点是 25 个；
- 两条分支**都写成 `Check(...)`**，异常既不会被静默吞掉，也不会被误记成跳过。

其余条件分支：`process.pathShape` 依赖快照里存在已解析路径的样本；若整机没有任何可解析路径，该行不输出，不计入失败。

## 已删除的检查项及删除理由

A4 删除 `Zentwik.Core/Services/ProcessorPowerPolicy.cs`（523 行）与 `ProcessorPowerTuning.cs`（404 行）后，以下 4 条检查项的验证对象已不存在：

| 原名称 | 删除前位置 | 原验证对象 |
|---|---|---|
| `power.activeScheme` | 原 Program.cs:156 | `ProcessorPowerPolicy.IsAvailable()` 读取当前电源方案 |
| `power.detect` | 原 Program.cs:161 | `ProcessorPowerPolicy.TryDetect()` 档位识别 |
| `power.boost` | 原 Program.cs:166 | `ProcessorPowerTuning.TryReadBoost()` 升频模式（PERFBOOSTMODE） |
| `power.coreParking` | 原 Program.cs:170（Check）与 172（Skip） | `ProcessorPowerTuning.TryReadCoreParking()` 核心停放百分比 |

删除理由：验证对象已随 A4 从 Core 移除，**直接删除检查项**，不做「验证类型不存在」这类反向检查 —— 那条断言只能证明「我删干净了」，不能证明任何运行时行为，属于自证式检查。

连带删除：`Skip(name, detail)` 辅助方法（唯一使用点是 `power.coreParking` 的失败分支），随之移除，避免留下零调用的死代码。当前 25 条检查项全部走 `Check`，不存在 `SKIP` 输出。

保留：`power.priorityLabel`（第 10 条）。它验证的是 `ProcessPriority` 的文案映射，与电源策略无关，仅名称沿用历史。

## A-2 删除电源类的影响记录

本小节记录 A4（删除 `Zentwik.Core/Services/ProcessorPowerPolicy.cs`、`ProcessorPowerTuning.cs`）对探针以外区域的连带影响，供后续批次追溯。A-3 建立 `docs/UFIX.md` 时，以下三条应作为 UFIX 条目搬入。

1. **ScenarioService 与电源类完全解耦，删除不波及场景逻辑。** `ScenarioRule` 只由 `Trigger`（`ProcessRunning` / `OnBattery` / `OnAcPower`）、`Argument`、`ProfileId`、`Priority`、`Enabled` 构成，命中后统一走 `ProfileService.Apply(target)`，**不存在任何电源动作分支**。因此 A-2 任务书里「把电源动作改成 TODO 分支加日志」在本轮没有可改对象；`PowerStatusWatcher` 与 `OnBattery` / `OnAcPower` 两个 trigger 均保留未动。

2. **`UI_Contract.md` 的三处电源类引用已删除，整份契约待批次四重写。** 删除位置：原第 24 行（反向生成来源清单中的两个类文件）、原第 102 行（§2.3 表格 `IsCoreParkingSupported` 行的 `TryReadCoreParking` 赋值说明）、原第 138 行（§3 集合表 `BoostOptions` 行的 `ProcessorPowerTuning.BoostOptions` 来源说明）。§2.3「电源策略」整节依赖这些条款，删除后已改写为一句状态说明，未留空章节；文件顶部另加「状态说明（批次 A-2 后）」。**该文件自本批次起仅作历史参考**，完整 UI 契约在 `Zentwik.UI` 重写时整份重新定义。

3. **25 个判定分支运行期正常输出 23 行、全部 PASS。** 差异来源为 `smu.construct` 与 `cpuset.topology` 各是 `try` / `catch` 互斥双分支，每次运行只走一条，故 `Check(...)` 调用点 25 个、实际打印 23 行、`failures=0`。判定口径以「25 个调用点全绿且 failures=0」为准，不得以「输出行数 25」作为回归判据。

4. **`UI_Contract.md` 的 §4.2 同步草稿语义（第 192/198/200 行）和 §4.3 步进表（第 225/235 行）仍含「电源策略 / 核心停放」字样**，本轮未清理，待批次四 UI 重写时统一重写。这两节不点名电源类名，属描述性文字，逐字清理性价比低。

> 以上第 1~3 条**已作为 UFIX 条目搬入 `docs/UFIX.md` 第 7 条**（原文在此保留，便于与 A-2 交付记录对照）。
