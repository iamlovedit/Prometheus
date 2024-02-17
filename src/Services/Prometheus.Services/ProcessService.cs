using Prometheus.Core.Exceptions;
using Prometheus.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;

namespace Prometheus.Services
{
    public partial class ProcessService : IProcessService
    {
        [LibraryImport("shell32.dll", EntryPoint = "CommandLineToArgvW", SetLastError = true)]
        private static partial IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine, out int pNumArgs);

        public int GetClientProcessId()
        {
            return GetClientProcess()?.Id ?? 0;
        }

        public Dictionary<string, string> GetProcessCommandLines()
        {
            var process = GetClientProcess();
            var commandLine = GetCommandLine(process);
            if (string.IsNullOrEmpty(commandLine))
            {
                throw new ClientNotFoundException();
            }
            var arguments = CommandLineToArgs(commandLine);
            var argumentsDict = new Dictionary<string, string>();
            foreach (var argument in arguments)
            {
                var equalIndex = argument.IndexOf('=');
                if (equalIndex != -1)
                {
                    var key = argument.Substring(0, equalIndex);
                    var value = argument.Substring(equalIndex + 1);
                    argumentsDict[key] = value;
                }
                else
                {
                    argumentsDict[argument] = string.Empty;
                }
            }
            return argumentsDict;
        }

        private Process GetClientProcess()
        {
            return Process.GetProcesses().FirstOrDefault(p => p.ProcessName == "LeagueClientUx") ?? throw new ClientNotFoundException();
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

        private string GetCommandLine(Process process)
        {
            var query = "SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + process.Id;
            using var searcher = new ManagementObjectSearcher(query);
            using var collection = searcher.Get();
            return collection.OfType<ManagementObject>().Select(o => o["Commandline"].ToString()).FirstOrDefault();
        }

        public string[] CommandLineToArgs(string commandLine)
        {
            var argv = CommandLineToArgvW(commandLine, out var argumentCount);
            if (argv == IntPtr.Zero)
            {
                throw new Win32Exception();
            }
            try
            {
                var arguments = new string[argumentCount];
                for (var i = 0; i < arguments.Length; i++)
                {
                    var p = Marshal.ReadIntPtr(argv, i * IntPtr.Size);
                    arguments[i] = Marshal.PtrToStringUni(p);
                }
                return arguments;
            }
            finally
            {
                Marshal.FreeHGlobal(argv);
            }
        }
    }
}
