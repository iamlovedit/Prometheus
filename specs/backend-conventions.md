# 后端约定

> 适用范围：`src/Services/` 全部代码，以及任何需要与英雄联盟客户端（LCU）或外部数据源通信的功能模块。
> 本文档是新增服务、新增端点、改造现有服务时的**强制约定**。与本文冲突的存量代码属于待还债，重构时应向本文收敛。

---

## 1. 术语与整体架构

### 1.1 术语

| 术语 | 说明 |
|------|------|
| LCU | League Client Update，英雄联盟客户端内置的本地服务，对外暴露 HTTPS REST 与 WAMP WebSocket |
| Port / Token | LCU 启动时随机分配的监听端口与 remoting 令牌，从 `LeagueClientUx` 进程命令行提取 |
| 服务层 | `src/Services/Prometheus.Services`（实现）与 `src/Services/Prometheus.Services.Interfaces`（契约） |
| 快照（Snapshot） | `MatchService` 发布的不可变状态对象（如 `LiveMatchSnapshot`），每次变更整体替换 |

### 1.2 数据来源分层

Prometheus 没有自建服务端。全部数据来自三类来源，**模块（View/ViewModel）只允许通过服务层访问，禁止直接发起 HTTP/WebSocket 请求**：

```
┌──────────────────────────────────────────────────────┐
│  Modules (ViewModels)                                │
│   └─ 只依赖 Prometheus.Services.Interfaces 中的接口    │
├──────────────────────────────────────────────────────┤
│  Services                                            │
│   ├─ IHttpService        LCU REST 统一入口（单例）     │
│   ├─ ILeagueClient       LCU WebSocket 生命周期（单例）│
│   ├─ IClientService      进程探测 / port+token 提取   │
│   ├─ IMatchService       对局状态编排 + 快照发布       │
│   ├─ ISummonerService / IGameService / ... 业务端点   │
│   └─ IGameResourceManager 游戏资源本地缓存             │
├──────────────────────────────────────────────────────┤
│  数据来源                                             │
│   1. LCU REST      https://127.0.0.1:{port}          │
│   2. LCU WebSocket wss://127.0.0.1:{port} (WAMP)     │
│   3. 外部数据源     op.gg 类公开接口（见 §10.3）        │
└──────────────────────────────────────────────────────┘
```

---

## 2. 连接引导（Bootstrap）

### 2.1 Port / Token 提取

实现在 `ClientService`（`src/Services/Prometheus.Services/Client/ClientService.cs`）：

1. `Process.GetProcesses()` 查找进程名 **`LeagueClientUx`**；找不到视为"客户端未运行"，返回 `0` / `null`，**不得抛异常**。
2. 通过 WMI 查询 `SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}` 取得完整命令行。
3. 用 Win32 `CommandLineToArgvW` 拆分参数（**禁止用空格 `Split`**，路径含空格时会出错），再按 `=` 拆成字典。
4. 必需键：
   - `--app-port` → LCU HTTPS/WSS 端口
   - `--remoting-auth-token` → 认证令牌
5. 两者任一缺失/为空 → 连接失败，进入重试（见 §4.4）。

```csharp
// 约定用法（LeagueClient.TryConnect）
var processId = _clientService.GetClientProcessId();
var argsMap   = _clientService.GetClientCommandLines();
if (processId <= 0 || argsMap is null ||
    !argsMap.TryGetValue("--app-port", out var port) ||
    !argsMap.TryGetValue("--remoting-auth-token", out var token) ||
    string.IsNullOrWhiteSpace(port) || string.IsNullOrWhiteSpace(token))
{
    return false; // 静默失败，由重试循环兜底
}
```

### 2.2 初始化顺序

`ILeagueClient`（WebSocket）先于 `IHttpService`（REST）建立连接；HTTP 服务在 WebSocket 首次连接成功后初始化（见 `MatchService`：`HttpService.Initialize(port, token)`）。**任何业务代码在 `IHttpService.IsInitialized == false` 时不得假设请求会成功**——未初始化时所有 `IHttpService` 方法返回 `default` 而不是抛异常（见 §6.1）。

### 2.3 连接标识

