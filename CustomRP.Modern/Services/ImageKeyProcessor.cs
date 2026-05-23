using System;

namespace CustomRP.Modern.Services;

/// <summary>
/// Matches CustomRPC 1.x image key handling: developer-portal asset names pass
/// through; direct image URLs are normalized for Discord's external asset format.
/// </summary>
public static class ImageKeyProcessor
{
    public static string Process(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "";

        key = key.Trim();

        // Portal asset names (e.g. "logo") are not URLs — pass through unchanged.
        if (Uri.TryCreate(key, UriKind.Absolute, out var uri))
        {
            if (IsMpExternalStringOverLimit(uri))
                return "";

            return uri.AbsoluteUri.Replace(uri.Host, uri.IdnHost);
        }

        return key;
    }

    public static string Proxify(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;
        return System.Text.RegularExpressions.Regex.Replace(
            key,
            @"//((cdn)|(media))\.discordapp\.((com)|(net))/",
            "//customrp.xyz/proxy/");
    }

    /// <summary>
    /// Discord builds an mp:external/… string from http(s) keys; must stay under 256 chars.
    /// </summary>
    private static bool IsMpExternalStringOverLimit(Uri uri)
    {
        var host = uri.IdnHost == "media.discordapp.net" ? "cdn.discordapp.com" : uri.IdnHost;
        var external = $"mp:external/43 characters that probably represent an id/{Uri.EscapeDataString(uri.Query)}/{uri.Scheme}/{host}{uri.AbsolutePath}";
        return external.Length > 256;
    }
}
