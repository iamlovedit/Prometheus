using Prometheus.Core.Models;
using System;
using System.Collections.Generic;

namespace Prometheus.Services.Interfaces
{
    /// <summary>
    /// Carries a single freshly-captured <see cref="LogEntry"/>. Raised on the logging thread;
    /// consumers must marshal to the UI thread before touching observable collections.
    /// </summary>
    public sealed class LogEntryLoggedEventArgs : EventArgs
    {
        public LogEntry Entry { get; }

        public LogEntryLoggedEventArgs(LogEntry entry)
        {
            Entry = entry;
        }
    }

    /// <summary>
    /// In-memory ring buffer of recent application log events. Captures whatever Serilog
    /// emits (file sink continues to persist to disk in parallel) so the UI can show the
    /// logs recorded during the running session. Intended lifetime: singleton.
    /// </summary>
    public interface ILogHistoryService
    {
        /// <summary>Maximum number of entries retained in memory.</summary>
        int Capacity { get; }

        /// <summary>
        /// Returns an immutable point-in-time copy of the currently buffered entries,
        /// ordered oldest-to-newest. Safe to call from any thread.
        /// </summary>
        IReadOnlyList<LogEntry> GetSnapshot();

        /// <summary>Raised (on the logging thread) whenever a new entry is captured.</summary>
        event EventHandler<LogEntryLoggedEventArgs> EntryLogged;

        /// <summary>Raised (on the calling thread) when the buffer is cleared.</summary>
        event EventHandler Cleared;

        /// <summary>Discards every buffered entry.</summary>
        void Clear();
    }
}
