# AI 手记 / 交接变更记录（AI Handoff Notes）

> 用途：写给**后续接手 Zentwik 的 AI 与开发者**。每轮实质性改动在此追加一条记录，并维护下方「当前红线」清单，避免新 AI 踩坑或静默回归。
> 权威上手入口见同目录 `AI与开发者上手指南.md`；唯一法定契约为仓库根目录 `UI_Contract.md`。

---

## 2026-11-01 本轮变更（breeze 设计移植 + 度量对齐 + 文档 + 发布）

### 做了什么
1. **breeze-shell 设计语言移植**：把朋友项目 breeze-shell（C++ Canvas + React 渲染器，技术栈与 WinUI3 零兼容，**仅做视觉/设计移植，代码零复用**）的设计语言搬进 Zentwik 主题层。
   - 强调色改为初音未来青 **`#39C5BB`（Miku cyan）**，全链路 token 化（改根级一处即全联动）。
   - `Controls.xaml` 补齐 **17 个缺失基元组件**：`AppPageHeader`+3 子样式、`AppInfoBar`+3 severity、`AppExpander`、`AppSegmentedControl`+`AppSegmentedItem`、`AppChoiceCard`、`AppProgressBar`、`AppSettingsCard`+2 子样式、`AppToggleSwitch`。
   - 新增 `AppComboBox.cs`（自绘下拉宽度对齐 + 关闭态文本通道）。
2. **修复三处编译错误**（移植 WIP 引入）：
   - `Controls.xaml`：`Rectangle` 无 `CornerRadius` → 改用 `RadiusX`/`RadiusY`。
   - `AppComboBox.cs`：WinUI3 的 `ComboBox` 无 `OnSelectionChanged` 可重写虚方法 → 改为在构造函数订阅 `SelectionChanged` 事件。
   - `Tokens.xaml`：强调色三键原用独立 `<Color>` 元素触发 WMC0011/后续 XLS0414 → 全部改为 `SolidColorBrush` 的 `Color="#..."` 属性（**见下方红线 1**）。
3. **对齐 breeze 度量**：`Tokens.xaml` 尺寸区与 `Controls.xaml` 新基元组件的硬编码像素对齐 breeze 实测值（来源 `breeze-shell-0.2.0/src/shell/script/ts/src/config_page/{constants.ts,components/Fluent.tsx}`）。
4. **补写统合文档**：新建 `docs/AI与开发者上手指南.md`（技术栈/契约驱动模式/设计系统 token 表/字体阶梯/组件清单/XAML 红线/提交流程），并给旧 `docs/UI-设计规范.md`（WPF 旧规范）加废弃提示。
5. **发布**：快进推送 `origin/main` 至 `6da5f11`，打注解 tag **`v2.0.0-can24`**（→ `6da5f11`）。

### 改动文件清单
| 文件 | 本轮性质 |
|---|---|
| `Zentwik.App/Themes/Tokens.xaml` | 强调色 token 化 + 度量对齐（卡片圆角 8→7、下拉圆角 6→4、Combo/输入高 34→32、内容内边距 40,32→28,24、卡片内边距 20,24→16,14；新增 `AppSegmentedCornerRadius=6`） |
| `Zentwik.App/Themes/Controls.xaml` | 新增 17 个基元样式 + 硬编码度量对齐 breeze（页头标题 20→28、描述 13→12、InfoBar 圆角 8→6、分段项内圆角 3→4、ComboBox 弹层圆角 4、设置卡间距 12→8） |
| `Zentwik.App/Controls/AppComboBox.cs` | 自绘下拉 + 订阅 SelectionChanged（移除不可重写的 override） |
| `Zentwik.App/ViewModels/TuningViewModel.cs` | 调校页逻辑收尾 |
| `docs/AI与开发者上手指南.md` | 新建：权威上手入口 |
| `docs/UI-设计规范.md` | 追加废弃提示（WPF 旧规范，勿照此实现） |

### 提交与 tag
- `0e42134` feat(theme): 移植 breeze 设计语言，引入初音青强调色与缺失基元控件（+442/−55）
- `6da5f11` style(theme): 对齐 breeze-shell 设计度量并补写统合上手文档（+228/−18）
- `v2.0.0-can24` → `6da5f11`（已推送 GitHub）

---

## 当前红线（后续 AI 必须守住，勿静默妥协）

1. **禁止独立 `<Color>` 元素 / `media:` 别名**：WinUI3 该工具链下，`<Color x:Key=...>` 报 WMC0011 伪告警，`xmlns:media="using:Microsoft.UI.Xaml.Media"` + `<media:Color>` 直接 XLS0414 硬错。**一律用 `<SolidColorBrush Color="#...">`**。
2. **强调色改一处即全联动**：任何 XAML **不要硬编码 `#39C5BB` 或其他强调色 Hex**。改主色只改根级 token（`SystemAccentColor` / `SystemAccentColorBrush` / `AppAccentBrush` 等 `AppAccent*` 系），下游全部 `ThemeResource` 引用自动跟随。
3. **度量一律走 token**：圆角/内边距/控件高度全部引用 `Tokens.xaml` 的 token（`AppCardCornerRadius` / `AppControlCornerRadius` / `AppComboCornerRadius` / `AppSegmentedCornerRadius` / `AppContentPadding` / `AppCardPadding` / `AppControlHeight` / `AppComboHeight` / `AppInputHeight` …），**不要硬编码像素**。新组件若 breeze 有对应度量，先对齐 breeze 实测值。
4. **前后端分离边界仍有效**（即便当前单 AI 作业）：`UI_Contract.md` 是唯一法定契约。前端只改 `.xaml`，后端只改 `.cs`，互不越界；严禁发明契约中不存在的属性/命令。
5. **编译红线 0 警告 0 错误**：提交前必须 VS 重新生成确认错误列表清零（WMC0011 之类伪告警也要清）。注释只写底层原理/接口约束，不写解释字面意思的废话。
6. **备份目录是安全网，勿动**：`.ai_backup_20261101/`（物理备份此轮主题层与 WIP）由用户要求保留，勿删勿改。
7. **推送约定**：新改动先提交到 `feat/logic`；合并 main 走 `git push origin HEAD:refs/heads/main` 快进（非空推、不回退）；发版打 `v2.0.0-canN` 注解 tag 并 `git push origin <tag>`。`build/zip_portable.py` 已移出追踪，勿重新 add。

---

## 当前仓库状态（2026-11-01 末）
- `feat/logic` == `origin/main` == `6da5f11`
- tag：`v2.0.0-can24` → `6da5f11`
- 故意未提交：` .ai_backup_20261101/`、`build/zip_portable.py`

## 相关文档索引
- `UI_Contract.md`（根目录）— 唯一法定契约，数据绑定/命令名称的法定依据
- `docs/AI与开发者上手指南.md` — 权威上手入口（设计系统 token 表 / 字体阶梯 / 组件清单 / XAML 红线 / 提交流程）
- `docs/UI-设计规范.md` — **已废弃**，WPF 旧规范（强调色 `#38BDF8`、圆角 10、侧栏 208px），与现状矛盾，勿照此实现
- `docs/全局交接文档.md` / `WPF封版与WinUI3重构交接文档.md` / `S5-前后端分离开局提示词.md` — 历史背景，按需查阅
