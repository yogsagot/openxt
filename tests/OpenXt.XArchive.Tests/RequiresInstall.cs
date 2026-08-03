using Xunit;

namespace OpenXt.XArchive.Tests;

/// <summary>
/// Marks a test that needs a real installation of one of the games. Without one it reports as
/// skipped rather than failing or silently passing, so CI stays green on machines that do not (and
/// legally need not) have a copy.
/// </summary>
public sealed class RequiresInstallFactAttribute : FactAttribute
{
    public RequiresInstallFactAttribute()
    {
        if (!InstallProbe.Any)
            Skip = InstallProbe.SkipReason;
    }
}

/// <inheritdoc cref="RequiresInstallFactAttribute"/>
public sealed class RequiresInstallTheoryAttribute : TheoryAttribute
{
    public RequiresInstallTheoryAttribute()
    {
        if (!InstallProbe.Any)
            Skip = InstallProbe.SkipReason;
    }
}

internal static class InstallProbe
{
    public const string SkipReason =
        "No X: Beyond the Frontier or X-Tension installation found; archive tests need one.";

    /// <summary>Probed once — discovery walks the filesystem and the answer cannot change mid-run.</summary>
    public static bool Any { get; } = XInstall.Discover().Count > 0;

    public static XInstall? ByKey(string gameKey) =>
        XInstall.Discover().FirstOrDefault(i =>
            string.Equals(i.Key, gameKey, StringComparison.OrdinalIgnoreCase));
}