一次连接的指纹为 `{ProcessId}:{Port}:{Token}`（`MatchService` 中的 `connectionId`）。客户端重启后指纹变化，必须重新 `Initialize` HTTP 服务并丢弃旧快照。

---

## 3. 认证与 TLS

### 3.1 HTTP Basic 认证

- 用户名固定为 **`riot`**，密码为 `--remoting-auth-token`。
- 头部：`Authorization: Basic base64("riot:" + token)`，在 `HttpServiceBase.Initialize` 中构造一次，全生命周期复用。
- **凭据只许附加到回环地址**：`ShouldAuthenticate` 仅当目标 URI 为 loopback 且 scheme/port 与已认证基地址完全一致时才附加 Authorization。防止 token 随重定向/绝对 URL 泄漏到外部主机。新增代码不得绕过该判断。

### 3.2 TLS 证书

- LCU 使用自签名证书。**只允许对 loopback 请求放宽证书校验**：

```csharp
ServerCertificateCustomValidationCallback = (request, cert, chain, errors) =>
    request?.RequestUri?.IsLoopback == true || errors == SslPolicyErrors.None
```

- 公共 HTTPS（外部数据源）必须走正常证书校验，禁止全局 `return true`。
- WebSocket 侧使用 .NET `ClientWebSocket`；证书回调仅允许 loopback 目标放宽校验，非 loopback 目标仍必须通过正常证书校验，禁止全局放行。

### 3.3 保密要求

- Token、完整命令行、含 token 的 URL **禁止写入日志、禁止提交到仓库**（与 `AGENTS.md` 一致）。
- 异常信息向外抛出的消息不得包含 token。

---

## 4. HTTP 约定（LCU REST）

统一入口：`HttpServiceBase` + `HttpService`（`src/Services/Prometheus.Services/`）。

### 4.1 HttpClient 生命周期

- `Initialize(int port, string token)` 是**唯一**重建 `HttpClient` 的入口；参数校验：`port` 必须在 1–65535，`token` 非空，否则抛 `ArgumentOutOfRangeException` / `ArgumentException`。
- 重建是原子替换：锁内交换字段，旧 client 在锁外 `Dispose`。**禁止**在锁内 dispose、禁止并发重建两条 client。
- 固定配置：

| 配置 | 值 | 原因 |
|------|----|------|
| BaseAddress | `https://127.0.0.1:{port}/` | LCU 本地服务 |
| DefaultRequestVersion | HTTP/2.0 | LCU 支持 |
| Timeout | 10 秒 | 本地调用不应更长 |
| Accept | `application/json` | LCU 默认返回 JSON |
| User-Agent | `LeagueOfLegendsClient/12.7.433.4138 (CEF 91)` | 模拟客户端 UA，避免被 LCU 拒绝 |
| Connection | `keep-alive` | 复用连接 |

### 4.2 URL 与查询串

- 端点一律写成**相对路径常量**（如 `lol-summoner/v1/current-summoner`），集中声明为服务类内的 `private const string`。禁止在调用处散落字符串字面量。
- 查询参数以 `IEnumerable<string>` 传入，每个元素是**已编码**的 `"key=value"` 片段；`BuildRelativeUrl` 负责用 `?`/`&` 拼接（自动识别 URL 中已有的 `?`），`BuildQueryStringFromParameters` 自动丢弃空白片段。
- 参数值必须调用方自行 `HttpUtility.UrlEncode`（参考 `SummonerService.SearchSummonerByName`）。中文召唤师名未编码是历史 bug 的高发点。

### 4.3 请求方法选择

| 场景 | 方法 |
|------|------|
| 读取 JSON 为字符串 | `GetAsync(url, query)` |
| 读取 JSON 并反序列化 | `GetAsync<T>(url, query)`（`T : class, new()`） |
| 下载二进制（图片等） | `GetByteArrayResponseAsync(HttpMethod.Get, url)` |
| 提交且无响应体消费 | `PostAsync(url, body)` |
| 提交并消费响应 | `PostAsync<T>(url, body, query)` |
| PUT / PATCH / DELETE | `SendAsync(HttpMethod.Xxx, url, body)` |

