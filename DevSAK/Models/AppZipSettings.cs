using System;
using System.Collections.Generic;

namespace DevSAK.Models
{
    /// <summary>
    /// Model for AppZip tool settings and configuration.
    /// </summary>
    public class AppZipSettings
    {
        /// <summary>
        /// Source directory path to compress.
        /// </summary>
        public string SourceDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Destination directory for the ZIP file.
        /// </summary>
        public string DestinationDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Custom ZIP file name (without .zip extension).
        /// </summary>
        public string ZipName { get; set; } = string.Empty;

        /// <summary>
        /// If true, ZIP name is automatically generated with timestamp.
        /// </summary>
        public bool UseAutomaticZipName { get; set; } = true;

        /// <summary>
        /// Start date filter for files (if applicable).
        /// </summary>
        public DateTime? StartDate { get; set; }

        /// <summary>
        /// If true, date filter is enabled.
        /// </summary>
        public bool UseDateFilter { get; set; } = false;

        /// <summary>
        /// Semicolon-separated list of file patterns to ignore (e.g., "web.config;appsettings.json").
        /// </summary>
        public string IgnoredFiles { get; set; } = string.Empty;

        /// <summary>
        /// Semicolon-separated list of folder patterns to ignore (e.g., "PrivateTempStorage;PublicTempStorage;logs;temp").
        /// </summary>
        public string IgnoredFolders { get; set; } = string.Empty;

        /// <summary>
        /// Semicolon-separated list of file extensions to ignore (e.g., ".cs;.pdb;.log;.tmp").
        /// </summary>
        public string IgnoredExtensions { get; set; } = string.Empty;

        /// <summary>
        /// Last 5 used source directories (for quick access).
        /// </summary>
        public List<string> RecentSourceDirectories { get; set; } = new();

        /// <summary>
        /// Last 5 used destination directories (for quick access).
        /// </summary>
        public List<string> RecentDestinationDirectories { get; set; } = new();

        /// <summary>
        /// Adds a directory to the recent source directories list (max 5 items).
        /// </summary>
        public void AddRecentSourceDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            // Remove if already exists to move it to the top
            RecentSourceDirectories.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));

            // Add to the beginning
            RecentSourceDirectories.Insert(0, path);

            // Keep only the last 5
            if (RecentSourceDirectories.Count > 5)
                RecentSourceDirectories.RemoveAt(RecentSourceDirectories.Count - 1);
        }

        /// <summary>
        /// Adds a directory to the recent destination directories list (max 5 items).
        /// </summary>
        public void AddRecentDestinationDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            // Remove if already exists to move it to the top
            RecentDestinationDirectories.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));

            // Add to the beginning
            RecentDestinationDirectories.Insert(0, path);

            // Keep only the last 5
            if (RecentDestinationDirectories.Count > 5)
                RecentDestinationDirectories.RemoveAt(RecentDestinationDirectories.Count - 1);
        }
    }
}
