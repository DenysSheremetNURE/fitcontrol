using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AuthService.Services.Cryptography;

public sealed class RsaKeyOptions
{
    public const string SectionName = "RsaKeys";
    public string KeysPath { get; set; } = "Keys";
    public bool AllowKeyGeneration { get; set; } = false;
}

public interface IRsaKeyManager
{
    SigningCredentials GetSigningCredentials();
    RsaSecurityKey GetPublicKey();
}

public sealed class RsaKeyManager : IRsaKeyManager, IDisposable
{
    private const int KeySizeBits = 2048;

    private readonly RSA _rsa;
    private readonly RsaSecurityKey _publicKey;
    private readonly SigningCredentials _signingCredentials;

    public RsaKeyManager(
        IOptions<RsaKeyOptions> options,
        IHostEnvironment environment,
        ILogger<RsaKeyManager> logger)
    {
        var rsaOptions = options.Value;
        var configuredPath = rsaOptions.KeysPath;

        var keysDirectory = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath);

        if (rsaOptions.AllowKeyGeneration)
        {
            Directory.CreateDirectory(keysDirectory);
        }

        var privateKeyPath = Path.Combine(keysDirectory, "private_key.pem");
        var publicKeyPath = Path.Combine(keysDirectory, "public_key.pem");

        try
        {
            _rsa = File.Exists(privateKeyPath)
                ? LoadPrivateKey(privateKeyPath, logger)
                : GenerateAndPersist(privateKeyPath, rsaOptions.AllowKeyGeneration, logger);

            if (rsaOptions.AllowKeyGeneration)
            {
                SyncPublicKeyFile(publicKeyPath, _rsa.ExportSubjectPublicKeyInfoPem(), logger);
            }
            else
            {
                if (!File.Exists(publicKeyPath))
                {
                    logger.LogCritical("Public key file is missing at path '{Path}' and auto-generation is disabled.", publicKeyPath);
                    throw new CryptographicException($"Public key file 'public_key.pem' is required in production mode but was not found at '{publicKeyPath}'.");
                }

                ValidateExistingPublicKeyFile(publicKeyPath, _rsa, logger);
            }

            var keyId = Convert.ToHexString(SHA256.HashData(_rsa.ExportSubjectPublicKeyInfo()))[..16]
                .ToLowerInvariant();

            _publicKey = new RsaSecurityKey(_rsa.ExportParameters(includePrivateParameters: false))
            {
                KeyId = keyId
            };

            _signingCredentials = new SigningCredentials(
                new RsaSecurityKey(_rsa) { KeyId = keyId },
                SecurityAlgorithms.RsaSha256);

            logger.LogInformation("RSA key initialized successfully. kid={KeyId}, KeySize={KeySize} bits", keyId, _rsa.KeySize);
        }
        catch
        {
            _rsa?.Dispose();
            throw;
        }
    }

    public SigningCredentials GetSigningCredentials() => _signingCredentials;
    public RsaSecurityKey GetPublicKey() => _publicKey;

    public void Dispose() => _rsa.Dispose();

    private static RSA LoadPrivateKey(string path, ILogger logger)
    {
        logger.LogInformation("Loading existing RSA private key from '{Path}'...", path);

        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(File.ReadAllText(path));

            if (rsa.KeySize < KeySizeBits)
            {
                throw new CryptographicException(
                    $"RSA key size is too short: {rsa.KeySize} bits. Minimum required is {KeySizeBits} bits.");
            }

            _ = rsa.ExportParameters(includePrivateParameters: true);

            return rsa;
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            rsa.Dispose();
            logger.LogCritical(ex, "Private key file is corrupted, missing private parameters, or fails security checks.");
            throw new CryptographicException("Failed to read RSA private key. Ensure the file contains a valid PKCS#8/RSA private key.", ex);
        }
    }

    private static RSA GenerateAndPersist(string privateKeyPath, bool allowGeneration, ILogger logger)
    {
        if (!allowGeneration)
        {
            logger.LogCritical("Private RSA key is missing at path '{Path}', and auto-generation is disabled (AllowKeyGeneration=false).", privateKeyPath);
            throw new InvalidOperationException(
                "RSA key not found. Auto-generation is disabled by setting RsaKeys:AllowKeyGeneration. Load the key via Secret/Vault.");
        }

        logger.LogWarning("Private RSA key not found. Generating new {Bits}-bit key pair...", KeySizeBits);

        var rsa = RSA.Create(KeySizeBits);
        try
        {
            WriteFileAtomically(privateKeyPath, rsa.ExportPkcs8PrivateKeyPem(), overwrite: false, isSecret: true);
            return rsa;
        }
        catch (IOException) when (File.Exists(privateKeyPath))
        {
            rsa.Dispose();
            logger.LogInformation("Key was created concurrently by another process, loading it...");
            return LoadPrivateKey(privateKeyPath, logger);
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }

    private static void ValidateExistingPublicKeyFile(string path, RSA activeRsa, ILogger logger)
    {
        string pemContent;
        try
        {
            pemContent = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Failed to read public key file at '{Path}'.", path);
            throw new CryptographicException($"Unable to read public key file at '{path}'.", ex);
        }

        using var tempPublicRsa = RSA.Create();
        try
        {
            tempPublicRsa.ImportFromPem(pemContent);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Failed to parse public key PEM content at '{Path}'.", path);
            throw new CryptographicException($"Public key file at '{path}' is invalid or corrupted PEM.", ex);
        }

        var activeParams = activeRsa.ExportParameters(false);
        var fileParams = tempPublicRsa.ExportParameters(false);

        if (!SequenceEqual(activeParams.Modulus, fileParams.Modulus) ||
            !SequenceEqual(activeParams.Exponent, fileParams.Exponent))
        {
            logger.LogCritical("Existing public_key.pem on disk does not match the active private key!");
            throw new CryptographicException("Key mismatch: public_key.pem contains a different public key.");
        }
    }

    private static void SyncPublicKeyFile(string path, string expectedPem, ILogger logger)
    {
        if (File.Exists(path) && File.ReadAllText(path).Trim() == expectedPem.Trim())
            return;

        logger.LogInformation("Updating public_key.pem on disk...");
        WriteFileAtomically(path, expectedPem, overwrite: true, isSecret: false);
    }

    private static void WriteFileAtomically(string targetPath, string content, bool overwrite, bool isSecret)
    {
        var tempPath = $"{targetPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            var streamOptions = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None
            };

            if (isSecret && !OperatingSystem.IsWindows())
                streamOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

            using (var stream = new FileStream(tempPath, streamOptions))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(content);
            }

            File.Move(tempPath, targetPath, overwrite);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static bool SequenceEqual(byte[]? a, byte[]? b)
    {
        if (a == null || b == null) return a == b;
        return a.SequenceEqual(b);
    }
}