using System;
using System.Security.Cryptography;

namespace AriadneTS.Runtime
{
    public sealed class ScriptPackageSignatureVerifier
    {
        private const string PublicKeyPrefix = "RSA1.";

        private readonly RSAParameters publicKey;

        public ScriptPackageSignatureVerifier(string publicKeyBase64)
        {
            if (string.IsNullOrWhiteSpace(publicKeyBase64))
            {
                throw new ArgumentException("A package signing public key is required.", nameof(publicKeyBase64));
            }

            var parts = publicKeyBase64.Trim().Split('.');
            if (parts.Length != 3 || parts[0] != PublicKeyPrefix.TrimEnd('.'))
            {
                throw new FormatException(
                    "Package signing public key must use RSA1.<modulus>.<exponent> format.");
            }

            publicKey = new RSAParameters
            {
                Modulus = DecodeBase64Url(parts[1]),
                Exponent = DecodeBase64Url(parts[2]),
            };
        }

        public void Verify(byte[] manifestBytes, byte[] signatureBytes)
        {
            if (manifestBytes == null)
            {
                throw new ArgumentNullException(nameof(manifestBytes));
            }
            if (signatureBytes == null)
            {
                throw new ArgumentNullException(nameof(signatureBytes));
            }

            using var rsa = RSA.Create();
            rsa.ImportParameters(publicKey);
            using var sha256 = SHA256.Create();
            var manifestHash = sha256.ComputeHash(manifestBytes);
            if (!rsa.VerifyHash(
                    manifestHash,
                    signatureBytes,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1))
            {
                throw new CryptographicException("Script package manifest signature is invalid.");
            }
        }

        private static byte[] DecodeBase64Url(string value)
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2:
                    base64 += "==";
                    break;
                case 3:
                    base64 += "=";
                    break;
                case 1:
                    throw new FormatException("Package signing public key contains invalid Base64Url.");
            }
            return Convert.FromBase64String(base64);
        }
    }
}
