using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DevSAK.Models;
using Microsoft.Win32;

namespace DevSAK.Services;

public static class UpdateInstallerService
{
    public const string AppMutexName = @"Local\DevSAK.Running";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{B115A936-DE0A-48D3-8B42-E13296D49F1A}_is1";

    public static bool IsSupportedInstallation()
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64) return false;
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
        var location = key?.GetValue("InstallLocation") as string;
        return !string.IsNullOrWhiteSpace(location) &&
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(location)).Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)), StringComparison.OrdinalIgnoreCase);
    }

    public static async Task StartAsync(string installer, AppUpdate update, CancellationToken cancellationToken)
    {
        if (!IsSupportedInstallation())
            throw new InvalidOperationException("A atualização automática requer a instalação x64 pelo instalador oficial do DevSAK. Builds de desenvolvimento, versões portáteis e MSIX devem ser atualizados manualmente.");
        // Keep the verified file locked against writes until the installer has started.
        await using var file = new FileStream(installer, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        var digest = await SHA256.HashDataAsync(file, cancellationToken);
        if (file.Length != update.Size || !Convert.ToHexString(digest).Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("O instalador foi alterado após o download. Baixe a atualização novamente.");
        var version = FileVersionInfo.GetVersionInfo(installer);
        if (version.ProductMajorPart != update.Version.Major || version.ProductMinorPart != update.Version.Minor ||
            version.ProductBuildPart != update.Version.Build)
            throw new InvalidDataException("A versão do instalador não corresponde à Release publicada.");
        var log = Path.Combine(Path.GetDirectoryName(installer)!, "install.log");
        var start = new ProcessStartInfo(installer)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(installer)!
        };
        start.ArgumentList.Add("/SILENT");
        start.ArgumentList.Add("/SP-");
        start.ArgumentList.Add("/NORESTART");
        start.ArgumentList.Add("/NOCLOSEAPPLICATIONS");
        start.ArgumentList.Add("/DEVSAKUPDATE=1");
        start.ArgumentList.Add($"/DIR={Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)}");
        start.ArgumentList.Add($"/LOG={log}");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Não foi possível iniciar o instalador.");
        // Catch immediate startup failures before closing the app.
        await Task.Delay(500, cancellationToken);
        if (process.HasExited)
            throw new InvalidOperationException($"O instalador encerrou antes de iniciar a atualização (código {process.ExitCode}). Consulte {log}.");
    }
}
