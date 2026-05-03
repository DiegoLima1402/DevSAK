using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using DevSAK.Models;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Asn1.X509;

namespace DevSAK.Services
{
    public sealed class CertificateValidationService
    {
        public async Task<CertificateValidationResult> ValidateAsync(
            string filePath, 
            string password, 
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new Exception("Arquivo de certificado não encontrado.");

            var ext = Path.GetExtension(filePath)?.Trim().ToLowerInvariant() ?? string.Empty;
            
            cancellationToken.ThrowIfCancellationRequested();

            X509Certificate2? cert = null;
            var result = new CertificateValidationResult
            {
                FileName = Path.GetFileName(filePath)
            };

            try
            {
                var flags = X509KeyStorageFlags.EphemeralKeySet;
                
                if (ext is ".cer" or ".crt")
                {
                    cert = new X509Certificate2(filePath);
                }
                else if (ext is ".pfx" or ".p12")
                {
                    cert = new X509Certificate2(filePath, password ?? string.Empty, flags);
                }
                else
                {
                    throw new Exception("Formato não suportado. Use .pfx, .p12, .cer ou .crt.");
                }
                
                PopulateResultFromCert(cert, result);
            }
            catch (CryptographicException)
            {
                // Fallback to BouncyCastle
                if (ext is ".pfx" or ".p12")
                {
                    if (IsWrongPasswordUsingBouncyCastle(filePath, password))
                        throw new InvalidPasswordException("Senha inválida.");

                    cert = FallbackToBouncyCastle(filePath, password, result);
                    if (cert == null && string.IsNullOrEmpty(result.Subject))
                    {
                        throw new Exception("A senha está incorreta ou o arquivo está corrompido.");
                    }
                }
                else
                {
                    throw new Exception("O arquivo está corrompido ou o formato é inválido.");
                }
            }
            finally
            {
                cert?.Dispose();
            }
            
            return await Task.FromResult(result);
        }
        
        private void PopulateResultFromCert(X509Certificate2 cert, CertificateValidationResult result)
        {
            result.Subject = cert.Subject;
            result.Issuer = cert.Issuer;
            result.NotBefore = cert.NotBefore;
            result.NotAfter = cert.NotAfter;
            
            var remaining = cert.NotAfter - DateTime.Now;
            result.DaysRemaining = remaining.Days < 0 ? 0 : remaining.Days;
            result.IsExpired = DateTime.Now > cert.NotAfter;
            
            result.Thumbprint = cert.Thumbprint;
            result.SerialNumber = cert.SerialNumber;
            result.SignatureAlgorithm = cert.SignatureAlgorithm.FriendlyName ?? cert.SignatureAlgorithm.Value ?? "Desconhecido";
            result.HasPrivateKey = cert.HasPrivateKey;
            
            var keyAlg = cert.PublicKey.Oid.FriendlyName ?? cert.PublicKey.Oid.Value ?? "Desconhecido";
            result.KeyAlgorithm = keyAlg;
            
            try
            {
                using var rsa = cert.GetRSAPublicKey();
                if (rsa != null) 
                {
                    result.KeySize = rsa.KeySize;
                    result.KeyAlgorithm = "RSA";
                }
            } catch { }

            try
            {
                using var ecdsa = cert.GetECDsaPublicKey();
                if (ecdsa != null) 
                {
                    result.KeySize = ecdsa.KeySize;
                    result.KeyAlgorithm = "ECDSA";
                }
            } catch { }
            
            result.FriendlyName = cert.FriendlyName;
            
            // Extract SANs
            foreach (var ext in cert.Extensions)
            {
                if (ext.Oid?.Value == "2.5.29.17") // Subject Alternative Name
                {
                    try
                    {
                        var asnData = new AsnEncodedData(ext.Oid, ext.RawData);
                        var formatted = asnData.Format(true);
                        var sans = formatted.Split(new[] { Environment.NewLine, ", " }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var san in sans)
                        {
                            var cleaned = san.Replace("DNS Name=", "").Replace("DNS Name:", "").Trim();
                            if (!string.IsNullOrEmpty(cleaned) && !result.SubjectAlternativeNames.Contains(cleaned))
                            {
                                result.SubjectAlternativeNames.Add(cleaned);
                            }
                        }
                    } catch { }
                }
            }
        }
        
