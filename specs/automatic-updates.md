# 自动更新规范

> Prometheus 使用 GitHub Actions 发布 `win-x64` self-contained 桌面程序。客户端直接访问公开仓库的 GitHub Releases API，通过 Release 的稳定版 Tag 判断是否存在更新，并从同一 Release 下载完整 ZIP。自动更新链路不依赖 Vercel、R2、自建更新 API 或客户端内置 GitHub Token。

## 范围

- 自动更新仅支持 `stable` 通道和 `win-x64`。
- 稳定版 Tag 必须使用 `v<major>.<minor>.<patch>` 格式，例如 `v1.2.3`；版本比较时去除 `v` 前缀。
- GitHub Release 的 `tag_name` 是远端版本的唯一来源，本地程序集版本是当前版本的唯一来源。
- 应用内更新只使用完整便携 ZIP，不使用 MSI、增量包或多个版本之间的链式升级。
- MSI 仅用于首次安装、手动版本升级和卸载，不得作为应用内自动更新下载目标。
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
  - 存在且仅存在一个名为 `Prometheus-<version>-win-x64.zip.sha256` 的 SHA-256 校验文件；
  - ZIP Asset 的下载地址使用 HTTPS，并且是 GitHub Release 下载地址。
- 客户端按三段数字版本比较本地版本和 `tag_name`。只有远端版本严格高于本地版本时才判定为有更新；相同版本或较低版本均视为无更新。
- Release 标题和正文仅用于展示发布说明，不得用于版本判定、文件名拼接之外的路径操作或命令执行。
- 客户端应使用响应中的 `ETag` 发起条件请求，避免重复消耗 GitHub API 限额。`304 Not Modified` 必须复用最近一次已验证的 Release 元数据，并重新与当前本地版本比较。
- GitHub API 返回 `403`、`429`、`5xx`、超时、无可用 Release 或格式不合法时均视为检查失败，不得错误报告为“已是最新版本”。

## 检查与交互

- 主程序加载后延迟 15 秒自动检查一次更新；设置页提供手动检查。
- 单次应用运行期间不得周期性高频轮询 GitHub；除用户手动触发外，只执行一次启动检查。
- 自动检查失败不得打断应用启动或弹出阻塞对话框；手动检查失败必须显示可理解且已本地化的错误。
- 检测到新版本后，主导航的设置入口必须显示“有更新”提示。
- 普通更新允许稍后处理；用户选择稍后处理后提示继续保留，直到重新检查确认无更新，或安装后应用以新版本启动。
- 设置页和更新对话框的进度条及其上方状态文字仅在下载、校验、已准备安装和启动安装状态显示；检查更新、无更新、仅发现版本和失败状态不得显示空进度条或对应状态文字。
- 发布说明可展示 GitHub Release 的正文，但必须作为纯文本或受限 Markdown 渲染，不得执行其中的 HTML、脚本或自定义 URI。

## 下载与完整性校验

