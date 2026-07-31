using System.Text.Json;
using Prometheus.Update;

namespace Prometheus.Updater;

internal static class BootstrapperStateStore
{
    public static string GetPath(string installRoot) => Path.Combine(installRoot, "current.json");

    public static BootstrapperState Load(string installRoot)
    {
        var path = GetPath(installRoot);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Prometheus current.json was not found.", path);
        }

        var state = JsonSerializer.Deserialize(File.ReadAllBytes(path),
            UpdateJsonContext.Default.BootstrapperState)
            ?? throw new InvalidDataException("Prometheus current.json is empty.");
        UpdateValidation.ValidateBootstrapperState(state);

        return state;
    }

    public static void Save(string installRoot, BootstrapperState state)
    {
        UpdatePaths.WriteJsonAtomic(GetPath(installRoot), state,
            UpdateJsonContext.Default.BootstrapperState);
    }
}
