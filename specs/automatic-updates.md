# 自动更新规范

> 当前发布阶段：GitHub Actions 构建 `win-x64` self-contained 桌面程序，同时将便携 ZIP 和首次安装用 MSI 附加到 GitHub Release。R2、签名 Manifest、Bootstrapper、增量包和应用内自动更新发布链路暂缓实现；下述自动更新协议保留为后续阶段的目标规范。

## 范围

Prometheus 首版自动更新仅支持 `stable` 通道和 `win-x64`。应用以可写便携目录部署，根目录 Native AOT Bootstrapper 负责启动、安装、健康检查和回滚；WPF 桌面程序位于版本目录。

```text
Prometheus/
├─ Prometheus.exe
├─ current.json
├─ versions/<version>/Prometheus.Desktop.exe
└─ .staging/
```

用户数据、日志、资源缓存和下载缓存必须位于 `%LocalAppData%\Prometheus`，不得进入版本 Manifest。

## 协议与可信边界

- 协议版本固定为 `1`；版本号使用稳定版三段式 `major.minor.patch`。
- 发布描述符、频道索引和完整文件 Manifest 使用 ECDSA P-256、IEEE P1363 签名。
- 签名信封包含 Base64Url 编码的精确 UTF-8 JSON payload 与签名；验证签名前不得信任 payload 中的对象键、版本或哈希。
- R2 保持私有。Vercel 只能为签名描述符已经授权的对象生成预签名 URL，不提供任意对象签名接口。
- 所有包必须同时校验描述符签名、对象大小和 SHA-256；目标目录完成后必须按完整 Manifest 再次逐文件校验。
- ZIP 条目和 Manifest 路径必须是规范化相对路径，拒绝绝对路径、驱动器路径、空段、`.`、`..`、NUL 和逃逸目标根目录的路径。

## 检查与下载

- 主程序加载后延迟 15 秒检查一次更新；设置页提供手动检查。
- 普通更新允许稍后处理；当前版本低于 `minimumSupportedVersion` 时必须更新或退出。
- 检测到可用版本后，主导航的设置入口必须显示“有更新”提示；用户选择稍后处理时提示继续保留，直到确认无更新，或安装后应用退出并以新版本重启。
- 设置页和更新对话框的进度条及其上方状态文字仅在下载、已准备安装和启动安装状态显示；检查更新、无更新、仅发现版本和失败状态不得显示空进度条或对应状态文字。
- API 无更新返回 HTTP 204；有更新返回签名发布描述符及目标 Manifest、首选包、完整包兜底和可选 Bootstrapper 的预签名 URL。
- 安装 ID 是持久化在 LocalAppData 的随机 GUID，不允许使用硬件标识。
- 下载写入 `.part` 文件并支持 HTTP Range。预签名 URL 返回 403 时重新请求同一发布描述符；只有目标版本和对象哈希保持一致时才能继续下载。
- 只有当前安装目录与已签名基础 Manifest 一致、且存在对应直接增量包时才使用增量；否则使用完整包。不得串联多个增量包。

## 安装、健康检查与回滚

- 主程序下载完成后复制根 Bootstrapper 到 LocalAppData，从临时副本执行 `apply`，再请求应用正常退出。
- Bootstrapper 必须等待明确的父进程 PID 退出，禁止只按进程名等待。
- 增量安装在 `.staging/<version>` 构建。未变化文件优先创建 NTFS 硬链接，失败时复制；变化文件来自增量包；目标 Manifest 不包含的旧文件不得进入新版本。
- 完整校验通过后才能移动到 `versions/<version>` 并原子替换 `current.json`。
- 新桌面进程启动后写入 health marker。60 秒内未成功、进程提前退出或目标校验失败时，Bootstrapper 必须保持或恢复旧版本并重新启动旧程序。
- 成功更新后只保留当前版本和一个回滚版本。

## 发布

### 当前 GitHub Release 阶段

- WPF 主程序以 self-contained、非 single-file、多文件方式发布，并压缩为 `Prometheus-<version>-win-x64.zip`。
- 同一发布目录通过 WiX Toolset 打包为 `Prometheus-<version>-win-x64.msi`；MSI 仅用于首次安装、MSI 版本升级和卸载，不作为应用内自动更新载体。
- MSI 以当前用户范围安装到 `%LocalAppData%\Programs\Prometheus`，不得要求管理员权限；用户数据仍位于 `%LocalAppData%\Prometheus` 并在卸载时保留。
- MSI 安装界面允许选择安装目录、桌面快捷方式和登录 Windows 后自动启动；桌面快捷方式默认启用，自动启动默认关闭，完成页允许立即启动应用。
- MSI 卸载或执行 Major Upgrade 前必须关闭正在运行的 Prometheus；静默卸载不得因应用仍在运行而遗留程序文件或要求重启。
- 推送 `v<major>.<minor>.<patch>` Tag 时创建或更新同名 GitHub Release，并将便携 ZIP 和 MSI 作为 Release 附件。
- Tag 版本必须与 `Directory.Build.props` 中的版本一致。
- 当前包不包含 Bootstrapper、签名 Manifest 或增量更新文件，应用内自动更新 API 保持未配置状态。
- 当前流水线不依赖 R2 凭据或更新签名密钥。

### 后续 R2 自动更新阶段

- Bootstrapper 使用 Native AOT。
- 每个版本发布完整包、目标 Manifest、最近三个稳定版本的直接增量包、Bootstrapper 和首次安装便携包。
- 增量包达到完整包大小的 70% 时不得发布。
- 所有不可变对象先上传，`channels/stable/win-x64.json` 必须最后上传。
- Release 发布缺少更新 API 地址、签名密钥或 R2 凭据时必须失败；任何私钥不得写入仓库或日志。

## 当前 GitHub Release 阶段验收标准

- 推送与项目版本一致的稳定版 Tag 后，GitHub Release 中存在对应的 `Prometheus-<version>-win-x64.zip` 和 `Prometheus-<version>-win-x64.msi` 发布附件。
- ZIP 解压后根目录包含可直接运行的 `Prometheus.Desktop.exe`，不要求用户预装 .NET Runtime。
- MSI 支持图形安装以及 `/qn` 静默安装；安装和卸载后文件、开始菜单快捷方式与安装器注册表状态一致，卸载不得删除 `%LocalAppData%\Prometheus` 用户数据。
- Prometheus 正在运行时，MSI 卸载必须先结束应用进程，再删除程序文件和快捷方式。
- MSI 不得替代 ZIP、签名 Manifest 或后续增量包参与应用内自动更新。
- 流水线不读取 R2 凭据或更新签名密钥；应用未配置更新 API 时，自动检查不得打断应用启动。

## 后续 R2 自动更新阶段验收标准

- 未变化文件不会从网络重复下载；篡改签名、包或本地基础文件时不会切换版本。
- 更新被中断、应用启动失败或 Bootstrapper 自更新失败时，旧版本仍可启动。
- 自动检查网络失败不打断应用启动；手动检查显示可理解的错误。
- 自动或手动检查发现新版本后设置菜单显示本地化提示，更新进度条及其状态文字的可见性与实际更新阶段一致。
- 中英文资源键保持对齐，普通更新和强制更新交互符合本规范。