- Body 序列化：`JsonConvert.SerializeObject(body)` + `Encoding.UTF8` + `application/json`（Newtonsoft.Json，全仓库统一，禁止混用 `System.Text.Json` 于 LCU 通信）。
- 所有发送路径统一 `EnsureSuccessStatusCode()`——**非 2xx 即抛 `HttpRequestException`**，由调用方决定是否捕获（§6）。

### 4.4 取消与超时

- 新接口方法必须暴露 `CancellationToken cancellationToken = default` 形参并透传到底（参考 `GameService.GetGameflowPhaseAsync`）。
- 快照刷新类调用必须支持取消；长轮询/自动化任务（自动接受、自动重连）必须使用可取消的 `Task.Delay`，禁止 `Thread.Sleep`。

### 4.5 重试策略

- 通用 HTTP 层**不做**隐式重试；重试是业务决策，集中在 `MatchService`：
  - 自动接受对局（Accept）：`0ms → 500ms → 1500ms`
  - 自动重连对局（Reconnect）：`0s → 2s → 5s`
- 新增自动化动作应沿用"少量、递增延迟、可取消"的模式，禁止无限重试。

---

## 5. WebSocket 约定（LCU 事件）

统一实现：`LeagueClient`（`src/Services/Prometheus.Services/Client/LeagueClient.cs`）。
`ClientListener` 为旧实现（基于 Websocket.Client + Rx），**新代码一律使用 `ILeagueClient`**，旧实现逐步下线。

### 5.1 协议

- 地址：`wss://127.0.0.1:{port}/`，子协议 **`wamp`**，凭据 `riot:{token}`。
- 连接成功后发送订阅帧：`[5, "OnJsonApiEvent"]`（WAMP SUBSCRIBE）。
- 事件帧固定为三元数组：`[8, "OnJsonApiEvent", { data, eventType, uri }]`；非此形状（长度≠3、首元素≠8、事件名不符）直接丢弃。
- 反序列化目标：`OnWebsocketEventArgs { dynamic Data; string EventType; string Uri; }`（`Prometheus.Core/Models`）。

### 5.2 生命周期

- `StartAsync` 启动一个**可重启的连接循环**：意外断开后每 2 秒（`RetryDelay`）重试，直到 `StopAsync` 取消。
- `StartAsync` 的返回值只代表**首次尝试**结果；后续断线重连通过 `OnConnected` / `OnDisconnected` 事件通知。
- `StopAsync` 是确定性停止：取消 CTS → 关闭 socket（`CloseStatusCode.Normal`）→ 等待重试循环退出 → 发 `OnDisconnected`（仅当之前处于已连接态）。
- 生命周期由 `_lifecycleGate`（SemaphoreSlim）串行化；Start/Stop 可重入且幂等。

### 5.3 订阅

- `Subscribe(uri, Action<OnWebsocketEventArgs>)`：按 URI 精确匹配分发；同一回调重复订阅会被去重。
- `Unsubscribe(uri, action)` 必须在 ViewModel 销毁时调用，防止泄漏。
- 单个订阅者抛异常**不得**影响其他订阅者（实现内已逐回调 try/catch）；订阅者回调在 socket 接收线程上执行，**回调内禁止直接操作 UI**，需自行 `Dispatcher.Invoke` 或转快照。
- 全局事件流（`OnWebsocketEvent`）与按 URI 订阅（`_eventsMap`）并存；新功能优先用按 URI 订阅。

### 5.4 线程模型

- 连接状态用 `lock (_stateSync)` 保护；订阅表用独立的 `_subscriptionsSync`。**两个锁不得嵌套顺序颠倒**，新代码沿用现有顺序（state → subscriptions，不反向）。
- `TaskCompletionSource` 信号一律 `RunContinuationsAsynchronously`，避免续体占用 socket 线程。

---

## 6. 错误处理与降级

### 6.1 未初始化

`IHttpService` 未初始化时所有方法返回 `default`（不抛）。调用方必须容忍 `null`/空字符串结果。需要区分"未连接"与"查询无结果"时，先查 `IMatchService.Current.ConnectionState` 或 `IHttpService.IsInitialized`。

