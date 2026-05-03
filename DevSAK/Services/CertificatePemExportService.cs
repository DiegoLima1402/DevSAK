using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevSAK.Models;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.OpenSsl;

namespace DevSAK.Services
{
    public sealed class CertificatePemExportService
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        public async Task ExportAsync(
            CertificateExportSettings settings,
            IProgress<CertificateExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (settings is null)
                throw new ArgumentNullException(nameof(settings));

            var inputPath = (settings.InputCertificatePath ?? string.Empty).Trim();
            var outputFolder = (settings.OutputFolder ?? string.Empty).Trim();
            var baseName = SanitizeBaseFileName((settings.OutputBaseFileName ?? string.Empty).Trim());
            var publicName = (settings.PublicPemFileName ?? string.Empty).Trim();
            var privateName = (settings.PrivatePemFileName ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(inputPath))
                throw new CertificateExportException("Selecione um arquivo de certificado.");

            if (!File.Exists(inputPath))
                throw new CertificateExportException("Arquivo de certificado não encontrado.");

            if (string.IsNullOrWhiteSpace(outputFolder))
                throw new CertificateExportException("Selecione a pasta de saída.");

            if (string.IsNullOrWhiteSpace(baseName))
                throw new CertificateExportException("Informe um nome base para os arquivos de saída.");

            var ext = Path.GetExtension(inputPath)?.Trim().ToLowerInvariant() ?? string.Empty;

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new CertificateExportProgress(2, "Validando opções..."));

