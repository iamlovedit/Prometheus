using Moq;
using Prism.Events;
using Prometheus.Modules.Setting.ViewModels;
using Prometheus.Services;
using Prometheus.Services.Interfaces.Client;
using Serilog;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.ViewModels
{
    public class LogViewModelTests
    {
        [Fact]
        public void Filters_DefaultToFinalOperationsAndCanSwitchToDiagnostics()
        {
            var history = new LogHistoryService(20);
            using var logger = new LoggerConfiguration()
                .WriteTo.Sink(history.Sink)
                .CreateLogger();
            WriteOperation(logger, "match.ready_check.accept", "Manual", "Succeeded",
                "Accepted ready check");
            WriteOperation(logger, "match.reconnect", "Automation", "Failed",
                "Reconnect failed");
            WriteOperation(logger, "match.reconnect", "Automation", "Started",
                "Reconnect started");
            logger.ForContext("Kind", "Diagnostic")
                .ForContext("EventName", "lcu.websocket.event.received")
                .ForContext("Category", "WebSocket")
                .ForContext("Origin", "Observed")
                .Information("Received event");
            var viewModel = CreateViewModel(history);

            Assert.True(viewModel.IsOperationView);
            Assert.Equal(2, viewModel.OperationCount);
            Assert.Equal(1, viewModel.DiagnosticCount);
            Assert.Equal(2, viewModel.Entries.Count);

            viewModel.ShowIntermediateOperations = true;
            Assert.Equal(3, viewModel.Entries.Count);
            viewModel.ShowIntermediateOperations = false;

            viewModel.SelectedOrigin = "Automation";
            viewModel.SelectedOutcome = "Failed";

            Assert.Single(viewModel.Entries.Cast<object>());

            viewModel.ResetFiltersCommand.Execute();
            viewModel.SearchText = "event:match.ready_check.accept outcome:succeeded";

            Assert.Single(viewModel.Entries.Cast<object>());

            viewModel.ResetFiltersCommand.Execute();
            viewModel.ShowDiagnosticsCommand.Execute();

            Assert.True(viewModel.IsDiagnosticView);
            Assert.Single(viewModel.Entries.Cast<object>());
            viewModel.Destroy();
        }

        [Fact]
        public void Pause_ReportsPendingEntriesAndReloadsOnResume()
        {
            var history = new LogHistoryService(20);
            using var logger = new LoggerConfiguration()
                .WriteTo.Sink(history.Sink)
                .CreateLogger();
            var viewModel = CreateViewModel(history);
            viewModel.IsPaused = true;

            WriteOperation(logger, "match.ready_check.accept", "Manual", "Succeeded",
                "Accepted ready check");

            Assert.Equal(1, viewModel.PendingCount);
            Assert.Empty(viewModel.Entries.Cast<object>());

            viewModel.IsPaused = false;

            Assert.Equal(0, viewModel.PendingCount);
            Assert.Single(viewModel.Entries.Cast<object>());
            viewModel.Destroy();
        }

        [Fact]
        public void LoggingState_DisablesWorkbenchAndClearsVisibleEntriesAtRuntime()
        {
            var history = new LogHistoryService(20);
            var loggingControl = new LoggingControlService(false, history, _ => { });
            using var logger = new LoggerConfiguration()
                .Filter.With(loggingControl)
                .WriteTo.Sink(history.Sink)
                .CreateLogger();
            var resources = new Mock<IResourceService>();
            resources.Setup(service => service.FindResource<string>(It.IsAny<string>()))
                .Returns((string key) => key);
            var viewModel = new LogViewModel(
                new EventAggregator(),
                resources.Object,
                history,
                loggingControl);

            Assert.True(viewModel.IsLoggingDisabled);

            loggingControl.SetEnabled(true);
            WriteOperation(logger, "match.ready_check.accept", "Manual", "Succeeded",
                "Accepted ready check");

            Assert.True(viewModel.IsLoggingEnabled);
            Assert.Single(viewModel.Entries.Cast<object>());

            loggingControl.SetEnabled(false);

            Assert.True(viewModel.IsLoggingDisabled);
            Assert.Empty(viewModel.Entries.Cast<object>());
            viewModel.Destroy();
        }

        private static LogViewModel CreateViewModel(LogHistoryService history)
        {
            var resources = new Mock<IResourceService>();
            resources.Setup(service => service.FindResource<string>(It.IsAny<string>()))
                .Returns((string key) => key);
            var loggingControl = new LoggingControlService(true, history, _ => { });
            return new LogViewModel(
                new EventAggregator(),
                resources.Object,
                history,
                loggingControl);
        }

        private static void WriteOperation(
            ILogger logger,
            string eventName,
            string origin,
            string outcome,
            string message)
        {
            logger.ForContext("Kind", "Operation")
                .ForContext("EventName", eventName)
                .ForContext("Category", "Match")
                .ForContext("Origin", origin)
                .ForContext("Outcome", outcome)
                .ForContext("EventId", Guid.NewGuid())
                .ForContext("OperationId", Guid.NewGuid())
                .ForContext("AppSessionId", Guid.NewGuid())
                .ForContext("Module", "Tests")
                .Information("{DisplayMessage}", message);
        }
    }
}