### 6.2 异常分级

| 层级 | 约定 |
|------|------|
| 传输层（HttpServiceBase） | 非 2xx 抛 `HttpRequestException`；未初始化抛 `InvalidOperationException`（仅 `CreateRequestMessage`，公开方法已前置拦截） |
| 服务层（*Service） | 可预期的失败（404、解析失败）→ 返回 `default` 并 `Log.Error`；不可预期的异常向上抛 |
| 编排层（MatchService） | 捕获一切，转化为快照的 `Error` / `DataQuality.Stale`，不得让异常击穿到 UI |
| ViewModel | 只读快照与服务返回值，禁止 try/catch 后静默吞掉（日志必填） |

### 6.3 日志

- 使用 **Serilog**（`Log.Error(ex, ...)`），禁止 `Console.WriteLine` / `Debug.WriteLine` 进入正式代码。
- 日志中不得出现 token（§3.3）、不得打印完整响应体中的用户隐私字段。

---

## 7. 服务层组织约定

### 7.1 契约与实现分离

- 接口在 `Prometheus.Services.Interfaces`，实现在 `Prometheus.Services`，命名 `IXxxService` ↔ `XxxService`。
- 接口方法注释写明：对应的 LCU 端点、LCU 不可用时的返回行为、是否支持取消。

### 7.2 依赖注入

- 全部服务在 Shell 的 `App.RegisterTypes` 中注册为**单例**（`RegisterSingleton`）。服务不得持有对具体模块/View 的引用。
- 构造注入只允许依赖其他服务接口或 `IContainerExtension`（`GameResourceManager` 现状），禁止 `new` 其他服务。

### 7.3 端点常量

- 每个服务类顶部集中声明 `private const string` 端点；带占位的用 `string.Format` 风格 `{0}`。
- 端点路径**不带前导 `/`**（`MatchService` 中带 `/` 的常量为历史遗留，改动时顺手去掉）。

### 7.4 现有服务清单

| 接口 | 实现 | 职责 |
|------|------|------|
| `IHttpService` | `HttpService` | LCU REST 统一入口 |
| `ILeagueClient` | `LeagueClient` | LCU WebSocket 生命周期与订阅分发 |
| `IClientListener` | `ClientListener` | （旧）WebSocket 监听，待下线 |
| `IClientService` | `ClientService` | 进程探测、命令行提取、客户端 UX 控制（ux-show/ux-flash/ux-minimize/unload）、安装目录、队列数据 |
| `ISummonerService` | `SummonerService` | 召唤师查询、段位、战绩列表、生涯背景 |
| `IGameService` | `GameService` | 对局会话、BP 操作、符文页、聊天状态、外部数据（英雄梯度/推荐符文） |
| `IMatchService` | `MatchService` | 对局全阶段状态编排，发布 `LiveMatchSnapshot`，自动接受/自动重连 |
| `IGameResourceManager` | `GameResourceManager` | 游戏静态资源（装备/符文/英雄/头像）拉取与本地文件缓存 |
| `IResourceService` | `ResourceService` | 主题/语言/段位图标等 WPF 资源切换 |
| `IGameAutomationSettings` | `GameAutomationSettings` | 自动化开关的 JSON 持久化（损坏文件按默认关闭处理，线程安全） |

---

## 8. 快照与状态发布

`MatchService` 是"实时对局"领域唯一的写入者，约定：

1. **不可变替换**：每次发布一个全新 `LiveMatchSnapshot` 实例（`CopySnapshot` 后改字段），读者无需加锁。
2. **单一事实源**：UI 只读 `Current` + 订阅 `SnapshotChanged`，不得自行缓存派生状态。
3. **连接态机**：`Disconnected → Connected`，数据质量 `Full / Partial / Stale` 与 `Error` 字段随快照携带，UI 据此降级展示。
4. **阶段版本**：`_phaseVersion/_phaseInstance` 单调递增，自动化动作（接受/重连）按实例去重，防止同一阶段重复执行。
5. 新的"实时"状态域必须沿用同一模式（快照不可变 + 事件 + 取消令牌），禁止引入第二种推送机制。

---

## 9. 资源缓存约定