- 客户端从匹配 ZIP Asset 的 `browser_download_url` 直接下载，允许跟随 GitHub 到其官方 Release Asset CDN 的 HTTPS 重定向。
- 下载写入 `%LocalAppData%\Prometheus\updates\<version>\` 下的 `.part` 文件，完成校验后再原子重命名为最终 ZIP 文件名。
- 下载必须支持取消。服务器支持 HTTP Range 时应支持断点续传；服务器不接受 Range 时必须安全地从头重新下载。
- 续传前必须确认目标版本、Asset 名称和已知文件大小没有变化。任何一个值变化时必须丢弃旧 `.part` 文件并重新下载。
- SHA-256 校验文件只允许包含目标 ZIP 的 64 位十六进制摘要和可选文件名。文件名存在时必须与目标 ZIP Asset 名称完全一致。
- ZIP 下载完成后必须校验 GitHub API 提供的 Asset 大小和 SHA-256。大小或摘要不匹配时不得进入安装阶段，并删除不可信的最终文件。
- ZIP 条目路径必须是规范化相对路径，拒绝绝对路径、驱动器路径、空段、`.`、`..`、NUL、符号链接和任何逃逸目标目录的路径。
- 更新检查和下载代码不得向非 GitHub API、GitHub Release 下载地址或其官方重定向目标发送仓库之外的凭据、安装 ID、硬件标识或用户数据。

## 安装与恢复

- 应用内更新必须由独立更新进程执行，主 WPF 进程不得在自身仍运行时覆盖其程序集或模块 DLL。
- 主程序下载并校验 ZIP 后，将更新程序复制到 `%LocalAppData%\Prometheus` 的临时目录，从临时副本启动安装，并传入明确的父进程 PID、目标版本、ZIP 路径和安装目录。
- 更新程序必须等待传入的父进程 PID 退出，禁止只按进程名等待。
- 更新内容必须先解压到同一磁盘上的暂存目录。完整解压、路径校验以及入口文件版本校验全部成功后，才能替换当前程序文件。
- ZIP 根目录必须包含可运行的 `Prometheus.Desktop.exe`，其程序集版本必须等于 Release Tag 去除 `v` 前缀后的版本。
- 替换前必须创建可恢复的上一版本备份；安装成功并确认新版本能够启动后才可删除多余备份，最终只保留一个回滚版本。
- 新桌面进程启动后必须写入健康标记；60 秒内未成功写入、进程提前退出或目标版本校验失败时，更新程序必须恢复上一版本并重新启动旧程序。
- 更新不得覆盖 `%LocalAppData%\Prometheus` 下的用户数据、日志、资源缓存和下载缓存。
- 安装失败、目标版本校验失败或新版本启动失败时，更新程序必须恢复上一版本并重新启动旧程序。
- 更新完成后应清理对应版本的 `.part` 文件和暂存目录；已验证 ZIP 可在确认新版本健康后删除。

## 发布

- WPF 主程序以 self-contained、非 single-file、多文件方式发布，并压缩为 `Prometheus-<version>-win-x64.zip`。
- ZIP 解压后的根目录必须包含可直接运行的 `Prometheus.Desktop.exe`，不要求用户预装 .NET Runtime。
- 发布流水线必须为 ZIP 生成 `Prometheus-<version>-win-x64.zip.sha256`，内容使用 SHA-256 十六进制摘要，并将 ZIP、校验文件和 MSI 附加到同一个 GitHub Release。
- 同一发布目录通过 WiX Toolset 打包为 `Prometheus-<version>-win-x64.msi`。
- MSI 以当前用户范围安装到 `%LocalAppData%\Programs\Prometheus`，不得要求管理员权限；安装界面允许选择安装目录、桌面快捷方式和登录 Windows 后自动启动，桌面快捷方式默认启用，自动启动默认关闭，完成页允许立即启动应用。
- MSI 卸载或执行 Major Upgrade 前必须关闭正在运行的 Prometheus；静默卸载不得因应用仍在运行而遗留程序文件或要求重启。
- 推送 `v<major>.<minor>.<patch>` Tag 时创建或更新同名 GitHub Release。稳定版 Release 不得标记为 Draft 或 Prerelease。
- 稳定版 Tag 是发布版本的唯一来源；流水线去除 `v` 前缀后通过 `RELEASE_VERSION` 注入 MSBuild，程序集、ZIP、SHA-256 文件和 MSI 必须使用同一版本。
- 发布流水线不得依赖 Vercel 配置、R2 凭据、更新签名密钥或自建更新 API 地址。
- 本地非发布构建使用 `Directory.Build.props` 中的默认版本，并允许通过测试配置覆盖 GitHub 仓库坐标，不得访问真实发布仓库完成单元测试。

## 验收标准

- 推送稳定版 Tag 后，GitHub Release 中存在对应的 ZIP、ZIP SHA-256 校验文件和 MSI，且程序集和 MSI 内部版本与 Tag 去除 `v` 前缀后的版本一致。
- 客户端直接请求 GitHub Releases API，并通过响应的 `tag_name` 判断版本；更新检查链路不请求 Vercel、R2 或自建更新 API。
- 远端版本高于本地版本且 Release Asset 完整时报告有更新；版本相同或更低时报告无更新。
- Draft、Prerelease、非法 Tag、缺少或重复 ZIP、缺少或重复校验文件、非法下载地址均不得进入下载阶段。
- ZIP 大小或 SHA-256 不匹配、下载被中断、ZIP 路径非法或入口程序集版本与 Tag 不一致时不得替换当前版本。
- 下载支持取消，并在服务器支持 Range 时能够从有效 `.part` 文件继续下载。
- 更新安装期间主程序 DLL 不会被运行中的进程锁定；安装失败或新版本启动失败后旧版本仍可启动。
- MSI 支持图形安装以及 `/qn` 静默安装；安装和卸载后文件、开始菜单快捷方式与安装器注册表状态一致，卸载不得删除 `%LocalAppData%\Prometheus` 用户数据。
- Prometheus 正在运行时，MSI 卸载必须先结束应用进程，再删除程序文件和快捷方式。
- 自动检查网络失败不打断应用启动；手动检查显示可理解的本地化错误；GitHub API 限流不得被显示成“已是最新版本”。
- 自动或手动检查发现新版本后设置菜单显示本地化提示，更新进度条及其状态文字的可见性与实际更新阶段一致。
- 中英文资源键保持对齐，ZIP 和 MSI 的职责边界符合本规范。
