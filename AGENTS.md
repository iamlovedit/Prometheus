# Repository Guidelines

## Project Structure & Module Organization

Prometheus is a Windows-only .NET 10 WPF application built with Prism. The solution is `src/Prometheus.slnx`. The shell and application entry point live in `src/Prometheus/`. Feature projects follow `src/Prometheus.Modules.<Feature>/`, typically pairing `Views/` with `ViewModels/` and a `<Feature>Module.cs` registration class. Shared models, events, MVVM bases, localization, and resources belong in `src/Prometheus.Core/`; reusable controls and presentation models belong in `src/Prometheus.Shared/`. Service contracts and implementations are separated under `src/Services/`. The updater is in `src/Prometheus.Updater/`, tests are under `src/Tests/`, and README screenshots are stored in `doc/images/`.

## Build, Test, and Development Commands

Run commands from the repository root on Windows with the .NET 10 SDK:

```powershell
dotnet restore src/Prometheus.slnx
dotnet build src/Prometheus.slnx -c Release
dotnet test src/Prometheus.slnx -c Release
dotnet run --project src/Prometheus/Prometheus.csproj
```

Restore downloads NuGet dependencies; build compiles every solution project; test runs the xUnit test project; run starts the WPF client. Stop `Prometheus.exe` before rebuilding, because the running application locks copied module DLLs.

## Coding Style & Naming Conventions

Use four-space indentation, braces on new lines, and namespaces matching project and folder names. Use PascalCase for types, methods, properties, views, and view models; camelCase for parameters and locals; `_camelCase` for private fields; and `I` prefixes for interfaces. Keep code-behind limited to view concerns and place navigation, commands, and state in view models. Pair `ExampleView.xaml` with `ExampleViewModel.cs`. No repository-specific formatter is configured, so use Visual Studio formatting or `dotnet format src/Prometheus.slnx` before submitting broad C# changes.

## Testing Guidelines

Tests use xUnit, Moq, and Coverlet. Add tests in `src/Tests/<Project>.Tests/`, name classes `<Type>Tests`, and prefer descriptive methods such as `LoadAsync_WhenClientUnavailable_ReturnsEmptyResult`. The current test project is only a scaffold, and no coverage threshold is enforced; add focused tests for changed services, view models, and regressions. Collect coverage with `dotnet test src/Prometheus.slnx --collect:"XPlat Code Coverage"`.

## Commit & Pull Request Guidelines

Recent history favors short imperative subjects such as `Add ...`, `Refactor ...`, and `Update ...`. Keep each commit focused and describe the concrete change. Pull requests should include a summary, linked issue when applicable, build/test results, and screenshots or GIFs for XAML or theme changes. Note localization updates and keep `en-US.xaml` and `zh-CN.xaml` aligned. Never commit credentials, League Client tokens, logs, or generated `bin/` and `obj/` content.

## Mandatory Workflow

**CRITICAL**: Before analyzing requirements, planning, modifying code, or writing tests:

1. Read `specs/README.md` first
2. Read all specs relevant to your task based on the index in `specs/README.md`
3. If the task spans multiple modules, read all corresponding specs
4. The `specs/` directory is the source of truth for implementation behavior and acceptance criteria
5. If a user request conflicts with a spec, point out the conflict and ask for confirmation — never silently ignore specs
6. After implementation, verify code and tests against the acceptance criteria in the relevant specs