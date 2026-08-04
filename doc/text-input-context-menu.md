# 输入框自定义右键菜单实现说明

## 背景与目标

WPF 的 `TextBox`、`RichTextBox` 和 `PasswordBox` 在未设置 `ContextMenu` 时，会显示框架提供的默认编辑菜单，其外观接近 Windows 系统菜单，与 Prometheus 的圆角卡片、主题颜色和交互状态不一致。

本次实现的目标是：

- 为应用内文本输入控件提供统一的右键菜单。
- 保留 WPF 原有的撤销、剪切、复制、粘贴、删除和全选行为。
- 自动适配明暗主题与中英文切换。
- 自动覆盖当前及后续模块新增的输入框，避免在每个页面重复配置。
- 不覆盖控件已经显式声明的自定义 `ContextMenu`。

## 整体流程

```text
App.OnStartup
    │
    └─ TextInputContextMenu.Register()
           │
           ├─ 注册 TextBoxBase.Loaded 类事件
           └─ 注册 PasswordBox.Loaded 类事件
                    │
                    └─ 控件加载
                          ├─ 已有 ContextMenu：保持原样
                          └─ 没有 ContextMenu：创建 Prometheus 风格菜单
                                  ├─ 绑定 WPF 编辑命令
                                  ├─ 引用动态语言资源
                                  └─ 应用主题样式资源
```

## 应用级接入方式

核心逻辑位于 [`TextInputContextMenu.cs`](../src/Prometheus/TextInputContextMenu.cs)。应用启动时，[`App.xaml.cs`](../src/Prometheus/App.xaml.cs) 调用：

```csharp
TextInputContextMenu.Register();
```

`Register()` 使用 `EventManager.RegisterClassHandler` 监听 `TextBoxBase` 和 `PasswordBox` 的 `Loaded` 事件。这样不需要修改每一个视图，也不会覆盖 HandyControl 的 `TextBoxExtend` 等文本框样式。

注册过程通过 `Interlocked.Exchange` 保证只执行一次，避免重复注册类事件。

控件加载后，仅当 `ContextMenu` 为 `null` 时才安装默认菜单。因此，页面以后如果需要业务专用菜单，可以直接在对应控件上声明 `ContextMenu`，应用级菜单不会覆盖它。

## 菜单功能

| 控件类型 | 菜单项 |
| --- | --- |
| `TextBoxBase` 及其派生控件 | 撤销、剪切、复制、粘贴、删除、全选 |
| `PasswordBox` | 粘贴、全选 |

密码框不提供复制和剪切，避免通过菜单读取密码内容。

菜单项直接使用 WPF 路由命令：

- `ApplicationCommands.Undo`
- `ApplicationCommands.Cut`
- `ApplicationCommands.Copy`
- `ApplicationCommands.Paste`
- `EditingCommands.Delete`
- `ApplicationCommands.SelectAll`

每个菜单项的 `CommandTarget` 都指向被点击的输入控件。菜单项是否可用由 WPF 的 `CanExecute` 自动判断，例如：

- 没有选中文本时，剪切和复制自动禁用。
- 只读输入框的剪切、粘贴和删除自动禁用。
- 没有可撤销操作时，撤销自动禁用。
- 剪贴板没有可粘贴内容时，粘贴自动禁用。

右侧显示的 `Ctrl+Z`、`Ctrl+C` 等文字用于提示快捷键；实际编辑行为仍由 WPF 命令系统处理，没有重复实现文本编辑逻辑。

## 视觉样式

菜单样式定义在 [`PageLayout.xaml`](../src/Prometheus.Core/Resources/Styles/PageLayout.xaml)，分为三个独立资源：

| 资源键 | 用途 |
| --- | --- |
| `TextInputContextMenuStyle` | 菜单容器、背景、边框、圆角和阴影 |
| `TextInputMenuItemStyle` | 菜单项布局、悬停状态和禁用状态 |
| `TextInputMenuSeparatorStyle` | 菜单分隔线 |

样式使用现有动态主题资源，包括：

- `RegionBrush`
- `BorderBrush`
- `PrimaryTextBrush`
- `SecondaryTextBrush`
- `SecondaryRegionBrush`
- `PrimaryBrush`
- `EffectShadow2`

因为这些资源通过 `DynamicResource` 引用，用户切换浅色、深色或跟随系统主题后，菜单会自动更新颜色，无需重新创建。

菜单项采用“图标、标题、快捷键”三列布局。图标优先使用 `Segoe Fluent Icons`，并以 `Segoe MDL2 Assets` 作为兼容回退字体，从而避免额外引入图片资源。

## 多语言处理

菜单文字位于以下语言资源字典：

- [`zh-CN.xaml`](../src/Prometheus.Core/Resources/Languages/zh-CN.xaml)
- [`en-US.xaml`](../src/Prometheus.Core/Resources/Languages/en-US.xaml)

资源键统一使用 `TextInput.ContextMenu.*` 前缀：

```text
TextInput.ContextMenu.Undo
TextInput.ContextMenu.Cut
TextInput.ContextMenu.Copy
TextInput.ContextMenu.Paste
TextInput.ContextMenu.Delete
TextInput.ContextMenu.SelectAll
```

代码通过 `SetResourceReference` 设置菜单标题，而不是在创建菜单时读取一次字符串。因此，语言资源字典被替换后，已经创建的菜单也会自动刷新文字。

## 扩展方式

新增通用菜单项时：

1. 在 `zh-CN.xaml` 和 `en-US.xaml` 中同时添加语言资源键。
2. 在 `CreateTextBoxMenu` 或 `CreatePasswordBoxMenu` 中添加对应命令项。
3. 尽量继续使用 WPF 路由命令，让命令系统负责可用状态和执行目标。
4. 如果操作不是标准编辑行为，再考虑引入自定义命令或事件处理。

如果某个页面需要完全不同的菜单，可直接为该输入控件声明本地 `ContextMenu`。应用级加载处理检测到已有菜单后会跳过。

自定义输入控件只有在继承 `TextBoxBase` 或 `PasswordBox` 时才会自动接入；其他类型需要显式安装菜单，或在 `Register()` 中增加对应的类事件注册。

## 验证结果

实现完成后执行了 Release 构建和测试：

```powershell
dotnet build src/Prometheus.slnx -c Release
dotnet test src/Prometheus.slnx -c Release
```

验证结果：

- Release 构建通过。
- 59 个自动化测试全部通过。
- 中英文菜单资源键保持一致。
- 原有页面无需逐个修改，现有显式右键菜单不会被覆盖。
