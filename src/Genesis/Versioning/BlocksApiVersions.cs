using System.Globalization;

namespace Blocks.Genesis;

/// <summary>One dated API version. Deprecated and Sunset are set when a newer version replaces it (12 months, D18).</summary>
public sealed record BlocksApiVersion(string Version, DateOnly? Deprecated = null, DateOnly? Sunset = null);

/// <summary>The outcome of resolving a request's API version (decision 0003, I11-WP5).</summary>
public sealed record BlocksVersionResolution(string? Version, string? Error, BlocksApiVersion? Entry);

/// <summary>
/// The dated API versions this Genesis supports, and the rule that picks one for a request. A new value is added only with a
/// breaking change; the oldest entry is the baseline that a project without a pin gets.
/// </summary>
public static class BlocksApiVersions
{
    public const string HeaderName = "Blocks-Version";
    public const string ItemKey = "Blocks-Version";
    public const string UnknownMessage = "Bad_Request: Unknown_Blocks_Version";
    public const string RetiredMessage = "Bad_Request: Retired_Blocks_Version";

    /// <summary>Ordered oldest first. The first value is the baseline.</summary>
    public static IReadOnlyList<BlocksApiVersion> Supported { get; } = [new BlocksApiVersion("2026-11-25")];

    public static string Baseline => Supported[0].Version;

    public static string Latest => Supported[^1].Version;

    /// <summary>
    /// Header first, else the project's pin (null means the baseline), else the latest when the request has no project.
    /// An unknown, future or malformed date is an error, never a fallback; a date past its sunset is reported as retired.
    /// </summary>
    public static BlocksVersionResolution Resolve(string? header, string? pin, bool hasProject, DateOnly today, IReadOnlyList<BlocksApiVersion>? supported = null)
    {
        supported ??= Supported;
        var requested = !string.IsNullOrWhiteSpace(header)
            ? header.Trim()
            : hasProject
                ? (string.IsNullOrWhiteSpace(pin) ? supported[0].Version : pin.Trim())
                : supported[^1].Version;

        var entry = supported.FirstOrDefault(v => string.Equals(v.Version, requested, StringComparison.Ordinal));
        if (entry is null)
        {
            return new BlocksVersionResolution(null, UnknownMessage, null);
        }

        if (entry.Sunset is { } sunset && sunset < today)
        {
            return new BlocksVersionResolution(null, RetiredMessage, entry);
        }

        return new BlocksVersionResolution(entry.Version, null, entry);
    }

    internal static string SunsetHeader(DateOnly date)
        => new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Utc).ToString("R", CultureInfo.InvariantCulture);
}
