using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CustomRP.Modern.Controls;

/// <summary>
/// Attached property that downloads an image URL and sets it as the
/// <see cref="Image.Source"/>. Replaces our AsyncImageLoader dependency so
/// we own the failure mode and the cache.
/// </summary>
public static class UrlImage
{
    public static readonly AttachedProperty<string?> SourceProperty =
        AvaloniaProperty.RegisterAttached<Image, string?>("Source", typeof(UrlImage));

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
        DefaultRequestHeaders =
        {
            { "User-Agent", "CustomRP.Modern/2.0 (+https://customrp.xyz)" },
        },
    };

    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new(StringComparer.Ordinal);

    static UrlImage()
    {
        SourceProperty.Changed.AddClassHandler<Image>(OnSourceChanged);
    }

    public static void SetSource(AvaloniaObject obj, string? value) =>
        obj.SetValue(SourceProperty, value);

    public static string? GetSource(AvaloniaObject obj) =>
        obj.GetValue(SourceProperty);

    private static void OnSourceChanged(Image image, AvaloniaPropertyChangedEventArgs e)
    {
        var raw = e.NewValue as string;
        var url = Normalize(raw);

        if (url is null)
        {
            image.Source = null;
            return;
        }

        if (Cache.TryGetValue(url, out var cached))
        {
            image.Source = cached;
            return;
        }

        _ = LoadAsync(image, url);
    }

    private static async Task LoadAsync(Image image, string url)
    {
        Bitmap? bitmap = null;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var bytes = await Http.GetByteArrayAsync(url, cts.Token);
            using var stream = new MemoryStream(bytes);
            bitmap = new Bitmap(stream);
        }
        catch
        {
            // Unreachable / invalid image / timeout — leave bitmap null so
            // the UI hides via its IsVisible binding.
        }

        Cache[url] = bitmap;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Only apply if the source URL didn't change while we were loading.
            if (GetSource(image) == GetOriginalValue(url))
                image.Source = bitmap;
        });
    }

    private static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return trimmed;
        // Not a URL — likely a Discord asset key. Can't preview it locally.
        return null;
    }

    private static string GetOriginalValue(string normalized) => normalized;
}
