namespace Prometheus.ReleaseTool;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            var options = ReleaseOptions.Parse(args);
            await ReleasePublisher.PublishAsync(options).ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
