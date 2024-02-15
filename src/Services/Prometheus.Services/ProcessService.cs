using Prometheus.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;

namespace Prometheus.Services
{
    public class ProcessService : IProcessService
    {
        public int GetClientProcessId()
        {
            return GetClientProcess()?.Id ?? 0;
        }

        public Dictionary<string, string> GetProcessCommandLines()
        {
            var process = GetClientProcess();
            var commands = GetCommandLines(process);
            var argumentsDict = new Dictionary<string, string>();
            foreach (var command in commands)
            {
                var equalIndex = command.IndexOf('=');
                if (equalIndex != -1)
                {
                    var key = command.Substring(0, equalIndex);
                    var value = command.Substring(equalIndex + 1);
                    argumentsDict[key] = value;
                }
                else
                {
                    argumentsDict[command] = string.Empty;
                }
            }
            return argumentsDict;
        }

        private Process GetClientProcess()
        {
            return Process.GetProcesses().FirstOrDefault(p => p.ProcessName == "LeagueClientUx") ?? throw new ArgumentNullException("process", "client not found!");
        }

        private IEnumerable<string> GetCommandLines(Process process)
        {
            var query = "SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + process.Id;
            using var searcher = new ManagementObjectSearcher(query);
            using var collection = searcher.Get();
            var commands = collection
                           .OfType<ManagementObject>()
                           .Select(o => o["Commandline"].ToString())
                           .FirstOrDefault();
            if (commands is null)
            {
                throw new ArgumentNullException("commands", "commands is null!");
            }
            return commands.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Replace('"', ' ').Trim());
        }
    }
}
