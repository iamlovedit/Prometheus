# 玩家操作日志规范

> 适用范围：所有由玩家在 Prometheus 中发起、由自动化功能代为执行，或从英雄联盟客户端（LCU）观察到的业务操作。
> 本文档规定操作事件的记录范围、结构化字段、日志级别、归属、脱敏、降噪、展示和验收标准。
> 技术诊断日志同时受 [后端约定](./backend-conventions.md) 约束；发生冲突时，以更严格的安全与隐私要求为准。

---

## 1. 目标与非目标

操作日志用于回答：

> 谁或什么触发了操作、在什么上下文中、对什么目标、执行了什么动作、结果如何、耗时多久。

本规范的目标是：

1. 记录会改变 Prometheus、LCU、玩家资料或对局状态的业务动作。
2. 明确区分玩家手动操作、自动化操作、观察到的外部变化和系统行为。
3. 同时记录最终成功、失败、取消、拒绝和跳过结果，便于排障与用户自查。
4. 使用稳定、可筛选的结构化字段，不依赖解析自然语言消息。
5. 在满足诊断价值的前提下保护玩家隐私和 LCU 凭据。

本规范不要求记录完整点击流。鼠标悬停、滚动、文本输入过程、普通列表选中等高频 UI 行为默认不记录。

---

## 2. 术语

### 2.1 日志类型（Kind）

| 值 | 说明 |
|----|------|
| `Operation` | 有业务意义的操作及其结果，是本文规范的主体 |
| `Diagnostic` | HTTP、WebSocket、缓存、异常栈等技术排障信息 |

### 2.2 触发来源（Origin）

| 值 | 说明 | 示例 |
|----|------|------|
| `Manual` | 玩家通过 Prometheus 界面主动发起 | 点击“接受对局” |
| `Automation` | 已启用的自动化功能代为执行 | 自动接受、自动重连 |
| `Observed` | Prometheus 只从 LCU 状态变化中观察到，无法证明由谁触发 | 在英雄联盟客户端中修改状态后收到事件 |
| `System` | 应用生命周期或恢复逻辑触发 | 建立连接、断线恢复 |

从 LCU 观察到但没有 Prometheus 操作上下文的变化，必须标记为 `Observed`，禁止描述为玩家在 Prometheus 中执行了该操作。

### 2.3 操作结果（Outcome）

| 值 | 说明 |
|----|------|
| `Started` | 操作已进入执行流程 |
| `Succeeded` | 操作已成功完成 |
| `Failed` | 操作最终失败，且不会继续自动重试 |
| `Cancelled` | 玩家、阶段切换或应用生命周期取消操作 |
| `Rejected` | 前置条件、LCU 状态或服务明确拒绝操作 |
| `Skipped` | 幂等、重复触发或当前状态无需执行 |

`Outcome` 与日志级别相互独立。例如，玩家取消文件导出属于 `Information + Cancelled`，不应记为错误。

---

## 3. 记录原则

1. **记录业务动作，不记录控件点击**：事件名描述最终业务意图，例如 `match.ready_check.accept`，不得使用 `button.clicked`。
2. **最终结果必填**：所有 P0 操作必须产生一条最终结果事件；不得只记录“已点击”或只在异常时记录。
3. **来源必填**：手动接受与自动接受即使调用同一个服务方法，也必须通过 `Origin` 区分。
4. **稳定事件名**：事件名采用小写点分格式 `<domain>.<resource>.<action>`，不包含本地化文本和结果后缀。
5. **结构化优先**：筛选、聚合和关联依赖属性字段；`DisplayMessage` 只用于人类阅读。
6. **单一记录边界**：同一业务结果只记录一次。ViewModel 与服务层不得各自生成一条含义相同的成功或失败事件。
7. **技术层不冒充业务层**：通用 `IHttpService` 只能记录诊断信息，禁止仅凭 URL 推断玩家动作或记录请求体作为操作日志。

