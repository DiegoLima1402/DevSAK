using System;
using System.Collections.Generic;

namespace DevSAK.Models
{
    public sealed class CertificateExportSettings
    {
        public string InputCertificatePath { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string OutputFolder { get; set; } = string.Empty;
        public string OutputBaseFileName { get; set; } = "certificado";
        public string PublicPemFileName { get; set; } = string.Empty;  // e.g. <base>.cert.pem
        public string PrivatePemFileName { get; set; } = string.Empty; // e.g. <base>.key.pem
        public ExportPemMode ExportMode { get; set; } = ExportPemMode.CombinedPemSingleFile;

        public bool AutoScrollEnabled { get; set; } = true;

        public List<string> RecentInputFiles { get; set; } = new();
        public List<string> RecentOutputFolders { get; set; } = new();

        public void AddRecentInputFile(string path, int maxItems = 10)
        {
            AddRecentItem(RecentInputFiles, path, maxItems);
        }

        public void AddRecentOutputFolder(string path, int maxItems = 10)
        {
            AddRecentItem(RecentOutputFolders, path, maxItems);
        }

        private static void AddRecentItem(List<string> list, string value, int maxItems)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            list.RemoveAll(p => p.Equals(value, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, value);

            if (list.Count > maxItems && maxItems > 0)
                list.RemoveRange(maxItems, list.Count - maxItems);
        }
    }
}

