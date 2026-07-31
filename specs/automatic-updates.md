# 自动更新规范

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

- WPF 主程序以 self-contained、非 single-file、多文件方式发布；Bootstrapper 使用 Native AOT。
- 每个版本发布完整包、目标 Manifest、最近三个稳定版本的直接增量包、Bootstrapper 和首次安装便携包。
- 增量包达到完整包大小的 70% 时不得发布。
- 所有不可变对象先上传，`channels/stable/win-x64.json` 必须最后上传。
- Release 发布缺少更新 API 地址、签名密钥或 R2 凭据时必须失败；任何私钥不得写入仓库或日志。

## 验收标准

- 未变化文件不会从网络重复下载；篡改签名、包或本地基础文件时不会切换版本。
- 更新被中断、应用启动失败或 Bootstrapper 自更新失败时，旧版本仍可启动。
- 自动检查网络失败不打断应用启动；手动检查显示可理解的错误。
- 自动或手动检查发现新版本后设置菜单显示本地化提示，更新进度条及其状态文字的可见性与实际更新阶段一致。
- 中英文资源键保持对齐，普通更新和强制更新交互符合本规范。
