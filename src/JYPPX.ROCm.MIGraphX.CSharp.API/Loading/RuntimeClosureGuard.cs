using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;

namespace JYPPX.ROCm.MIGraphXSharp.Loading;

internal sealed class RuntimeClosureValidation
{
    internal RuntimeClosureValidation(bool isPackageCandidate, bool success, string? identity, string detail)
    {
        IsPackageCandidate = isPackageCandidate;
        Success = success;
        Identity = identity;
        Detail = detail;
    }

    internal bool IsPackageCandidate { get; }

    internal bool Success { get; }

    internal string? Identity { get; }

    internal string Detail { get; }
}

internal static class RuntimeClosureGuard
{
    internal const string MarkerFileName = "migraphx-runtime-closure.xml";
    internal const string RuntimePackageId = "JYPPX.ROCm.MIGraphX.CSharp.API.Runtime.linux-x64";
    internal const string RuntimePackageVersion = "7.2.1";
    internal const string RuntimeFamily = "ROCm-7.2.1-linux-x64";

    internal static RuntimeClosureValidation ValidateCandidate(string candidatePath, bool requirePackageMarker)
    {
        var fullCandidate = Path.GetFullPath(candidatePath);
        var markerPath = FindMarker(fullCandidate);
        if (markerPath is null)
        {
            return requirePackageMarker
                ? Failure(true, $"Package-layout candidate is missing {MarkerFileName}.")
                : new RuntimeClosureValidation(false, true, null, "No Runtime package marker is present; use the existing explicit/system-native path.");
        }

        try
        {
            var markerInfo = new FileInfo(markerPath);
            if (markerInfo.Length <= 0 || markerInfo.Length > 1024 * 1024)
            {
                throw new InvalidDataException("Runtime closure marker size is outside the accepted range.");
            }

            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            };
            XDocument document;
            using (var stream = File.OpenRead(markerPath))
            using (var reader = XmlReader.Create(stream, settings))
            {
                document = XDocument.Load(reader, LoadOptions.None);
            }

            var root = document.Root ?? throw new InvalidDataException("Runtime closure marker has no root element.");
            if (root.Name != "runtimeClosure" || Attribute(root, "schemaVersion") != "1")
            {
                throw new InvalidDataException("Runtime closure marker schema is not supported.");
            }
            if (Attribute(root, "packageId") != RuntimePackageId ||
                Attribute(root, "packageVersion") != RuntimePackageVersion ||
                Attribute(root, "rid") != "linux-x64" ||
                Attribute(root, "family") != RuntimeFamily)
            {
                throw new InvalidDataException("Runtime closure package identity, RID, or ROCm family does not match the M7 lock.");
            }

            var manifestDigest = Attribute(root, "manifestContentDigestSha256");
            AssertHash(manifestDigest, "manifest content digest");
            var files = root.Elements("file").ToArray();
            if (files.Length == 0 || files.Length > 512)
            {
                throw new InvalidDataException("Runtime closure marker must contain between 1 and 512 files.");
            }

            var markerDirectory = Path.GetDirectoryName(markerPath)!;
            var paths = new HashSet<string>(PathComparer);
            var candidateDeclared = false;
            string? rootHash = null;
            foreach (var file in files)
            {
                var relativePath = NormalizeRelativePath(Attribute(file, "path"));
                var expectedHash = Attribute(file, "sha256");
                var soname = Attribute(file, "soname");
                AssertHash(expectedHash, $"hash for {relativePath}");
                if (string.IsNullOrWhiteSpace(soname))
                {
                    throw new InvalidDataException($"Runtime closure file has no SONAME identity: {relativePath}");
                }
                if (!paths.Add(relativePath))
                {
                    throw new InvalidDataException($"Runtime closure marker contains a duplicate path: {relativePath}");
                }

                var fullPath = Path.GetFullPath(Path.Combine(markerDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
                AssertUnderDirectory(fullPath, markerDirectory);
                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException($"Runtime closure file is missing: {relativePath}", fullPath);
                }
                if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException($"Runtime closure file cannot be a symbolic link or reparse point: {relativePath}");
                }

                var actualHash = Sha256(fullPath);
                if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"Runtime closure file hash changed: {relativePath}; expected {expectedHash}, actual {actualHash}.");
                }
                if (string.Equals(fullPath, fullCandidate, PathComparison))
                {
                    candidateDeclared = true;
                    rootHash = actualHash;
                    if (soname != "libmigraphx_c.so.3")
                    {
                        throw new InvalidDataException("The selected package root does not declare SONAME libmigraphx_c.so.3.");
                    }
                }
            }

            if (!candidateDeclared)
            {
                throw new InvalidDataException("The selected MIGraphX library is not declared by its Runtime closure marker.");
            }

            var identity = $"{RuntimeFamily}:{RuntimePackageVersion}:{manifestDigest}";
            return new RuntimeClosureValidation(
                true,
                true,
                identity,
                $"Verified package-local closure {identity}; root SONAME libmigraphx_c.so.3; root SHA-256 {rootHash}; {files.Length} allowlisted files.");
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is XmlException || exception is InvalidDataException)
        {
            return Failure(true, $"Runtime package closure rejected: {exception.Message}");
        }
    }

    internal static bool IsReservedPackageCandidate(string candidatePath)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(candidatePath))!);
        return string.Equals(directory.Name, "lib", StringComparison.OrdinalIgnoreCase) &&
            directory.Parent is not null &&
            string.Equals(directory.Parent.Name, "native", StringComparison.OrdinalIgnoreCase) &&
            directory.Parent.Parent is not null &&
            string.Equals(directory.Parent.Parent.Name, "linux-x64", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool ReservedPackageDirectoryExists(string candidatePath)
    {
        return IsReservedPackageCandidate(candidatePath) && Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(candidatePath)));
    }

    private static string? FindMarker(string fullCandidate)
    {
        var directory = Path.GetDirectoryName(fullCandidate);
        if (directory is null)
        {
            return null;
        }

        var adjacent = Path.Combine(directory, MarkerFileName);
        if (File.Exists(adjacent))
        {
            return adjacent;
        }

        var parent = Directory.GetParent(directory);
        if (parent is null)
        {
            return null;
        }
        var parentMarker = Path.Combine(parent.FullName, MarkerFileName);
        return File.Exists(parentMarker) ? parentMarker : null;
    }

    private static string Attribute(XElement element, string name)
    {
        return element.Attribute(name)?.Value ?? throw new InvalidDataException($"Runtime closure marker is missing '{name}'.");
    }

    private static string NormalizeRelativePath(string path)
    {
        var normalized = path.Replace('\\', '/');
        var segments = normalized.Split('/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.IndexOf(':') >= 0 || segments.Any(segment => segment.Length == 0 || segment == "." || segment == ".."))
        {
            throw new InvalidDataException($"Runtime closure path must be relative and traversal-free: {path}");
        }
        return normalized;
    }

    private static void AssertUnderDirectory(string path, string directory)
    {
        var prefix = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, PathComparison))
        {
            throw new InvalidDataException($"Runtime closure path escapes its native directory: {path}");
        }
    }

    private static void AssertHash(string value, string name)
    {
        if (value.Length != 64 || value.Any(character => !((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'))))
        {
            throw new InvalidDataException($"Runtime closure {name} is not a lowercase SHA-256 value.");
        }
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static RuntimeClosureValidation Failure(bool isPackageCandidate, string detail) =>
        new RuntimeClosureValidation(isPackageCandidate, false, null, detail);

    private static StringComparison PathComparison => IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static IEqualityComparer<string> PathComparer => IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;
}
