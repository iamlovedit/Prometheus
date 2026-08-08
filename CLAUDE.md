# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Prometheus is a Windows-only League of Legends desktop companion application built with .NET 10 and WPF. It communicates with the local League Client Update (LCU) API via HTTPS REST and WebSocket to provide summoner profiles, match history, live match data, champion select automation, game resources, and common automation features. The application uses Prism for modular architecture, dependency injection, and MVVM patterns.

**Core principle**: All LCU communication happens locally through loopback (127.0.0.1). Authentication tokens never leave the local machine. The companion window uses an independent WPF window with public LCU APIs—no code injection, no screen reading, no modification of the League client.

## Build and Test Commands

Run from the repository root on Windows with .NET 10 SDK installed:

```powershell
# Restore NuGet dependencies
dotnet restore src/Prometheus.slnx

# Build solution (Release configuration recommended)
dotnet build src/Prometheus.slnx -c Release

# Run all tests
dotnet test src/Prometheus.slnx -c Release

# Collect test coverage
dotnet test src/Prometheus.slnx --collect:"XPlat Code Coverage"

# Run the application
dotnet run --project src/Prometheus/Prometheus.csproj
```

**CRITICAL**: Stop any running `Prometheus.exe` before rebuilding. The running application locks module DLLs and will cause build failures.

## Mandatory Workflow: Specs First

**Before analyzing requirements, planning, modifying code, or writing tests**:

1. Read `specs/README.md` first to understand the spec index
2. Read all specs relevant to your task based on the index
3. If the task spans multiple modules or features, read all corresponding specs
4. The `specs/` directory is the **source of truth** for implementation behavior and acceptance criteria
5. If a user request conflicts with a spec, point out the conflict and ask for confirmation—never silently ignore specs
6. After implementation, verify code and tests against the acceptance criteria in the relevant specs

Key specs:
- `specs/backend-conventions.md`: Mandatory for all `src/Services/` code and LCU communication
- `specs/player-operation-logging.md`: Mandatory for logging player operations and automation events
- `specs/lcu-champion-select-companion.md`: Champion select companion window lifecycle and behavior
- `specs/client-dependent-navigation.md`: Navigation logic based on LCU connection state
- `specs/quick-match-lobbies.md`: Quick match room creation and switching
- `specs/summoner-match-search.md`: Summoner search and match preview
- `specs/automatic-updates.md`: Update system architecture and workflow

## Architecture

### Project Structure

```
src/
├── Prometheus/                              # Shell, App.xaml.cs entry point, main window, system tray
├── Prometheus.Core/                         # Shared models, events, MVVM base classes, localization, constants
│   ├── Events/                              # Prism events for cross-module communication
│   ├── Models/                              # Domain models (matches, champions, summoners, etc.)
│   ├── Mvvm/                                # Base ViewModel and command classes
│   ├── Resources/                           # Localization dictionaries (en-US.xaml, zh-CN.xaml)
│   └── Logging/                             # Structured logging utilities
├── Prometheus.Shared/                       # Reusable WPF controls and presentation models
├── Prometheus.Modules.Home/                 # Home page: connection status, recent matches, quick actions
├── Prometheus.Modules.Summoner/             # Summoner career: level, rank, performance, match history
├── Prometheus.Modules.Search/               # Player search by Riot ID, match history viewer
├── Prometheus.Modules.Match/                # Live match information, real-time roster and stats
├── Prometheus.Modules.Inventory/            # Champion skins, summoner icons browsing and export
├── Prometheus.Modules.Utility/              # Automation tools: auto-accept, auto-reconnect, auto-pick/ban
├── Prometheus.Modules.Setting/              # Settings: language, theme, automation toggles, diagnostics
├── Services/
│   ├── Prometheus.Services.Interfaces/      # Service contracts
│   └── Prometheus.Services/                 # Service implementations (LCU communication, resource management)
├── Prometheus.Update/                       # Update protocol and shared update logic
├── Prometheus.Updater/                      # Desktop update UI
└── Tests/                                   # xUnit test projects
```

### Module Registration

All feature modules follow the Prism pattern:
- Each module has a `<Feature>Module.cs` that implements `IModule`
- Modules are registered in `App.ConfigureModuleCatalog` in `src/Prometheus/App.xaml.cs`
- Views are paired with ViewModels following the naming convention: `ExampleView.xaml` ↔ `ExampleViewModel.cs`
- Navigation uses Prism regions (see `Prometheus.Core/RegionNames.cs`)

### Dependency Injection

All services are registered as **singletons** in `App.RegisterTypes` (`src/Prometheus/App.xaml.cs`). Services must only depend on other service interfaces or `IContainerExtension`, never instantiate other services with `new`.

