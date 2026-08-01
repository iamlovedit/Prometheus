# Prometheus

Prometheus 是一款基于 **League Client Update (LCU) API** 的英雄联盟第三方桌面助手，通过本机客户端 WebSocket 与客户端直连，提供召唤师生涯、战绩查询、游戏资产管理等增强功能。

- 中英双语动态切换
- 支持深、浅色主题动态切换
- 基于 Prism 的模块化 WPF 架构，面向 Windows / .NET 10

## 主页

主页汇总客户端连接状态、当前召唤师信息、最近对局战绩，并提供自动接受 / 自动重连等助手自动化开关与快捷操作入口。

![Home](./doc/images/Home.png)

## 召唤师生涯

展示召唤师等级、段位、生涯数据等信息，并支持自定义生涯背景。

![Career](./doc/images/career.png)

### 自定义生涯背景

![Skin](./doc/images/CareerSkin.png)

## 战绩查询

查询近期战绩、历史对局与对局详情（分页加载），并解析对局模式。

![Search](./doc/images/Search.png)

## 游戏资源

浏览并管理游戏内资产，支持更换皮肤与召唤师头像。

### 游戏皮肤

![Skins](./doc/images/Inventory_skins.png)

### 召唤师头像

![Icons](./doc/images/Inventory_icons.png)

## 软件设置

提供偏好设置、系统设置与运行日志查看（含日志开关与保留策略）。

![Setting](./doc/images//Setting.png)

## 实用工具

集成常用游戏操作：创建 5V5 练习模式房间、设置在线状态与个性签名、自定义段位展示，以及大乱斗自动交换英雄（按优先级列表自动交换）。

![Tools](./doc/images/Tools.png)

## 构建与运行

需要 Windows 与 **.NET 10 SDK**，在仓库根目录执行：

```powershell
dotnet restore src/Prometheus.slnx
dotnet build src/Prometheus.slnx -c Release
dotnet test src/Prometheus.slnx -c Release
dotnet run --project src/Prometheus/Prometheus.csproj
```

> 重新构建前请先关闭正在运行的 `Prometheus.exe`，否则运行中的程序会锁定已复制的模块 DLL。

## 技术栈

- **.NET 10 / WPF** — 桌面客户端
- **Prism** — 模块化 MVVM（各功能模块位于 `src/Prometheus.Modules.<Feature>/`）
- **LCU WebSocket** — 与英雄联盟客户端通信（`LeagueClient` 生命周期管理）
- **Serilog** — 结构化日志
- **xUnit + Moq + Coverlet** — 单元测试

## 目录结构

| 路径 | 说明 |
|------|------|
| `src/Prometheus/` | 应用外壳与入口 |
| `src/Prometheus.Modules.<Feature>/` | 功能模块（Home / Summoner / Match / Search / Inventory / Setting / Utility） |
| `src/Prometheus.Core/` | 共享模型、事件、MVVM 基类、本地化与资源 |
| `src/Prometheus.Shared/` | 可复用控件与展示模型 |
| `src/Services/` | 服务契约与实现（客户端通信、对局、召唤师、资源等） |
| `src/Tests/` | 单元测试 |
| `specs/` | 实现行为与验收标准的权威说明 |

## 许可

详见 [LICENSE](./LICENSE)。
