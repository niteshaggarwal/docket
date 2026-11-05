using System.Runtime.InteropServices.WindowsRuntime;
using Docket.Core;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace Docket.App.Services;

/// <summary>Uploaded list background images, fetched once per list and kept while the app runs.</summary>
public static class Backgrounds
{
    private static readonly Dictionary<long, BitmapImage?> Cache = [];

    public static void Forget(long projectId) => Cache.Remove(projectId);

    public static async Task<BitmapImage?> ImageAsync(long projectId)
    {
        if (Cache.TryGetValue(projectId, out var hit)) return hit;
        if (App.Client is not { } client) return null;
        BitmapImage? image = null;
        try
        {
            var bytes = await client.GetBackgroundAsync(projectId);
            if (bytes is { Length: > 0 })
            {
                using var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(bytes.AsBuffer());
                stream.Seek(0);
                image = new BitmapImage { DecodePixelWidth = 1920 };
                await image.SetSourceAsync(stream);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or VikunjaException or TaskCanceledException or System.Runtime.InteropServices.COMException) { image = null; }
        Cache[projectId] = image;
        return image;
    }
}
