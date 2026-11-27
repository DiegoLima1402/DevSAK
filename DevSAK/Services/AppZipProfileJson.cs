using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DevSAK.Models;

namespace DevSAK.Services
{
    /// <summary>Portable format: one profile per file, without local paths or internal IDs.</summary>
    public static class AppZipProfileJson
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private sealed class ProfileDocument
        {
            public int Version { get; set; } = 1;
            public string Name { get; set; } = string.Empty;
            public List<string> IgnoredFiles { get; set; } = new();
            public List<string> IgnoredFolders { get; set; } = new();
            public List<string> IgnoredExtensions { get; set; } = new();
        }

        public static AppZipIgnoreProfile Deserialize(string json)
        {
            var document = JsonSerializer.Deserialize<ProfileDocument>(json, Options)
                ?? throw new InvalidDataException("O JSON deve conter um perfil.");
            if (document.Version != 1)
                throw new InvalidDataException("Versão de perfil não suportada.");
            if (string.IsNullOrWhiteSpace(document.Name))
                throw new InvalidDataException("Informe o nome do perfil no campo Name.");

            return new AppZipIgnoreProfile
            {
                Name = document.Name.Trim(),
                IgnoredFiles = Normalize(document.IgnoredFiles),
                IgnoredFolders = Normalize(document.IgnoredFolders),
                IgnoredExtensions = Normalize(document.IgnoredExtensions, extensions: true)
            };
        }

        public static string Serialize(AppZipIgnoreProfile profile) => JsonSerializer.Serialize(new ProfileDocument
        {
            Name = profile.Name,
            IgnoredFiles = profile.IgnoredFiles,
            IgnoredFolders = profile.IgnoredFolders,
            IgnoredExtensions = profile.IgnoredExtensions
        }, Options);

        private static List<string> Normalize(List<string>? rules, bool extensions = false)
        {
            if (rules == null || rules.Any(rule => string.IsNullOrWhiteSpace(rule)))
                throw new InvalidDataException("As regras devem ser listas de textos não vazios, com uma regra por item.");

            return rules.SelectMany(rule => rule.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(rule => extensions && !rule.StartsWith('.') ? "." + rule : rule)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
