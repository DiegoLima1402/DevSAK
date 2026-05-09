using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace DevSAK.Services
{
    public sealed class ReleaseNotesService
    {
        private readonly AppSettingsService _settingsService = AppSettingsService.Instance;

        public Version CurrentVersion => GetCurrentVersion();

        public async Task<ReleaseNotesInfo?> GetPendingReleaseNotesAsync()
        {
            var currentVersion = CurrentVersion;
            var settings = await _settingsService.LoadAsync().ConfigureAwait(false);

            if (TryParseVersion(settings.LastShownReleaseNotesVersion, out var lastShownVersion) &&
                currentVersion <= lastShownVersion)
            {
                return null;
            }

            var releaseNotes = await LoadReleaseNotesAsync(currentVersion).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(releaseNotes))
            {
                return null;
            }

            return new ReleaseNotesInfo(currentVersion, releaseNotes);
        }

        public async Task MarkReleaseNotesAsShownAsync(Version version)
        {
            var settings = await _settingsService.LoadAsync().ConfigureAwait(false);
            settings.LastShownReleaseNotesVersion = ToDisplayVersion(version);
            await _settingsService.SaveAsync(settings).ConfigureAwait(false);
        }

        public static string ToDisplayVersion(Version version)
            => version.Revision > 0
                ? $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}"
                : $"{version.Major}.{version.Minor}.{version.Build}";

        private static Version GetCurrentVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            var cleanVersion = informationalVersion?.Split('+')[0];
            if (TryParseVersion(cleanVersion, out var parsedInformationalVersion))
            {
                return parsedInformationalVersion;
            }

            return assembly.GetName().Version ?? new Version(0, 0, 0);
        }

        private static bool TryParseVersion(string? value, out Version version)
        {
            version = new Version(0, 0, 0);
            return !string.IsNullOrWhiteSpace(value) && Version.TryParse(value.Trim(), out version!);
        }

        private static async Task<string?> LoadReleaseNotesAsync(Version version)
        {
            var releaseNotesPath = Path.Combine(
                AppContext.BaseDirectory,
                "Resources",
                "ReleaseNotes",
                $"{ToDisplayVersion(version)}.md");

            if (!File.Exists(releaseNotesPath))
            {
                return null;
            }

            return await File.ReadAllTextAsync(releaseNotesPath).ConfigureAwait(false);
        }

    }

    public sealed record ReleaseNotesInfo(Version Version, string Markdown);
}