        private X509Certificate2? FallbackToBouncyCastle(string pfxPath, string password, CertificateValidationResult result)
        {
            try
            {
                byte[] pfxBytes = File.ReadAllBytes(pfxPath);
                var store = new Pkcs12Store();
                
                using (var stream = new MemoryStream(pfxBytes))
                {
                    store.Load(stream, (password ?? "").ToCharArray());
                }
                
                foreach (string alias in store.Aliases)
                {
                    if (store.IsKeyEntry(alias))
                    {
                        result.HasPrivateKey = true;
                        result.FriendlyName = alias;
                    }
                    
                    if (store.IsCertificateEntry(alias))
                    {
                        var certEntry = store.GetCertificate(alias);
                        var bcCert = certEntry.Certificate;
                        
                        result.Subject = bcCert.SubjectDN.ToString();
                        result.Issuer = bcCert.IssuerDN.ToString();
                        result.NotBefore = bcCert.NotBefore;
                        result.NotAfter = bcCert.NotAfter;
                        
                        var remaining = result.NotAfter - DateTime.Now;
                        result.DaysRemaining = remaining.Days < 0 ? 0 : remaining.Days;
                        result.IsExpired = DateTime.Now > result.NotAfter;
                        
                        result.SerialNumber = bcCert.SerialNumber.ToString(16).ToUpper();
                        result.SignatureAlgorithm = bcCert.SigAlgName;
                        
                        var pubKey = bcCert.GetPublicKey();
                        if (pubKey is Org.BouncyCastle.Crypto.Parameters.RsaKeyParameters rsaKey)
                        {
                            result.KeyAlgorithm = "RSA";
                            result.KeySize = rsaKey.Modulus.BitLength;
                        }
                        else if (pubKey is Org.BouncyCastle.Crypto.Parameters.ECPublicKeyParameters ecKey)
                        {
                            result.KeyAlgorithm = "ECDSA";
                            result.KeySize = ecKey.Parameters.Curve.FieldSize;
                        }
                        
                        // SAN
                        var sanExt = bcCert.GetExtensionValue(new Org.BouncyCastle.Asn1.DerObjectIdentifier("2.5.29.17"));
                        if (sanExt != null)
                        {
                            try
                            {
                                var dIn = new Org.BouncyCastle.Asn1.Asn1InputStream(sanExt.GetOctets());
                                var sanObj = dIn.ReadObject();
                                if (sanObj is Org.BouncyCastle.Asn1.Asn1Sequence seq)
                                {
                                    var genNames = GeneralNames.GetInstance(seq);
                                    foreach (var name in genNames.GetNames())
                                    {
                                        if (name.TagNo == GeneralName.DnsName) // DNS Name
                                        {
                                            result.SubjectAlternativeNames.Add(name.Name.ToString()!);
                                        }
                                    }
                                }
                            } catch { }
                        }
                        
                        try
                        {
                            var dotNetCert = new X509Certificate2(bcCert.GetEncoded());
                            result.Thumbprint = dotNetCert.Thumbprint;
                            return dotNetCert; 
                        }
                        catch
                        {
                            // ignore thumbprint error if it somehow fails
                        }
                    }
                }
                
                return null;
            }
            catch (Exception)
            {
                // Password incorrect or corrupted
                return null;
            }
        }

        private static bool IsWrongPasswordUsingBouncyCastle(string pfxPath, string password)
        {
            try
            {
                byte[] pfxBytes = File.ReadAllBytes(pfxPath);
                var store = new Pkcs12Store();
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
    }
}