Key services:
- `IHttpService`: LCU REST unified entry point (singleton)
- `ILeagueClient`: LCU WebSocket lifecycle and subscription dispatcher (singleton, replaces legacy `IClientListener`)
- `IClientService`: Process detection, command-line extraction, client window control
- `IMatchService`: Match state orchestration, publishes immutable `LiveMatchSnapshot`
- `ISummonerService`: Summoner queries, rank, match history, career backgrounds
- `IGameService`: Match session, champion select operations, rune pages, chat status
- `IGameResourceManager`: Static game resources (items, runes, champions, icons) with local file caching
- `IGameAutomationSettings`: JSON persistence for automation toggles
- `IQuickMatchSettings`: JSON persistence for last selected match queue

### LCU Connection Bootstrap

Connection flow (implemented in `ClientService` and `LeagueClient`):

1. Find `LeagueClientUx` process via `Process.GetProcesses()`
2. Extract command-line via WMI: `SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}`
3. Parse with Win32 `CommandLineToArgvW` (never `Split` on space—paths may contain spaces)
4. Extract `--app-port` and `--remoting-auth-token`
5. WebSocket (`ILeagueClient`) connects first, then initializes `IHttpService` with port and token
6. Authentication: HTTP Basic with username `riot` and password = token
7. Connection fingerprint: `{ProcessId}:{Port}:{Token}`—must reinitialize on client restart

### LCU Communication Conventions

**Must read `specs/backend-conventions.md` before modifying any service code.**

Key points:
- **Loopback only**: LCU credentials are only attached to loopback addresses. TLS certificate validation is relaxed only for loopback.
- **No token logging**: Tokens, full command lines, and LCU URLs with tokens must never appear in logs or be committed to the repository.
- **Endpoint constants**: Declare all LCU endpoint paths as `private const string` at the top of each service class. Use relative paths without leading `/`.
- **Cancellation**: New service methods must expose `CancellationToken cancellationToken = default` and propagate it through the call chain.
- **Error handling**: Uninitialized `IHttpService` returns `default`, does not throw. Services return `default` on expected failures (404, parse errors) and log errors; unexpected exceptions propagate up. `MatchService` catches everything and converts to snapshot errors.
- **WebSocket subscriptions**: Use `ILeagueClient.Subscribe(uri, callback)` for event-driven updates. Always `Unsubscribe` in ViewModel disposal to prevent leaks.

### State Management: Immutable Snapshots

`MatchService` is the single writer for live match state:
- Publishes immutable `LiveMatchSnapshot` instances via `Current` property and `SnapshotChanged` event
- ViewModels read snapshots—no caching of derived state
- Connection state: `Disconnected` → `Connected`
- Data quality: `Full` / `Partial` / `Stale` with `Error` field
- Phase versioning: `_phaseVersion` / `_phaseInstance` increments monotonically; automation actions deduplicate per instance

**Progressive roster loading** (see `specs/backend-conventions.md` §8.1):
- `ChampSelect`: Load only visible allies; enemies remain hidden
- `InProgress`: Load enemy data only after gameflow exposes it
- Reuse loaded data across phase transitions for the same PUUID

### Localization

Resource dictionaries: `src/Prometheus.Core/Resources/en-US.xaml` and `zh-CN.xaml`. Keep both files synchronized—never add a key to one without adding it to the other. Use `x:Key` for all localizable strings; avoid hardcoded text in XAML or C#.

## Testing

- Framework: xUnit with Moq for mocking and Coverlet for coverage
- Test projects: `src/Tests/<Project>.Tests/`
- Naming: `<Type>Tests.cs` with descriptive method names like `LoadAsync_WhenClientUnavailable_ReturnsEmptyResult`
- Mock `IHttpService` and `ILeagueClient` when testing services
- No coverage threshold enforced—add focused tests for changed services, ViewModels, and regressions

## Coding Conventions

- **Indentation**: 4 spaces, braces on new lines
- **Naming**: PascalCase for types/methods/properties/views/ViewModels; camelCase for parameters/locals; `_camelCase` for private fields; `I` prefix for interfaces
- **Code-behind**: Limit to view concerns only. Put navigation, commands, and state in ViewModels.
- **Namespaces**: Match project and folder structure
- **Format**: Use Visual Studio formatting or `dotnet format src/Prometheus.slnx` before committing

## Logging

