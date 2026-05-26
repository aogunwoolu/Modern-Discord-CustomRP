using CustomRP.Modern.Models;
using DiscordRPC;
using System;
using System.Linq;

namespace CustomRP.Modern.Services;

/// <summary>
/// Builds <see cref="RichPresence"/> payloads matching the legacy CustomRP rules
/// closely enough for Discord to accept and display them.
/// </summary>
internal static class PresencePayloadBuilder
{
    /// <summary>Official CustomRP test application — same default as CustomRPC 1.x.</summary>
    public const string DefaultClientId = "896771305108553788";

    private const int MaxLineLength = 128;
    private const int MaxButtonLabel = 32;
    private const int MaxButtonUrl = 512;

    public static RichPresence Build(Preset preset)
    {
        // Legacy CustomRP passes empty strings, not null — null can break serialization.
        var presence = new RichPresence
        {
            Name = Truncate(preset.ActivityName ?? "", MaxLineLength),
            Type = (ActivityType)(int)preset.Type,
            StatusDisplay = (StatusDisplayType)(int)preset.Display,
            Details = Truncate(preset.Details ?? "", MaxLineLength),
            State = Truncate(preset.State ?? "", MaxLineLength),
            DetailsUrl = Truncate(preset.DetailsUrl ?? "", MaxLineLength),
            StateUrl = Truncate(preset.StateUrl ?? "", MaxLineLength),
            Party = new Party
            {
                ID = preset.PartySize > 0 && preset.PartyMax > 0 ? "CustomRP" : "",
                Size = Math.Max(0, preset.PartySize),
                Max = Math.Max(0, preset.PartyMax),
            },
        };

        if (preset.PartyMax < preset.PartySize)
            presence.Party.Max = presence.Party.Size;

        // Always send Assets (legacy behaviour) — portal keys and https image URLs both work.
        presence.Assets = new Assets
        {
            LargeImageKey = ImageKeyProcessor.Proxify(ImageKeyProcessor.Process(preset.LargeImage.Key)),
            LargeImageText = preset.LargeImage.Text ?? "",
            LargeImageUrl = preset.LargeImage.Url ?? "",
            SmallImageKey = ImageKeyProcessor.Proxify(ImageKeyProcessor.Process(preset.SmallImage.Key)),
            SmallImageText = preset.SmallImage.Text ?? "",
            SmallImageUrl = preset.SmallImage.Url ?? "",
        };

        switch (preset.Timestamps)
        {
            case TimestampMode.SinceStart:
                presence.Timestamps = Timestamps.Now;
                break;
            case TimestampMode.LocalTime:
                presence.Timestamps = new Timestamps(DateTime.Today.ToUniversalTime());
                break;
            case TimestampMode.Custom:
                presence.Timestamps = preset.CustomTimestampEndEnabled
                    ? new Timestamps(preset.CustomTimestampStart.ToUniversalTime(),
                                     preset.CustomTimestampEnd.ToUniversalTime())
                    : new Timestamps(preset.CustomTimestampStart.ToUniversalTime());
                break;
        }

        var liveButtons = preset.Buttons
            .Where(b => !b.IsEmpty &&
                        !string.IsNullOrWhiteSpace(b.Text) &&
                        !string.IsNullOrWhiteSpace(b.Url))
            .Select(b => new DiscordRPC.Button
            {
                Label = Truncate(b.Text!, MaxButtonLabel),
                Url = NormalizeButtonUrl(b.Url!),
            })
            .Where(b => !string.IsNullOrEmpty(b.Url))
            .Take(2)
            .ToArray();
        if (liveButtons.Length > 0)
            presence.Buttons = liveButtons;

        if (string.IsNullOrWhiteSpace(presence.Name))
            presence.Name = "CustomRP";

        return presence;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value.Substring(0, max);

    private static string NormalizeButtonUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        if (!url.Contains("://")) url = "https://" + url;
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var parsed))
                url = parsed.AbsoluteUri.Replace(parsed.Host, parsed.IdnHost);
        }
        catch { }
        return url.Length <= MaxButtonUrl ? url : url.Substring(0, MaxButtonUrl);
    }

    public static string Summarize(RichPresence p)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrWhiteSpace(p.Name)) parts.Add($"name: {p.Name}");
        if (!string.IsNullOrWhiteSpace(p.Details)) parts.Add($"details: {p.Details}");
        if (!string.IsNullOrWhiteSpace(p.State)) parts.Add($"state: {p.State}");
        if (!string.IsNullOrWhiteSpace(p.Assets?.LargeImageKey)) parts.Add("+large");
        if (!string.IsNullOrWhiteSpace(p.Assets?.SmallImageKey)) parts.Add("+small");
        if (p.Buttons is { Length: > 0 }) parts.Add($"+{p.Buttons.Length} button(s)");
        if (p.Timestamps is not null) parts.Add("+timestamps");
        return parts.Count == 0 ? "(empty)" : string.Join(" · ", parts);
    }
}
