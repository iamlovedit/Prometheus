# 自动更新规范

> Prometheus 使用 GitHub Actions 发布 `win-x64` self-contained 桌面程序。客户端直接访问公开仓库的 GitHub Releases API，通过 Release 的稳定版 Tag 判断是否存在更新；用户明确点击下载后，客户端使用默认浏览器打开对应 Tag 的 GitHub Release 页面。更新链路不依赖 Vercel、R2、自建更新 API 或客户端内置 GitHub Token。

## 范围

- 自动更新仅支持 `stable` 通道和 `win-x64`。
- 稳定版 Tag 必须使用 `v<major>.<minor>.<patch>` 格式，例如 `v1.2.3`；版本比较时去除 `v` 前缀。
- GitHub Release 的 `tag_name` 是远端版本的唯一来源，本地程序集版本是当前版本的唯一来源。
- 客户端只负责检查更新和打开 GitHub Release 页面，不在应用内下载、校验、解压或安装更新包。
- 用户可在 Release 页面选择 MSI 或便携 ZIP；现有 MSI 安装用户优先使用 MSI 手动升级。
- 用户数据、日志、资源缓存和更新下载缓存必须位于 `%LocalAppData%\Prometheus`，不得被应用更新覆盖或在卸载时删除。

## GitHub Release 协议

- 客户端必须请求 GitHub REST API：

  ```text
  GET https://api.github.com/repos/<owner>/<repository>/releases/latest
  ```

- 请求必须包含 GitHub REST API 要求的 `User-Agent`，并使用 `Accept: application/vnd.github+json`。
- 仓库所有者和仓库名必须由构建配置提供；正式构建不得依赖用户配置 Vercel 地址、自建 API 地址或 GitHub 访问令牌。
- 自动更新仅支持公开仓库。不得在桌面客户端中嵌入 GitHub Personal Access Token、Actions Token 或其他发布凭据。
- Release 必须满足以下条件，否则不得作为更新使用：
  - `draft` 为 `false`；
  - `prerelease` 为 `false`；
  - `tag_name` 严格匹配 `v<major>.<minor>.<patch>`；
  - 存在且仅存在一个名为 `Prometheus-<version>-win-x64.zip` 的 Release Asset；
  - ZIP Asset 的 `digest` 是合法的 `sha256:<64 位十六进制摘要>`，或者存在且仅存在一个名为 `Prometheus-<version>-win-x64.zip.sha256` 的 SHA-256 校验文件；
  - 若 Release 包含上述 SHA-256 校验文件，则不得存在重复同名 Asset；
  - ZIP Asset 和可选 SHA-256 校验文件的下载地址使用 HTTPS，并且是当前仓库、当前 Tag 的 GitHub Release 下载地址。
- 客户端按三段数字版本比较本地版本和 `tag_name`。只有远端版本严格高于本地版本时才判定为有更新；相同版本或较低版本均视为无更新。
- Release 标题和正文仅用于展示发布说明，不得用于版本判定、文件名拼接之外的路径操作或命令执行。
- 客户端应使用响应中的 `ETag` 发起条件请求，避免重复消耗 GitHub API 限额。`304 Not Modified` 必须复用最近一次已验证的 Release 元数据，并重新与当前本地版本比较。
- GitHub API 返回 `403`、`429`、`5xx`、超时、无可用 Release 或格式不合法时均视为检查失败，不得错误报告为“已是最新版本”。

## 检查与交互

- 主程序加载后延迟 15 秒自动检查一次更新；设置页提供手动检查。
- 单次应用运行期间不得周期性高频轮询 GitHub；除用户手动触发外，只执行一次启动检查。
- 自动检查失败不得打断应用启动或弹出阻塞对话框；手动检查失败必须显示可理解且已本地化的错误。
- 检测到新版本后，主导航的设置入口必须显示“有更新”提示。
- 启动自动检查发现更新时不得自动弹出更新对话框、打开浏览器或开始下载，只更新“有更新”提示。
- 设置页未发现更新时按钮显示“检查更新”；已知存在更新时按钮显示“前往 GitHub 下载”。
- 用户在设置页明确点击更新后，客户端必须使用系统默认浏览器打开对应版本的 GitHub Release 页面，不显示应用内更新对话框。
- 打开 Release 页面后“有更新”提示继续保留，直到重新检查确认无更新，或应用以新版本启动。
- 无法启动默认浏览器时必须在设置页显示可理解且已本地化的错误，不得导致应用退出。

## Release 页面与下载

- Release 页面地址必须由已验证的仓库所有者、仓库名和稳定版 Tag 构造，格式为 `https://github.com/<owner>/<repository>/releases/tag/v<version>`。
- Release 页面只能在用户明确点击设置页更新按钮后打开；启动自动检查不得主动启动浏览器。
- 客户端不得直接请求 Release Asset 下载地址，不得创建 `.part` 文件，也不得在后台下载 ZIP 或 MSI。
- 文件下载、断点续传和保存位置由用户的默认浏览器负责；客户端不得向浏览器或 GitHub 发送安装 ID、硬件标识、用户数据或仓库凭据。
- 用户应根据 Release 页面提供的 SHA-256 信息自行校验便携 ZIP；MSI 的下载和运行由用户明确操作。

