using System;

namespace DevSAK.Models;

public sealed record AppUpdate(Version Version, string Notes, Uri DownloadUri, long Size, string Sha256);