            ValidateModeForExtension(settings.ExportMode, ext);
            ValidateOutputFileNames(settings.ExportMode, baseName, publicName, privateName);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new CertificateExportProgress(10, "Carregando certificado..."));

            // Required flags: exportable key material in-memory only.
            var flags = X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet;

            X509Certificate2 cert;
            try
            {
                // .cer/.crt typically has no private key and no password.
                cert = (ext is ".cer" or ".crt")
                    ? new X509Certificate2(inputPath)
                    : new X509Certificate2(inputPath, settings.Password ?? string.Empty, flags);
            }
            catch (CryptographicException ex)
            {
                if (ext is ".pfx" or ".p12")
                {
                    if (IsWrongPasswordUsingBouncyCastle(inputPath, settings.Password ?? string.Empty))
                        throw new InvalidPasswordException("Senha inválida.", ex);
                }
                throw new CertificateExportException("Não foi possível abrir o certificado. Verifique o arquivo e a senha.", ex);
            }

            using (cert)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new CertificateExportProgress(25, "Exportando certificado público (PEM)..."));

                var certPem = cert.ExportCertificatePem();

                string? privateKeyPem = null;
                var requiresPrivateKey = settings.ExportMode is ExportPemMode.PrivateKeyOnly
                    or ExportPemMode.SeparatePublicAndPrivatePem
                    or ExportPemMode.CombinedPemSingleFile;

                if (requiresPrivateKey)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report(new CertificateExportProgress(45, "Exportando chave privada (PEM)..."));

                    try
                    {
                        // First attempt: Native .NET export (works if key is marked as exportable in Windows store/container)
                        if (cert.HasPrivateKey)
                        {
                            privateKeyPem = cert.GetRSAPrivateKey()?.ExportPkcs8PrivateKeyPem()
                                            ?? cert.GetECDsaPrivateKey()?.ExportPkcs8PrivateKeyPem();
                        }
                    }
                    catch (CryptographicException)
                    {
                        // Fallback: Native export failed, likely due to Windows exportability restriction.
                        // We will try to read the PFX directly using BouncyCastle.
                    }

                    // Second attempt: Manual PFX parsing using BouncyCastle if native export failed or returned null
                    if (string.IsNullOrWhiteSpace(privateKeyPem))
                    {
                        if (ext is ".pfx" or ".p12")
                        {
                            try
                            {
                                privateKeyPem = ExportPrivateKeyFromPfxUsingBouncyCastle(inputPath, settings.Password ?? string.Empty);
                            }
                            catch (Exception ex)
                            {
                                throw new CertificateExportException(
                                    "Falha ao exportar a chave privada. O certificado pode estar protegido contra exportação ou o formato é incompatível.",
                                    ex);
                            }
                        }
                        else
                        {
                            throw new CertificateExportException("Este certificado não possui chave privada exportável.");
                        }
                    }

                    if (string.IsNullOrWhiteSpace(privateKeyPem))
                        throw new CertificateExportException("Não foi possível extrair a chave privada deste certificado.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new CertificateExportProgress(70, "Gravando arquivos PEM..."));

                Directory.CreateDirectory(outputFolder);

                switch (settings.ExportMode)
                {
                    case ExportPemMode.PublicCertificateOnly:
                    {
                        var outFile = string.IsNullOrWhiteSpace(publicName) ? $"{baseName}.cert.pem" : publicName;
                        var outPath = Path.Combine(outputFolder, outFile);
                        await WriteTextUtf8Async(outPath, certPem, cancellationToken).ConfigureAwait(false);
                        break;
                    }
                    case ExportPemMode.PrivateKeyOnly:
                    {
                        var outFile = string.IsNullOrWhiteSpace(privateName) ? $"{baseName}.key.pem" : privateName;
                        var outPath = Path.Combine(outputFolder, outFile);
                        await WriteTextUtf8Async(outPath, privateKeyPem!, cancellationToken).ConfigureAwait(false);
                        break;
                    }
                    case ExportPemMode.SeparatePublicAndPrivatePem:
                    {
                        var certFile = string.IsNullOrWhiteSpace(publicName) ? $"{baseName}.cert.pem" : publicName;
                        var keyFile = string.IsNullOrWhiteSpace(privateName) ? $"{baseName}.key.pem" : privateName;

                        var certPath = Path.Combine(outputFolder, certFile);
                        var keyPath = Path.Combine(outputFolder, keyFile);
                        await WriteTextUtf8Async(certPath, certPem, cancellationToken).ConfigureAwait(false);
                        await WriteTextUtf8Async(keyPath, privateKeyPem!, cancellationToken).ConfigureAwait(false);
                        break;
                    }
                    case ExportPemMode.CombinedPemSingleFile:
                    {
                        var outPath = Path.Combine(outputFolder, $"{baseName}.pem");
                        var combined = certPem.TrimEnd() + Environment.NewLine + Environment.NewLine + privateKeyPem!.TrimEnd() + Environment.NewLine;
                        await WriteTextUtf8Async(outPath, combined, cancellationToken).ConfigureAwait(false);
                        break;
                    }
                    default:
                        throw new CertificateExportException("Modo de exportação não suportado.");
                }

                progress?.Report(new CertificateExportProgress(100, "Exportação concluída."));
            }
        }

        /// <summary>
        /// Fallback method to export private key using BouncyCastle to read the PFX file directly.
        /// This bypasses Windows-specific exportability flags that often block X509Certificate2.Export.
        /// BouncyCastle is extremely robust at handling PKCS#12 containers regardless of Windows restrictions.
        /// </summary>
        private string ExportPrivateKeyFromPfxUsingBouncyCastle(string pfxPath, string password)
        {
            try
            {
                // Read the PFX file as bytes
                byte[] pfxBytes = File.ReadAllBytes(pfxPath);

                // Load the PKCS#12 store using BouncyCastle
                Pkcs12Store store = new Pkcs12StoreBuilder().Build();
                using (var stream = new MemoryStream(pfxBytes))
                {
                    store.Load(stream, password.ToCharArray());
                }

                // Iterate through all aliases in the store to find the private key
                foreach (string alias in store.Aliases)
                {
                    // Check if this alias has an associated private key
                    if (store.IsKeyEntry(alias))
                    {
                        // Get the private key entry
                        AsymmetricKeyEntry keyEntry = store.GetKey(alias);
                        if (keyEntry != null)
                        {
                            AsymmetricKeyParameter privateKey = keyEntry.Key;

                            // Export the private key to PEM format using BouncyCastle's PEM writer
                            using (var writer = new StringWriter())
                            {
                                var pemWriter = new PemWriter(writer);
                                pemWriter.WriteObject(privateKey);
                                pemWriter.Writer.Flush();
                                return writer.ToString();
                            }
                        }
                    }
                }

                throw new CertificateExportException("Nenhuma chave privada encontrada no arquivo PKCS#12.");
            }
            catch (CertificateExportException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new CertificateExportException($"Erro ao processar o arquivo PKCS#12: {ex.Message}", ex);
            }
        }

        private static bool IsWrongPasswordUsingBouncyCastle(string pfxPath, string password)
        {
            try
            {
                byte[] pfxBytes = File.ReadAllBytes(pfxPath);
                var store = new Pkcs12StoreBuilder().Build();
                using (var stream = new MemoryStream(pfxBytes))
                {
                    store.Load(stream, (password ?? "").ToCharArray());
                }
                return false;
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase) || 
                    ex.Message.Contains("mac invalid", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                return false;
            }
        }

        private static void ValidateModeForExtension(ExportPemMode mode, string extension)
        {
            if (extension is ".pfx" or ".p12")
                return;

            if (extension is ".cer" or ".crt")
            {
                if (mode != ExportPemMode.PublicCertificateOnly)
                    throw new CertificateExportException("Arquivos .cer/.crt permitem apenas exportar o certificado público.");
                return;
            }

            throw new CertificateExportException("Formato de certificado não suportado. Use .pfx, .p12, .cer ou .crt.");
        }

        private static void ValidateOutputFileNames(ExportPemMode mode, string baseName, string publicName, string privateName)
        {
            if (string.IsNullOrWhiteSpace(baseName))
                throw new CertificateExportException("Informe um nome base para os arquivos de saída.");

            static void ValidateSingleFileName(string value, string expectedSuffix, string label)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;

                if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new CertificateExportException($"Nome inválido para {label}.");

                if (value.Contains(Path.DirectorySeparatorChar) || value.Contains(Path.AltDirectorySeparatorChar))
                    throw new CertificateExportException($"O {label} deve ser apenas o nome do arquivo (sem pastas).");

                if (!value.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase))
                    throw new CertificateExportException($"O {label} deve terminar com \"{expectedSuffix}\".");
            }

            if (mode == ExportPemMode.PublicCertificateOnly)
                ValidateSingleFileName(publicName, ".cert.pem", "arquivo público");

            if (mode == ExportPemMode.PrivateKeyOnly)
                ValidateSingleFileName(privateName, ".key.pem", "arquivo privado");

            if (mode == ExportPemMode.SeparatePublicAndPrivatePem)
            {
                ValidateSingleFileName(publicName, ".cert.pem", "arquivo público");
                ValidateSingleFileName(privateName, ".key.pem", "arquivo privado");

                var effectivePublic = string.IsNullOrWhiteSpace(publicName) ? $"{baseName}.cert.pem" : publicName;
                var effectivePrivate = string.IsNullOrWhiteSpace(privateName) ? $"{baseName}.key.pem" : privateName;

                if (effectivePublic.Equals(effectivePrivate, StringComparison.OrdinalIgnoreCase))
                    throw new CertificateExportException("Os nomes dos arquivos público e privado devem ser diferentes.");
            }
        }

        private static async Task WriteTextUtf8Async(string path, string content, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(path, content, Utf8NoBom, cancellationToken).ConfigureAwait(false);
        }

        private static string SanitizeBaseFileName(string baseName)
        {
            if (string.IsNullOrWhiteSpace(baseName))
                return string.Empty;

            foreach (var c in Path.GetInvalidFileNameChars())
                baseName = baseName.Replace(c, '_');

            return baseName.Trim();
        }

        private sealed class CertificateExportException : Exception
        {
            public CertificateExportException(string message, Exception? inner = null) : base(message, inner) { }
        }
    }
}
