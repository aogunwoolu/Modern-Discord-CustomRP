using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CustomRP.Modern.Services;

/// <summary>
/// Reads the active-tab URL from a Firefox / Zen / LibreWolf session recovery
/// file (mozlz4 format). Used as a last-resort fallback when UI Automation
/// cannot access the browser's address bar across process isolation boundaries.
///
/// Firefox writes the recovery file roughly every 15 seconds while running;
/// the file is read with FileShare.ReadWrite so it can be read while Zen holds it.
/// </summary>
internal static class GeckoSessionReader
{
    private static readonly (string ProcessFragment, string AppDataSubdir)[] BrowserDirs =
    {
        ("zen",       @"zen\Profiles"),
        ("firefox",   @"Mozilla\Firefox\Profiles"),
        ("librewolf", @"librewolf\Profiles"),
    };

    /// <summary>
    /// Returns the URL of the currently active tab, or null if it cannot be read.
    /// <paramref name="processName"/> is matched case-insensitively (e.g. "zen").
    /// </summary>
    public static string? TryGetActiveTabUrl(string processName)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        foreach (var (fragment, sub) in BrowserDirs)
        {
            if (processName.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            var profilesRoot = Path.Combine(appData, sub);
            if (!Directory.Exists(profilesRoot)) continue;

            // Pick the most recently modified recovery file across all profiles.
            var recovery = Directory
                .EnumerateDirectories(profilesRoot)
                .Select(d => Path.Combine(d, "sessionstore-backups", "recovery.jsonlz4"))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (recovery is null) continue;

            try
            {
                var json = DecodeMozLz4(recovery);
                return ParseActiveTabUrl(json);
            }
            catch { /* corrupt / locked file — try next profile */ }
        }

        return null;
    }

    // -------------------------------------------------------------------------
    // mozlz4 decoding
    // Format: 8-byte ASCII magic "mozLz40\0", 4-byte LE uint32 decompressed
    // length, then an LZ4 block-compressed payload.

    private static string DecodeMozLz4(string path)
    {
        byte[] raw;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                       FileShare.ReadWrite | FileShare.Delete))
        {
            raw = new byte[fs.Length];
            _ = fs.Read(raw, 0, raw.Length);
        }

        const int HeaderMagicLen = 8;
        const int HeaderTotalLen = 12; // magic (8) + uint32 length (4)
        if (raw.Length < HeaderTotalLen)
            throw new InvalidDataException("mozlz4 file too small");

        var decompLen = (int)BitConverter.ToUInt32(raw, HeaderMagicLen);
        var payload   = Lz4BlockDecompress(raw, HeaderTotalLen, raw.Length - HeaderTotalLen, decompLen);
        return Encoding.UTF8.GetString(payload);
    }

    /// <summary>
    /// Minimal LZ4 block-format decompressor.
    /// See: https://github.com/lz4/lz4/blob/dev/doc/lz4_Block_format.md
    /// </summary>
    private static byte[] Lz4BlockDecompress(byte[] src, int srcOff, int srcLen, int dstLen)
    {
        var dst  = new byte[dstLen];
        int sPos = srcOff, sEnd = srcOff + srcLen, dPos = 0;

        while (sPos < sEnd)
        {
            var token = src[sPos++];

            // Literal length
            var litLen = token >> 4;
            if (litLen == 15)
            {
                byte b;
                do { b = src[sPos++]; litLen += b; } while (b == 255);
            }

            Buffer.BlockCopy(src, sPos, dst, dPos, litLen);
            sPos += litLen;
            dPos += litLen;

            if (sPos >= sEnd) break; // last sequence has no match

            // Match offset (LE uint16)
            var offset = src[sPos] | (src[sPos + 1] << 8);
            sPos += 2;

            // Match length
            var matchLen = (token & 0xF) + 4;
            if ((token & 0xF) == 15)
            {
                byte b;
                do { b = src[sPos++]; matchLen += b; } while (b == 255);
            }

            // Copy (may overlap — must byte-by-byte)
            var copyFrom = dPos - offset;
            for (var i = 0; i < matchLen; i++)
                dst[dPos++] = dst[copyFrom++];
        }

        return dst;
    }

    // -------------------------------------------------------------------------
    // Session JSON parsing
    // Relevant shape:
    // { "windows": [ { "selected": <1-based>, "tabs": [ { "index": <1-based>,
    //   "entries": [ { "url": "..." } ] } ] } ] }

    private static string? ParseActiveTabUrl(string json)
    {
        using var doc = JsonDocument.Parse(json,
            new JsonDocumentOptions { AllowTrailingCommas = true });
        var root = doc.RootElement;

        if (!root.TryGetProperty("windows", out var windows) ||
            windows.ValueKind != JsonValueKind.Array ||
            windows.GetArrayLength() == 0)
            return null;

        var window = windows[0];

        if (!window.TryGetProperty("tabs",     out var tabs) ||
            !window.TryGetProperty("selected", out var selectedProp))
            return null;

        // "selected" is 1-based.
        var tabIdx = Math.Clamp(selectedProp.GetInt32() - 1, 0, tabs.GetArrayLength() - 1);
        var tab    = tabs[tabIdx];

        if (!tab.TryGetProperty("entries", out var entries) ||
            !tab.TryGetProperty("index",   out var indexProp) ||
            entries.GetArrayLength() == 0)
            return null;

        // "index" is 1-based (history position within the tab).
        var entIdx = Math.Clamp(indexProp.GetInt32() - 1, 0, entries.GetArrayLength() - 1);
        var entry  = entries[entIdx];

        if (!entry.TryGetProperty("url", out var urlProp)) return null;

        var url = urlProp.GetString();
        if (string.IsNullOrWhiteSpace(url))              return null;
        if (url.StartsWith("about:", StringComparison.Ordinal)) return null;
        if (url.StartsWith("moz-extension://", StringComparison.Ordinal)) return null;

        return url;
    }
}