---

## 4. 事件范围与优先级

### 4.1 P0：必须记录

P0 操作会改变对局、LCU、玩家资料、持久化自动化设置，或清除用户可见数据。

| 领域 | 事件名 | 操作 | 安全上下文字段 |
|------|--------|------|----------------|
| 匹配 | `match.ready_check.accept` | 手动或自动接受就绪检查 | `GameflowPhase`、`PhaseInstance` |
| 对局 | `match.reconnect` | 手动或自动重连游戏 | `GameflowPhase`、`AttemptCount` |
| 自动化 | `automation.auto_accept.changed` | 开启或关闭自动接受 | `OldValue`、`NewValue`、`Module` |
| 自动化 | `automation.auto_reconnect.changed` | 开启或关闭自动重连 | `OldValue`、`NewValue`、`Module` |
| 自动化 | `automation.aram_bench_swap.changed` | 开启或关闭大乱斗自动换英雄 | `OldValue`、`NewValue`、`Module` |
| 自动化 | `automation.aram_bench_preferences.changed` | 修改大乱斗目标英雄优先级 | `OldCount`、`NewCount`、`Module` |
| BP | `champ_select.pick` | 选择或锁定英雄 | `ActionId`、`ChampionId` |
| BP | `champ_select.ban` | 禁用英雄 | `ActionId`、`ChampionId` |
| BP | `champ_select.bench.swap` | 手动或自动从大乱斗替补席交换英雄 | `ChampionId`、`PhaseInstance`、`AttemptCount` |
| 符文 | `rune.page.create` | 创建符文页 | 安全的页面标识；不得记录完整请求体 |
| 符文 | `rune.page.delete` | 删除符文页 | `RunePageId` |
| 符文 | `rune.page.apply` | 应用符文页 | `RunePageId` |
| 玩家资料 | `profile.background.changed` | 修改生涯背景 | `SkinId` |
| 玩家资料 | `profile.icon.changed` | 修改召唤师头像 | `ProfileIconId` |
| 社交状态 | `social.availability.changed` | 修改在线状态 | 枚举化的新状态 |
| 社交状态 | `social.status_message.changed` | 修改个性签名 | `IsEmpty`、`TextLength` |
| 社交状态 | `social.rank_display.changed` | 修改展示段位 | `QueueType`、`Tier`、`Division` |
| 房间 | `lobby.practice.create` | 创建训练模式房间 | `HasPassword`、安全的房间配置 |
| 诊断 | `diagnostics.logs.clear` | 清空日志面板的内存记录 | `PreviousCount`、清理范围 |

服务层已经具备但尚未接入 UI 的动作，也必须在未来接入时遵守本节，例如英雄选择/禁用、符文页写操作和修改头像。

自动接受和自动重连分别复用 `match.ready_check.accept` 与 `match.reconnect`，并以 `Origin=Automation` 区分来源。禁止再生成含义相同的 `automation.*.execute` 最终结果事件。

### 4.2 P1：建议记录

| 领域 | 建议事件 | 说明 |
|------|----------|------|
| 客户端控制 | 显示、闪烁、最小化、退出客户端 | 成功用 `Information`，失败用 `Error` |
| 连接恢复 | 手动重试连接、手动刷新对局状态 | 自动连接状态变化使用 `System` |
| 设置 | 语言、主题等持久化设置变化 | 仅在值真实变化时记录旧值和新值 |
| 文件操作 | 导出皮肤、头像等资源 | 记录资源类型、资源 ID、扩展名和结果 |
| 查询异常 | 搜索无结果、战绩或详情加载失败 | 不记录原始搜索词和完整玩家标识 |
| 应用生命周期 | 应用启动、正常退出请求 | 使用 `System` 来源 |

### 4.3 P2：默认不记录或仅用于 Debug

