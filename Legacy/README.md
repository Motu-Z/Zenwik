# Legacy — 归档代码，仅供查阅

本目录存放已弃用的客户端源码，**仅作为历史参考，不参与任何编译**。

## 内容

| 路径 | 说明 |
|---|---|
| `WPF/Zentwik/` | 旧版 WPF 客户端（已被 `Zentwik.UI` 取代，UI 全部从零重写） |
| `WinUI3/Zentwik.App/` | 已放弃的 WinUI 3 客户端 |

## 规则

1. 本目录下的工程**不进入** `Zentwik.slnx`，不参与 CI，不参与打包与发布。
2. **禁止**任何 `csproj` 通过 `ProjectReference` 或 `Reference` 引用本目录内容。
3. **禁止**把本目录的 XAML、ViewModel 或任何 UI 逻辑搬迁到新工程；新 UI 必须从零编写。
4. 本目录是**移动**归档而非重命名复用：原工程保持原样，不得被改造成新工程。

## 为什么这里有一个空的 Directory.Build.props

MSBuild 查找 `Directory.Build.props` 时会向上遍历父目录并在**第一个命中处停止**。本目录放置一个空文件，
即可切断 `Legacy` 子树对仓库根目录（以及将来任何上层目录）属性的继承，避免归档工程在误构建时
被套上新的 TargetFramework、分析器或包版本，产生与归档代码无关的编译噪音。
