using Serilog;
using System.Resources;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Prometheus.Desktop.Services
{
    public static class PrometheusIconSource
    {
        private const string IconResourcePath = "Prometheus.ico";

        public static ImageSource Large { get; } = LoadLargestFrame();

        private static ImageSource LoadLargestFrame()
        {
            try
            {
                var assembly = typeof(PrometheusIconSource).Assembly;
                var bundleName = assembly.GetManifestResourceNames()
                    .SingleOrDefault(name => name.EndsWith(
                        ".g.resources", StringComparison.Ordinal));
                if (string.IsNullOrEmpty(bundleName))
                {
                    throw new InvalidOperationException(
                        "Unable to locate the WPF resource bundle.");
                }

                using var bundle = assembly.GetManifestResourceStream(bundleName);
                using var resources = new ResourceReader(bundle ?? throw new
                    InvalidOperationException(
                        $"Unable to open WPF resource bundle '{bundleName}'."));
                var entries = resources.GetEnumerator();
                Stream iconStream = null;
                while (entries.MoveNext())
                {
                    if (string.Equals(
                            entries.Key as string,
                            IconResourcePath,
                            StringComparison.OrdinalIgnoreCase) &&
                        entries.Value is Stream resourceStream)
                    {
                        iconStream = resourceStream;
                        break;
                    }
                }

                if (iconStream is null)
                {
                    throw new InvalidOperationException(
                        $"Unable to locate icon resource '{IconResourcePath}'.");
                }

                var decoder = new IconBitmapDecoder(
                    iconStream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                var frame = decoder.Frames
                    .OrderByDescending(item => item.PixelWidth * item.PixelHeight)
                    .First();
                frame.Freeze();
                return frame;
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Unable to load the large Prometheus icon frame");
                var empty = new DrawingImage();
                empty.Freeze();
                return empty;
            }
        }
    }
}
