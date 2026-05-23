namespace CustomRP.Modern.Models;

public enum AutoUpdateStrategy
{
    Off,
    WindowTitle,
    BrowserUrl,
}

public sealed class AutoUpdateConfig
{
    public bool Enabled { get; set; }
    public AutoUpdateStrategy Strategy { get; set; } = AutoUpdateStrategy.WindowTitle;
    public string ProcessName { get; set; } = "";
    public int IntervalSeconds { get; set; } = 3;
    public string DetailsTemplate { get; set; } = "{title}";
    public string StateTemplate { get; set; } = "";
    public bool UseFaviconAsSmallImage { get; set; }
    public string Button1TextTemplate { get; set; } = "";
    public string Button1UrlTemplate { get; set; } = "";
    public string Button2TextTemplate { get; set; } = "";
    public string Button2UrlTemplate { get; set; } = "";
}

/// <summary>
/// A point-in-time view of the watched process, produced by <c>AutoUpdateService</c>
/// and consumed by the editor / RPC push pipeline.
/// </summary>
public sealed class LiveSnapshot
{
    public string ProcessName { get; init; } = "";
    public string WindowTitle { get; init; } = "";
    public string? Url { get; init; }
    public string? FaviconUrl { get; init; }
    public bool ProcessFound { get; init; }
}
