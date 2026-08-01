using Newtonsoft.Json;
using Prometheus.Core.Models;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using Serilog;
using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace Prometheus.Services.Client
{
    public class ClientService : IClientService
    {
        private const string QueuesEndpoint = "lol-game-queues/v1/queues";

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool PathCanonicalize(StringBuilder dst, string src);

        [DllImport("shell32.dll", EntryPoint = "CommandLineToArgvW", SetLastError = true)]
        private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine, out int pNumArgs);

        private readonly string _client = "riotclient/{0}";
        private readonly IHttpService _httpService;
        private readonly SemaphoreSlim _queuesGate = new(1, 1);
        private IReadOnlyList<GameQueue> _queues = Array.Empty<GameQueue>();

        public ClientService(IHttpService httpService)
        {
            _httpService = httpService;
        }

        public async Task<string> GetInstallLocation()
        {
            var path = await _httpService.GetAsync("data-store/v1/install-dir");
            if (string.IsNullOrEmpty(path))
            {
                return default;
            }
            return ConvertPath(path);
        }

        public async Task QuitClientAsync()
        {
            await _httpService.PostAsync(string.Format(_client, "unload"), null);
        }

        private static string ConvertPath(string path)
        {
            var pathParts = path.Split(':');
            return path.Replace(pathParts[0], pathParts[0].ToUpper()).Replace(@"\\", @"\");
        }

        public async Task<IReadOnlyList<GameQueue>> GetQueuesAsync(
            CancellationToken cancellationToken = default)
        {
            var cachedQueues = _queues;
            if (cachedQueues.Count > 0)
            {
                return cachedQueues;
            }

            await _queuesGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cachedQueues = _queues;
                if (cachedQueues.Count > 0)
                {
                    return cachedQueues;
                }

                try
                {
                    var queues = await _httpService.GetAsync<List<GameQueue>>(
                        QueuesEndpoint, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    if (queues is null || queues.Count == 0)
                    {
                        return Array.Empty<GameQueue>();
                    }

                    cachedQueues = queues.ToArray();
                    _queues = cachedQueues;
                    return cachedQueues;
                }
                catch (HttpRequestException exception)
                {
                    Log.Error(exception, "Unable to load LCU game queues");
                    return Array.Empty<GameQueue>();
                }
                catch (JsonException exception)
                {
                    Log.Error(exception, "Unable to parse LCU game queues");
                    return Array.Empty<GameQueue>();
                }
            }
            finally
            {
                _queuesGate.Release();
            }
        }

        public async Task SetForgeground()
        {
            await _httpService.PostAsync(string.Format(_client, "ux-show"), null);
        }

        public async Task FlashClient()
        {
            await _httpService.PostAsync(string.Format(_client, "ux-flash"), null);
        }

        public async Task MinimizeClient()
        {
            await _httpService.PostAsync(string.Format(_client, "ux-minimize"), null);
        }


        public int GetClientProcessId()
        {
            return GetClientProcess()?.Id ?? 0;
        }

        public Dictionary<string, string> GetClientCommandLines()
        {
            var process = GetClientProcess();
            if (process is null)
            {
                return default;
            }
            var commandLine = GetCommandLine(process);
            if (string.IsNullOrEmpty(commandLine))
            {
                return default;
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
            return Process.GetProcesses().FirstOrDefault(p => p.ProcessName == "LeagueClientUx");
        }

        [Obsolete]
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