- 页面导航、返回上一页。
- 日志搜索、级别过滤、暂停、自动滚动。
- 普通筛选、分页、列表选中、展开详情。
- 鼠标悬停、滚动和每次文本变更。
- 只读数据加载成功、轮询和缓存命中。
- 复制文本到剪贴板。

P2 行为仅在明确的排障需求下使用 `Debug`，不得默认污染玩家可见的操作日志。

---

## 5. 结构化事件模型

### 5.1 必填字段

| 字段 | 类型/示例 | 说明 |
|------|-----------|------|
| `Kind` | `Operation` | 与技术诊断日志区分 |
| `EventName` | `match.ready_check.accept` | 稳定、非本地化的事件代码 |
| `Category` | `Match`、`Automation` | 面板分类与聚合依据 |
| `Origin` | `Manual`、`Automation` | 触发来源 |
| `Outcome` | `Succeeded`、`Failed` | 操作结果 |
| `EventId` | GUID 或等价随机 ID | 单条日志事件的唯一标识，每条事件都不同 |
| `OperationId` | GUID 或等价随机 ID | 关联同一次操作的开始、重试和结果 |
| `AppSessionId` | GUID 或等价随机 ID | 关联一次 Prometheus 运行会话 |
| `Module` | `Home`、`Utility` | 操作入口；无 UI 入口时可为服务域 |
| `DisplayMessage` | “接受就绪检查成功” | 玩家可读消息，不作为筛选逻辑依据 |

时间戳和日志级别由 Serilog 事件本身提供，不重复以自由文本保存。

### 5.2 条件字段

根据事件类型选择以下字段，不得为了“完整”而记录敏感数据：

| 字段 | 用途 |
|------|------|
| `ClientSessionId` | 关联一次 LCU 连接会话，必须随机生成且不得从 token 派生 |
| `TargetType` / `TargetId` | 英雄、皮肤、符文页等安全目标 |
| `OldValue` / `NewValue` | 语言、主题、开关等安全值变化 |
| `GameflowPhase` | 操作发生时的对局阶段 |
| `ConnectionState` | 操作发生时的 LCU 连接状态 |
| `PhaseInstance` | 自动化去重和关联依据 |
| `DurationMs` | 从接受操作到最终结果的耗时 |
| `AttemptCount` | 自动化最终执行次数 |
| `ErrorType` | 异常类型，不包含未经审查的原始文本 |
| `ErrorCode` | 稳定错误码 |
| `HttpStatusCode` | 可安全记录的 HTTP 状态码 |

所有扩展属性必须采用白名单。禁止把任意请求对象、响应对象或动态 JSON 直接附加到日志事件。

---

## 6. 操作生命周期与日志级别

### 6.1 默认级别

| 场景 | 级别 |
|------|------|
| 操作开始、单次自动重试、低价值交互 | `Debug` |
| 操作成功、设置变化、正常取消 | `Information` |
| 前置条件不满足、LCU 拒绝、部分成功、主动跳过 | `Warning` |
| 操作最终失败 | `Error` |
| 应用无法继续运行 | `Fatal`，不得用于普通玩家操作 |

当前根日志配置未显式降低最低级别时，Serilog 默认不采集 `Debug`。因此 P0 操作的最终结果不得只写在 `Debug`。

### 6.2 开始与结束

- P0 操作必须至少记录一条最终结果。
- 耗时较长、可能崩溃中断或需要关联重试的操作，可以记录 `Started`，并与最终结果共享同一个 `OperationId`。
- 简单同步设置变更可以只记录一条最终结果，包含 `OldValue` 和 `NewValue`。
- `Started` 事件不能代替最终结果。

### 6.3 成功判定

