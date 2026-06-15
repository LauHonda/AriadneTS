using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AriadneTS.Runtime
{
    public sealed class ScriptPackageReader
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("ARDPKG01");
        private static readonly byte[] LegacyMagic = Encoding.ASCII.GetBytes("KZNPKG01");
        private static readonly byte[] OriginalLegacyMagic = Encoding.ASCII.GetBytes("TSRPKG01");
        private const uint FormatVersion = 1;
        private const int MaxManifestBytes = 1024 * 1024;
        private const int MaxSignatureBytes = 16 * 1024;
        private const int MaxFiles = 100000;
        private const int MaxPathBytes = 4096;

        private readonly ScriptPackageSignatureVerifier signatureVerifier;
        private readonly Func<byte[], ScriptPackageManifest> deserializeManifest;

        public ScriptPackageReader(
            string signingPublicKeyBase64,
            Func<byte[], ScriptPackageManifest> deserializeManifest)
            : this(
                new ScriptPackageSignatureVerifier(signingPublicKeyBase64),
                deserializeManifest)
        {
        }

        public ScriptPackageReader(
            ScriptPackageSignatureVerifier signatureVerifier,
            Func<byte[], ScriptPackageManifest> deserializeManifest)
        {
            this.signatureVerifier = signatureVerifier ??
                throw new ArgumentNullException(nameof(signatureVerifier));
            this.deserializeManifest = deserializeManifest ??
                throw new ArgumentNullException(nameof(deserializeManifest));
        }

        public ScriptPackage Read(byte[] packageBytes)
        {
            if (packageBytes == null)
            {
                throw new ArgumentNullException(nameof(packageBytes));
            }

            using var stream = new MemoryStream(packageBytes, false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, true);
            RequireMagic(reader);
            Require(reader.ReadUInt32() == FormatVersion, "Unsupported script package format version.");

            var manifestLength = ReadBoundedLength(reader, MaxManifestBytes, "manifest");
            var signatureLength = ReadBoundedLength(reader, MaxSignatureBytes, "signature");
            var fileCount = ReadBoundedLength(reader, MaxFiles, "file count");
            var manifestBytes = ReadExact(reader, manifestLength, "manifest");
            var signatureBytes = ReadExact(reader, signatureLength, "signature");

            signatureVerifier.Verify(manifestBytes, signatureBytes);
            var manifest = deserializeManifest(manifestBytes) ??
                throw new InvalidDataException("Script package manifest could not be deserialized.");
            ValidateManifest(manifest);
            Require(fileCount == manifest.Files.Length, "Script package file count does not match manifest.");

            var expectedFiles = new Dictionary<string, ScriptPackageFile>(StringComparer.Ordinal);
            foreach (var file in manifest.Files)
            {
                expectedFiles.Add(NormalizeAndValidatePath(file.Path), file);
            }

            var modules = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < fileCount; ++index)
            {
                var pathLength = ReadBoundedLength(reader, MaxPathBytes, "file path");
                var dataLength = ReadUInt64Length(reader, "file data");
                var path = NormalizeAndValidatePath(
                    Encoding.UTF8.GetString(ReadExact(reader, pathLength, "file path")));

                Require(expectedFiles.Remove(path, out var expected), $"Unexpected or duplicate package file: {path}");
                Require(dataLength == expected.SizeBytes, $"Script package file size mismatch: {path}");

                var data = ReadExact(reader, dataLength, $"file '{path}'");
                Require(
                    string.Equals(ComputeSha256(data), expected.Sha256, StringComparison.OrdinalIgnoreCase),
                    $"Script package hash mismatch: {path}");
                modules.Add(path, Encoding.UTF8.GetString(data));
            }

            Require(expectedFiles.Count == 0, "Script package is missing manifest files.");
            Require(stream.Position == stream.Length, "Script package contains trailing data.");
            return new ScriptPackage(manifest, modules);
        }

        public static void ValidateManifest(ScriptPackageManifest manifest)
        {
            if (manifest == null)
            {
                throw new InvalidDataException("Script package manifest is missing.");
            }
            if (string.IsNullOrWhiteSpace(manifest.Version) || manifest.BuildNumber < 0)
            {
                throw new InvalidDataException("Script package version or build number is invalid.");
            }
            if (manifest.RequiredRuntimeAbiVersion != ScriptRuntime.RequiredAbiVersion)
            {
                throw new InvalidDataException(
                    $"Script package requires runtime ABI {manifest.RequiredRuntimeAbiVersion}, " +
                    $"but this client provides ABI {ScriptRuntime.RequiredAbiVersion}.");
            }
            if (string.IsNullOrWhiteSpace(manifest.EntryModule) ||
                manifest.Files == null ||
                manifest.Files.Length == 0)
            {
                throw new InvalidDataException("Script package entry module or files are missing.");
            }

            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in manifest.Files)
            {
                if (file == null || file.SizeBytes < 0 || string.IsNullOrWhiteSpace(file.Sha256))
                {
                    throw new InvalidDataException("Script package contains an invalid file entry.");
                }
                if (!paths.Add(NormalizeAndValidatePath(file.Path)))
                {
                    throw new InvalidDataException($"Duplicate script package file: {file.Path}");
                }
            }
            if (!paths.Contains(NormalizeAndValidatePath(manifest.EntryModule)))
            {
                throw new InvalidDataException("Script package entry module is not listed in files.");
            }
        }

        private static string NormalizeAndValidatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidDataException("Script package file path is required.");
            }

            var normalized = ScriptPackage.NormalizePath(path);
            if (normalized.StartsWith("/", StringComparison.Ordinal) ||
                normalized == "." ||
                normalized == ".." ||
                normalized.StartsWith("../", StringComparison.Ordinal) ||
                normalized.Contains("/../") ||
                normalized.EndsWith("/..", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Invalid script package file path: {path}");
            }
            return normalized;
        }

        private static int ReadBoundedLength(BinaryReader reader, int maximum, string field)
        {
            var value = reader.ReadUInt32();
            Require(value <= maximum, $"Script package {field} exceeds its limit.");
            return checked((int)value);
        }

        private static int ReadUInt64Length(BinaryReader reader, string field)
        {
            var value = reader.ReadUInt64();
            Require(value <= int.MaxValue, $"Script package {field} exceeds its limit.");
            return checked((int)value);
        }

        private static byte[] ReadExact(BinaryReader reader, int length, string field)
        {
            var bytes = reader.ReadBytes(length);
            Require(bytes.Length == length, $"Script package {field} is truncated.");
            return bytes;
        }

        private static void RequireMagic(BinaryReader reader)
        {
            var actual = reader.ReadBytes(Magic.Length);
            if (MatchesMagic(actual, LegacyMagic) ||
                MatchesMagic(actual, OriginalLegacyMagic))
            {
                throw new InvalidDataException(
                    "This package uses a legacy format. Rebuild it with the AriadneTS package tool.");
            }

            Require(
                MatchesMagic(actual, Magic),
                "Invalid script package magic.");
        }

        private static bool MatchesMagic(byte[] actual, byte[] expected)
        {
            return actual.Length == expected.Length &&
                CryptographicOperations.FixedTimeEquals(actual, expected);
        }

        private static string ComputeSha256(byte[] data)
        {
            using var sha256 = SHA256.Create();
            return BitConverter.ToString(sha256.ComputeHash(data))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidDataException(message);
            }
        }
    }
}