`GameResourceManager` 负责把 LCU 静态资源落到本地：

- 缓存目录常量集中在 `Prometheus.Core/ParameterNames`（`Equipments`、`Perks`、`Skins`、`Spells`、`ChampoinIcon`、`ProfileIcon` 等）。
- **写前查存在**：`File.Exists` 命中即返回本地路径；未命中才走 `GetByteArrayResponseAsync` 下载并 `File.WriteAllBytesAsync`。
- 失败降级：装备图标缺失回退 `gp_ui_placeholder.png`，召唤师技能回退 `summoner_empty.png`；下载失败记日志并返回 `default`，不得抛到 UI。
- 内存缓存（`_equipments/_spells/_perks`）懒加载，单例生命周期内有效；**不保证线程安全的字段禁止跨线程新增写入路径**。
- 缓存文件属于可再生数据，删除后应能自愈；清理逻辑不得触碰 `LocalResourceDirectory` 之外的目录。

---

## 10. 端点清单

### 10.1 LCU REST（基地址 `https://127.0.0.1:{port}`）

| 端点 | 方法 | 用途 | 调用方 |
|------|------|------|--------|
| `data-store/v1/install-dir` | GET | 客户端安装目录 | `ClientService` |
| `riotclient/unload` · `ux-show` · `ux-flash` · `ux-minimize` | POST | 退出 / 置前 / 闪烁 / 最小化客户端 | `ClientService` |
| `lol-game-queues/v1/queues` | GET | 队列模式数据 | `ClientService` |
| `lol-summoner/v1/current-summoner` | GET | 当前登录召唤师 | `SummonerService` |
| `lol-summoner/v1/summoners?name=` | GET | 按昵称搜索（需 UrlEncode） | `SummonerService` |
| `lol-summoner/v2/summoners/puuid/{puuid}` | GET | 按 PUUID 查询 | `SummonerService` |
| `lol-ranked/v1/ranked-stats/{puuid}` | GET | 排位段位 | `SummonerService` |
| `lol-match-history/v1/products/lol/{puuid}/matches?begIndex=&endIndex=` | GET | 历史战绩分页 | `SummonerService` |
| `lol-match-history/v1/games/{gameId}` | GET | 单局详情 | `GameService` |
| `lol-collections/v1/inventories/{summonerId}/backdrop` | GET | 生涯背景 | `SummonerService` |
| `lol-summoner/v1/current-summoner/summoner-profile` | GET / POST | 读 / 写生涯背景（`{key:"backgroundSkinId", value}`） | `GameResourceManager` |
| `lol-summoner/v1/current-summoner/icon` | PUT | 设置召唤师头像 | `GameService` |
| `lol-chat/v1/me` | PUT | 在线状态 / 签名 / 展示段位 | `GameService` |
| `lol-lobby/v2/lobby` | GET / POST | 房间快照 / 创建训练模式房间 | `MatchService` / `GameService` |
| `lol-matchmaking/v1/search` | GET | 匹配中状态 | `MatchService` |
| `lol-matchmaking/v1/ready-check` · `/accept` | GET / POST | 就绪检查 / 接受对局 | `MatchService` / `GameService` |
| `lol-champ-select/v1/session` | GET | BP 会话快照 | `MatchService` / `GameService` |
| `lol-champ-select/v1/session/actions/{actionId}` | PATCH | 选择 / 禁用英雄（`{type, championId}`） | `GameService` |
| `lol-champ-select/v1/current-champion` · `pickable-champions` · `pin-drop-notification` | GET | 当前英雄 / 可选列表 / 分边 | `GameService` |
| `lol-gameflow/v1/gameflow-phase` | GET | 对局阶段（Lobby/ChampSelect/InProgress…） | `MatchService` |
| `lol-gameflow/v1/session` | GET | 对局会话全量 | `MatchService` / `GameService` |
| `lol-gameflow/v1/reconnect` | POST | 重连对局 | `GameService` |
| `lol-end-of-game/v1/eog-stats-block` | GET | 结算数据 | `MatchService` |
| `lol-perks/v1/pages` · `/currentpage` | GET / POST / DELETE | 符文页 CRUD | `GameService` |
| `lol-game-data/assets/v1/champion-summary.json` · `champions/{id}.json` | GET | 英雄数据 | `GameResourceManager` / `GameService` |
| `lol-game-data/assets/v1/items.json` · `perks.json` · `summoner-spells.json` · `profile-icons.json` | GET | 装备/符文/技能/头像数据 | `GameResourceManager` / `GameService` |
| `lol-game-data/assets/v1/profile-icons/{id}.jpg` · `champion-icons/{id}.png` | GET（二进制） | 图标下载 | `GameResourceManager` |