- `Succeeded` 必须基于该事件预先定义的成功条件，禁止仅因 `await` 没有抛异常就判定成功。
- LCU 写操作发起前必须检查连接与 `IHttpService.IsInitialized`。未连接或未初始化属于 `Rejected`，不得记为成功。
- LCU 请求必须获得明确的 2xx 确认；因未初始化返回 `default`、空值或未发送请求时，不得记为成功。
- 如果事件语义是“请求已被 LCU 接受”，2xx 可以作为成功条件，显示消息必须准确表述为“请求已发送/已接受”。
- 如果事件语义是“业务状态已经改变”，还必须通过 WebSocket、快照或后续查询确认目标状态；仅有 2xx 不足以表示完成。
- 本地持久化设置必须在新值生效且持久化成功后记录 `Succeeded`；持久化失败不得静默记为成功。
- 文件导出只有在目标文件写入完成后才算成功；日志清空只有在目标缓冲区达到预期状态后才算成功。
- 每个 P0 事件的测试必须明确其成功判定，不得使用“未抛异常”作为唯一断言。

### 6.4 自动重试

- 同一操作的所有尝试共享 `OperationId`。
- 单次重试使用 `Debug`，不得为每次失败都生成面板级 `Error`。
- 最终成功或失败只生成一条摘要事件，并包含 `AttemptCount`。
- 自动接受和自动重连使用现有 `PhaseInstance` 作为幂等依据；同一阶段实例不得重复生成相同的最终成功事件。

---

## 7. 记录归属

### 7.1 手动操作

手动操作入口负责创建 `OperationId`，并提供 `Origin=Manual`、`Module` 和操作时的 UI 上下文。每次写入日志时再生成唯一的 `EventId`。最终结果应在能够确认业务结果的唯一边界记录。

如果 ViewModel 直接 `await` 一个服务调用，允许由 ViewModel 统一记录开始与最终结果；此时下层服务不得重复记录同一业务结果。

### 7.2 自动化操作

自动化操作由实际编排者记录。当前自动接受和自动重连应由 `MatchService` 记录，因为它拥有阶段实例、取消、重试次数和最终结果。

### 7.3 状态观察

WebSocket 或快照刷新观察到的状态变化可以记录为 `Observed`，但不得据此推断玩家通过 Prometheus 发起了操作。轮询结果没有发生变化时不记录。

### 7.4 传输层

`HttpServiceBase` / `HttpService` 只负责技术诊断，不负责玩家操作审计，原因包括：

- 无法可靠判断 `Manual` 或 `Automation` 来源。
- 无法理解业务成功、跳过或取消语义。
- 请求体和响应体可能包含 token、密码或玩家隐私。

---

## 8. 隐私与脱敏

### 8.1 绝对禁止记录

- LCU token、Authorization 头、完整客户端命令行。
- `MatchService` 中包含 token 的原始连接指纹或由其直接派生的标识。
- 房间密码。
- HTTP/WebSocket 完整请求体、响应体或原始事件 JSON。
- 个性签名、聊天或剪贴板正文。
- 完整玩家昵称、Riot ID、PUUID、SummonerId 等可识别玩家身份的数据。
- 未经处理的召唤师搜索关键词。
- 用户选择的完整本地文件路径。
- 可能包含上述内容且未经审查的异常消息或 URL。

### 8.2 安全替代字段

| 原始数据 | 允许记录 |
|----------|----------|
| 搜索关键词 | `QueryLength`、是否找到、结果数量、耗时；确需关联时使用会话级加盐哈希 |
| 个性签名 | `IsEmpty`、`OldLength`、`NewLength` |
| 房间密码 | `HasPassword` |
| 文件导出路径 | `AssetType`、`AssetId`、文件扩展名 |
| 玩家身份 | 当前会话内随机别名或会话级加盐哈希 |
| 服务异常 | `ErrorType`、稳定错误码、HTTP 状态码、已确认安全的摘要 |

脱敏采用“允许字段白名单”，禁止先记录全部内容再依赖正则事后清洗。

---

## 9. 去重与降噪