- **Framework**: Serilog with JSON formatting
- **User control**: Logging is disabled by default and controlled via Settings page. When disabled, no logs are written to disk or memory.
- **Sensitive data**: Never log tokens, command lines, full Riot IDs, PUUIDs, room passwords, or chat content
- **Operation logging**: Follow `specs/player-operation-logging.md` for all player-initiated or automated actions
- **Diagnostic logging**: Technical logs (HTTP, WebSocket, exceptions) must set `Kind=Diagnostic`
- **WebSocket events**: Every received `OnJsonApiEvent` must log at `Information` level with sanitized `Data`, `EventType`, and `Uri` before dispatch
- **Log retention**: 7 days rolling; files follow pattern `log-yyyyMMdd.txt` or `prometheus-*.jsonl`

## Key Technical Constraints

- **Platform**: Windows x64 only
- **Framework**: .NET 10, WPF with Prism.DryIoc
- **JSON**: Newtonsoft.Json throughout—do not mix with System.Text.Json for LCU communication
- **HTTP client**: Singleton `HttpClient` managed by `HttpService`, initialized once per connection
- **WebSocket**: WAMP protocol, subscribe to `OnJsonApiEvent`, all events are 3-element arrays: `[8, "OnJsonApiEvent", {data, eventType, uri}]`
- **Self-contained**: Release builds use self-contained deployment; users do not need to install .NET SDK separately

## Git and Commits

- Keep commits focused and use imperative mood: `Add ...`, `Fix ...`, `Refactor ...`
- Never commit: credentials, LCU tokens, logs, `bin/`, `obj/`
- PRs should include: summary, linked issue, build/test results, screenshots/GIFs for XAML or theme changes
- Keep `en-US.xaml` and `zh-CN.xaml` synchronized in localization changes

## Common Tasks

### Adding a New Service Method

1. Read `specs/backend-conventions.md` §11 (new feature checklist)
2. Check §10.1 endpoint catalog—reuse existing endpoint if present
3. Declare endpoint constant at top of service class (relative path, no leading `/`)
4. Add method to interface in `Prometheus.Services.Interfaces`
5. Implement in corresponding service in `Prometheus.Services`
6. Include `CancellationToken cancellationToken = default` parameter
7. URL-encode query parameters with `HttpUtility.UrlEncode`
8. Document return behavior when LCU is unavailable or returns 404
9. Add xUnit test with Moq stubs for `IHttpService` or `ILeagueClient`

### Adding Real-Time Updates

1. Use `ILeagueClient.Subscribe(uri, callback)` for WebSocket event subscriptions
2. Unsubscribe in ViewModel disposal: `ILeagueClient.Unsubscribe(uri, callback)`
3. If state needs cross-module sharing, extend `LiveMatchSnapshot` instead of creating custom global events
4. Callback executes on socket thread—do not manipulate UI directly; use `Dispatcher.Invoke` or update snapshot

### Adding Automation Features

1. Read `specs/player-operation-logging.md` for logging requirements
2. Add toggle to `IGameAutomationSettings` with JSON persistence
3. Implement automation logic in `MatchService` or `GameService` with retry policy (see `specs/backend-conventions.md` §4.5)
4. Support cancellation via `CancellationToken`
5. Log with `Origin=Automation`, `Outcome`, and all required context fields
6. Deduplicate per phase instance using `_phaseVersion` / `_phaseInstance` pattern

### Modifying the Champion Select Companion Window

Read `specs/lcu-champion-select-companion.md` before changes. The companion window:
- Lives in `src/Prometheus.Shared/Views/LcuCompanionWindow.xaml`
- Managed by `ILcuCompanionWindowController` service
- Lifecycle tied to `ChampSelect` phase and LCU window visibility
- Positioning: snap to LCU right edge (or left if insufficient space)
- Content: teammate stats from `IMatchService.Current.Roster.MyTeam`, automation status
- Never use global topmost; never inject code into LCU process

## Update System (Not Yet Complete)

**WARNING**: Online updates within the application are not yet functional. Users must manually download MSI installers or portable ZIP from GitHub Releases to upgrade. Read `specs/automatic-updates.md` for the planned architecture when implementing update features.

## External Data Sources

Some features access public HTTPS endpoints for champion tier data and recommendations (see `specs/backend-conventions.md` §10.3). These requests:
- Must use the same `IHttpService` instance
- Never attach LCU credentials (enforced by `ShouldAuthenticate`)
- Use normal TLS certificate validation (not relaxed)
- Return text (often JS/JSONP format); parse manually, do not use `GetAsync<T>`

## Mandatory Workflow

**CRITICAL**: Before analyzing requirements, planning, modifying code, or writing tests:

1. Read `specs/README.md` first
2. Read all specs relevant to your task based on the index in `specs/README.md`
3. If the task spans multiple modules, read all corresponding specs
4. The `specs/` directory is the source of truth for implementation behavior and acceptance criteria
5. If a user request conflicts with a spec, point out the conflict and ask for confirmation — never silently ignore specs
6. After implementation, verify code and tests against the acceptance criteria in the relevant specs