## 手动安装

- 客户端不得自动退出、启动安装器、覆盖程序文件或重启应用。
- 用户下载 MSI 后由用户明确启动安装；安装器负责关闭正在运行的 Prometheus 并执行 Major Upgrade。
- 便携 ZIP 由用户在关闭 Prometheus 后手动解压和替换，不得覆盖 `%LocalAppData%\Prometheus` 下的用户数据。
- 下载或安装失败由浏览器或安装器向用户报告，客户端只负责显示检查更新和打开 Release 页面阶段的错误。

## 发布

- WPF 主程序以 self-contained、非 single-file、多文件方式发布，并压缩为 `Prometheus-<version>-win-x64.zip`。
- ZIP 解压后的根目录必须包含可直接运行的 `Prometheus.Desktop.exe`，不要求用户预装 .NET Runtime。
- 发布流水线必须为 ZIP 生成 `Prometheus-<version>-win-x64.zip.sha256`，内容使用 SHA-256 十六进制摘要，并将 ZIP、校验文件和 MSI 附加到同一个 GitHub Release。
- 同一发布目录通过 WiX Toolset 打包为 `Prometheus-<version>-win-x64.msi`。
- MSI 以当前用户范围安装到 `%LocalAppData%\Programs\Prometheus`，不得要求管理员权限；安装界面允许选择安装目录、桌面快捷方式和登录 Windows 后自动启动，桌面快捷方式默认启用，自动启动默认关闭，完成页允许立即启动应用。
- MSI 卸载或执行 Major Upgrade 前必须关闭正在运行的 Prometheus；静默卸载不得因应用仍在运行而遗留程序文件或要求重启。
- 推送 `v<major>.<minor>.<patch>` Tag 时创建或更新同名 GitHub Release。稳定版 Release 不得标记为 Draft 或 Prerelease。
- 稳定版 Tag 是发布版本的唯一来源；流水线去除 `v` 前缀后通过 `RELEASE_VERSION` 注入 MSBuild，程序集、ZIP、SHA-256 文件和 MSI 必须使用同一版本。
- GitHub Release 正文必须来自 `CHANGELOG.md` 中与 Tag 版本完全匹配的 `## [<version>] - <date>` 章节；创建 Release、重新运行发布流水线或在 `master` 更新 changelog 时均同步该章节。缺少对应章节或章节为空时必须终止发布。
- 发布流水线不得依赖 Vercel 配置、R2 凭据、更新签名密钥或自建更新 API 地址。
- 本地非发布构建使用 `Directory.Build.props` 中的默认版本，并允许通过测试配置覆盖 GitHub 仓库坐标，不得访问真实发布仓库完成单元测试。

## 验收标准

- 推送稳定版 Tag 后，GitHub Release 中存在对应的 ZIP、ZIP SHA-256 校验文件和 MSI，且程序集和 MSI 内部版本与 Tag 去除 `v` 前缀后的版本一致。
- GitHub Release 正文与 `CHANGELOG.md` 中对应版本章节一致；已有 Release 重新运行发布流水线后，标题、正文和资产均同步更新，`master` 中已发布版本的 changelog 变化也会自动同步标题和正文。
- 客户端直接请求 GitHub Releases API，并通过响应的 `tag_name` 判断版本；更新检查链路不请求 Vercel、R2 或自建更新 API。
- 远端版本高于本地版本且 Release Asset 完整时报告有更新；版本相同或更低时报告无更新。
- Draft、Prerelease、非法 Tag、缺少或重复 ZIP、缺少可用 SHA-256 来源、重复校验文件、摘要来源互相冲突或非法下载地址均不得报告为可下载更新。
- 启动自动检查发现更新时只显示“有更新”提示，不弹出对话框、不打开浏览器、不下载文件。
- 设置页点击更新后使用默认浏览器打开与已验证 Tag 完全对应的 GitHub Release 页面，不显示应用内更新对话框。
- 客户端不创建更新 `.part` 文件、不下载 ZIP/MSI、不启动更新程序，也不自动退出或重启。
- MSI 支持图形安装以及 `/qn` 静默安装；安装和卸载后文件、开始菜单快捷方式与安装器注册表状态一致，卸载不得删除 `%LocalAppData%\Prometheus` 用户数据。
- Prometheus 正在运行时，MSI 卸载必须先结束应用进程，再删除程序文件和快捷方式。
- 自动检查网络失败不打断应用启动；手动检查显示可理解的本地化错误；GitHub API 限流不得被显示成“已是最新版本”。
- 自动或手动检查发现新版本后设置菜单显示本地化提示，设置页按钮切换为本地化的 GitHub 下载文案。
- 中英文资源键保持对齐，默认浏览器、ZIP 和 MSI 的职责边界符合本规范。