1. 自动化设置只有在值真实变化时才记录；重复设置相同值不记录。
2. 轮询成功、缓存命中和未变化的快照不记录为操作日志。
3. 同一业务结果不得同时由 ViewModel、业务服务和 HTTP 层各记录一遍。
4. 自动化重试以最终摘要为主，单次尝试仅在 `Debug` 中可见。
5. 重复网络错误应在短时间窗口内聚合，摘要中携带发生次数。
6. `Cancelled`、`Rejected`、`Skipped` 必须使用准确结果，禁止统一记为 `Failed`。
7. 文件选择对话框被取消、重复点击被幂等保护等正常情况不得记录异常栈。

---

## 10. 日志面板与持久化

### 10.1 面板能力

日志面板必须能够区分 `Operation` 与 `Diagnostic`，并支持按以下维度筛选：

- 类别 `Category`
- 来源 `Origin`
- 结果 `Outcome`
- 日志级别
- 当前会话或时间范围
- `EventName` 和安全属性

操作视图默认只显示最终业务结果；`Started` 和单次重试可通过详细/调试视图查看。

建议行展示格式：

```text
15:03:21 [对局] [手动] [成功] 接受就绪检查 · 126 ms
15:03:35 [自动化] [失败] 自动重连 · 3 次 · Timeout
15:04:02 [资料] [成功] 修改生涯背景 · SkinId=12345
```

稳定事件名、枚举值和筛选逻辑不得依赖当前语言。显示文本可以通过 `en-US.xaml` / `zh-CN.xaml` 本地化。

### 10.2 结构化属性保留

内存日志模型和 Sink 必须保留操作日志所需的结构化属性。仅保存 Serilog 的 `RenderMessage()` 无法满足分类、来源和结果筛选要求。

短期兼容阶段可以在消息中显示标签，但不得把解析消息文本作为长期筛选实现。

### 10.3 清空语义

- “清空日志”必须明确说明清理的是内存面板、磁盘文件还是二者。
- 如果只清空内存缓冲区，界面不得暗示磁盘日志也已删除。
- 默认“清空日志”只清空内存面板；完成后面板必须保持为空，不得因记录清空事件而立即重新出现一条日志。
- `diagnostics.logs.clear` 必须写入持久化日志，并带有不进入刚清空内存 Sink 的标记；事件包含清理前数量和清理范围。
- 如果未来允许清理磁盘文件，必须使用独立确认操作，并把清理事件写入新的活动文件或独立审计 Sink。

### 10.4 持久化

- 排障用途允许操作日志和诊断日志继续共用 Serilog 文件管道。
- 任何落盘的操作日志都必须保留必填结构化字段；文本格式应输出结构化属性，或改用 JSON formatter，禁止只保存渲染后的消息。
- 落盘日志必须配置明确的滚动与保留策略；推荐默认保留 14 天。
- 如果产品目标升级为不可抵赖或可追责审计，必须使用独立的结构化 JSON 日志及独立保留策略。当前可清空的内存环形缓冲区和普通文本文件不属于不可篡改审计存储。

---

## 11. 示例事件

以下示例展示语义，不限定具体封装 API：

```text
Kind=Operation
EventName=match.ready_check.accept
Category=Match
Origin=Manual
Outcome=Succeeded
EventId=2a71...
OperationId=8d9c...
AppSessionId=0f35...
Module=Home
GameflowPhase=ReadyCheck
DurationMs=126
DisplayMessage=接受就绪检查成功
```

```text
Kind=Operation
EventName=match.reconnect
Category=Match
Origin=Automation
Outcome=Failed
EventId=4c93...
OperationId=41af...
AppSessionId=0f35...
Module=MatchService
GameflowPhase=Reconnect
PhaseInstance=27
AttemptCount=3
ErrorType=HttpRequestException
DisplayMessage=自动重连失败
```