### 10.2 LCU WebSocket 订阅 URI（按 URI 精确订阅）

| URI | 用途 |
|-----|------|
| `/lol-gameflow/v1/gameflow-phase` | 阶段切换驱动快照刷新 |
| `/lol-gameflow/v1/session` | 对局会话变化 |
| `/lol-lobby/v2/lobby` | 房间变化 |
| `/lol-matchmaking/v1/search` | 匹配状态变化 |
| `/lol-matchmaking/v1/ready-check` | 弹窗/自动接受触发 |
| `/lol-champ-select/v1/session` | BP 过程驱动 |

### 10.3 外部数据源（公共 HTTPS，正常证书校验，无 LCU 凭据）

| 端点 | 用途 | 调用方 |
|------|------|--------|
| `https://x1-6833.native.qq.com/x1/6833/1061021&3af49f?lane=&tier=&dtstatdate=&ijob=all&gamequeueconfigid=420&championid=666` | 英雄梯度/胜率榜（国服数据） | `GameService.GetChampionRankAsync` |
| `https://lol.qq.com/act/lbp/common/guides/champDetail/champDetail_{id}.js` | 英雄克制关系 | `GameService` |
| `https://www.wegame.com.cn/lol/resources/js/champion/recommend/{id}.js` | 推荐符文 | `GameService.GetRuneItemsFromOnlineAsync` |

约定：外部端点必须走与 LCU 相同的 `IHttpService`（`ShouldAuthenticate` 保证不会附带 LCU 凭据）；返回多为 JS/JSONP 风格文本，按字符串获取、自行解析，禁止用 `GetAsync<T>` 直接反序列化。

---

## 11. 新增功能检查清单

新增一个依赖 LCU 的功能时，按顺序自检：

1. [ ] 端点是否已存在于 §10.1？存在则复用对应服务，不新增调用点。
2. [ ] 新端点常量在服务类顶部集中声明，接口与实现同步添加。
3. [ ] 方法签名含 `CancellationToken` 默认值并透传。
4. [ ] 查询参数已 `UrlEncode`；返回行为（未初始化/404）已写明注释。
5. [ ] 需要实时性 → 用 `ILeagueClient.Subscribe(uri, ...)`，并在销毁时 `Unsubscribe`。
6. [ ] 状态需要跨模块共享 → 扩展快照（§8），不发自定义全局事件。
7. [ ] 日志不含 token；异常路径不吞错。
8. [ ] 服务保持单例注册；ViewModel 只依赖接口。
9. [ ] 为服务补充 xUnit 测试（Moq 打桩 `IHttpService` / `ILeagueClient`）。

---

## 12. 已知技术债（改造时按本文收敛）

| 项 | 现状 | 目标 |
|----|------|------|
| `ClientListener` | 旧 WebSocket 实现（Rx + Websocket.Client），重连/生命周期不如 `LeagueClient` 严谨 | 下线，统一 `ILeagueClient` |
| `MatchService` 端点前导 `/` | 常量为 `/lol-...`，与其他服务不一致 | 去掉前导 `/` |
| `SummonerService.GetMatchesAsync` | `string.Format` 中冗余 `$` 插值、直接 `JObject["games"]["games"]` 无空判 | 改强类型 + 空判 |
| `GameService.BanChampionAsync` | body `type` 误写为 `"pick"`（应 `"ban"`） | 修正并补测试 |
| 外部 URL `_recommendPerks` | 常量尾部带空格 | 去除 |
| `ClientService.GetCommandLines` | `[Obsolete]` 旧实现残留 | 删除 |