```text
Kind=Operation
EventName=lobby.practice.create
Category=Lobby
Origin=Manual
Outcome=Succeeded
EventId=ed12...
OperationId=7b02...
AppSessionId=0f35...
Module=Utility
HasPassword=true
DurationMs=203
DisplayMessage=训练模式房间创建成功
```

---

## 12. 验收标准

新增或改造玩家操作日志时，必须满足：

1. [ ] 每个 P0 操作至少产生一条最终结果事件。
2. [ ] 手动、自动化、观察和系统来源可被可靠区分。
3. [ ] 成功、失败、取消、拒绝和跳过不会被混为同一结果。
4. [ ] 事件使用稳定的点分 `EventName`，不依赖本地化显示消息。
5. [ ] 每条事件具有唯一 `EventId`，同一操作的开始、重试和最终结果共享 `OperationId`。
6. [ ] 所有操作事件具有当前运行期一致的 `AppSessionId`。
7. [ ] `Succeeded` 有明确业务判定；未初始化返回 `default` 或仅“未抛异常”不会被判定为成功。
8. [ ] 自动重试只生成一条最终摘要，并携带 `AttemptCount`。
9. [ ] ViewModel、服务层和 HTTP 层不会重复记录同一业务结果。
10. [ ] token、密码、完整命令行、完整请求/响应体和玩家隐私不会进入内存或磁盘日志。
11. [ ] 操作日志的结构化属性经过 Sink 后仍可用于面板筛选。
12. [ ] 操作日志落盘后仍保留必填结构化字段。
13. [ ] 面板可区分操作日志和诊断日志，并按来源、结果、类别筛选。
14. [ ] 清空内存日志后面板保持为空，清空事件仅进入持久化日志并明确清理范围。
15. [ ] 中英文资源保持同步，筛选逻辑不依赖本地化字符串。

---

## 13. 测试要求

至少覆盖以下场景：

- 手动操作与自动化操作产生不同 `Origin`。
- 成功、最终失败、取消、拒绝和跳过结果映射正确。
- 每条事件的 `EventId` 唯一，同一操作的 `OperationId` 保持一致。
- 同一运行期的操作日志具有相同 `AppSessionId`，不同运行期不复用。
- `IHttpService` 未初始化并返回 `default` 时不会生成 `Succeeded`。
- 多次自动重试共享 `OperationId`，只生成一条最终摘要。
- 重复设置相同值不会生成重复操作日志。
- Sink 能保留 `EventName`、`Origin`、`Outcome` 等结构化属性。
- 落盘格式能够恢复操作日志的全部必填字段。
- 日志面板按类型、类别、来源和结果正确筛选。
- token、房间密码、签名正文、搜索关键词、完整 PUUID 和完整文件路径不会出现在内存快照或落盘文本中。
- 清空内存日志后面板保持为空，不会误删磁盘文件，持久化日志中仍记录清理范围。

---

## 14. 当前实现差距

以下为本文档建立时的已知差距，后续实现应向本规范收敛：

| 项 | 当前状态 | 目标 |
|----|----------|------|
| 操作成功日志 | 现有日志几乎只覆盖异常 | P0 操作均记录最终结果 |
| 内存模型 | `LogEntry` 仅有时间、级别、消息、异常 | 保留并暴露结构化操作字段 |
| Sink | `LogHistoryService.ToEntry()` 丢弃 Serilog 属性 | 提取操作字段供面板筛选 |
| 文件格式 | 仅输出时间、级别、渲染消息和异常 | 额外保留必填结构化属性或使用 JSON formatter |
| 面板 | 仅支持文本和最低级别筛选 | 增加类型、类别、来源、结果筛选 |
| Debug 采集 | 根日志配置默认最低级别为 `Information` | 明确环境级别策略，不依赖未生效的 Debug 日志 |
| 清空行为 | 仅清空内存环形缓冲区 | 明确展示清理范围并记录清空事件 |
| 文件保留 | 每日滚动但未显式声明保留策略 | 配置明确的保留期限 |
