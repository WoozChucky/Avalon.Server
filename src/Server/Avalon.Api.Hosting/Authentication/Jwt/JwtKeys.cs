using System.Security.Cryptography;
using Avalon.Api.Hosting.Config;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Avalon.Api.Hosting.Authentication.Jwt;

/// <summary>
/// The keys of the API's access tokens (#801, design D4.2). Identity signs them with ES256, under a key id, with a
/// private key no other service holds (<see cref="TokenValidationConfig.SigningKey"/>,
/// <see cref="TokenValidationConfig.SigningKeyId"/>). Every service checks a token against the public key its key id
/// names (<see cref="TokenValidationConfig.ValidationKeys"/>; a process that signs knows its own), with ES256 and
/// nothing else: HS256, which every token before #801 was signed with, is refused whatever is configured
/// (<see cref="TokenValidationConfig.IssuerSigningKeySetting"/> is ignored). Built once, eagerly, so a key that cannot be
/// used stops startup, naming its setting.
/// </summary>
public sealed class JwtKeys
{
    public const string SigningKeySetting = TokenValidationConfig.Section + ":SigningKey";
    public const string SigningKeyIdSetting = TokenValidationConfig.Section + ":SigningKeyId";
    public const string ValidationKeysSetting = TokenValidationConfig.Section + ":ValidationKeys";

    /// <summary>The key id of the key a process that only generates the OpenAPI document makes for itself.</summary>
    public const string EphemeralKeyId = "openapi-generation-only";

    private const string HowToSet = "See docs/development-setup.md, \"REST API signing key\".";

    private readonly IReadOnlyDictionary<string, SecurityKey> _validation;

    private JwtKeys(SigningCredentials? signing, IReadOnlyDictionary<string, SecurityKey> validation)
    {
        Signing = signing;
        _validation = validation;
    }

    /// <summary>The one algorithm an access token may be signed with.</summary>
    public static IReadOnlyList<string> Algorithms { get; } = [SecurityAlgorithms.EcdsaSha256];

    /// <summary>What identity signs with: its private key, under its key id, with ES256. Null in a process that does not sign.</summary>
    public SigningCredentials? Signing { get; }

    /// <summary>The key ids a token may name.</summary>
    public IEnumerable<string> KeyIds => _validation.Keys;

    /// <summary>
    /// The keys <paramref name="config"/> configures, for a process that signs tokens when <paramref name="signsTokens"/>
    /// (one that runs identity) and for one that only validates them otherwise. Stops startup, naming the setting, when a
    /// process that does not sign holds the private key, when a process that signs has no usable private key or key id,
    /// when a public key does not parse or a key id is not a plain name, and when a process has no key to validate with.
    /// </summary>
    public static JwtKeys Create(TokenValidationConfig? config, bool signsTokens)
    {
        config ??= new TokenValidationConfig();
        if (!signsTokens && !string.IsNullOrEmpty(config.SigningKey))
        {
            throw new InvalidOperationException(
                $"{SigningKeySetting} is set, but this process runs no service that signs access tokens: only identity " +
                $"does. Remove it here; a process without identity validates tokens with {ValidationKeysSetting} alone, " +
                $"and a key that signs could mint any token. {HowToSet}");
        }

        // Key ids are compared as configuration compares its keys, without regard to case: a key id listed in another
        // case than identity writes it still finds its key.
        var validation = new Dictionary<string, SecurityKey>(StringComparer.OrdinalIgnoreCase);
        var listed = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach ((string keyId, string value) in config.ValidationKeys)
        {
            string setting = $"{ValidationKeysSetting}:{keyId}";
            CheckKeyId(keyId, setting);
            ECDsa key = Load(value, setting, privateKey: false);
            listed[keyId] = key.ExportSubjectPublicKeyInfo();
            validation[keyId] = new ECDsaSecurityKey(key) { KeyId = keyId };
        }

        SigningCredentials? signing = null;
        if (signsTokens)
        {
            ECDsa privateKey = Load(config.SigningKey, SigningKeySetting, privateKey: true);
            string keyId = config.SigningKeyId;
            CheckKeyId(keyId, SigningKeyIdSetting);
            byte[] publicHalf = privateKey.ExportSubjectPublicKeyInfo();
            if (listed.TryGetValue(keyId, out byte[]? listedKey) && !listedKey.AsSpan().SequenceEqual(publicHalf))
            {
                throw new InvalidOperationException(
                    $"{ValidationKeysSetting}:{keyId} is not the public half of {SigningKeySetting}, though both are " +
                    $"under the key id {keyId}: list the public key of the private key identity signs with. {HowToSet}");
            }

            var ownPublic = ECDsa.Create();
            ownPublic.ImportSubjectPublicKeyInfo(publicHalf, out _);
            validation[keyId] = new ECDsaSecurityKey(ownPublic) { KeyId = keyId };
            signing = new SigningCredentials(new ECDsaSecurityKey(privateKey) { KeyId = keyId },
                SecurityAlgorithms.EcdsaSha256);
        }

        if (validation.Count == 0)
        {
            throw new InvalidOperationException(
                $"{ValidationKeysSetting} lists no key, so this process could accept no access token: set " +
                $"{ValidationKeysSetting}:<key id> to the public key identity signs with, under its key id " +
                $"({SigningKeyIdSetting}). {HowToSet}");
        }

        return new JwtKeys(signing, validation);
    }

    /// <summary>
    /// A key made in memory for a process that only generates the OpenAPI document (<c>AVALON_OPENAPI_GENERATION_ONLY</c>),
    /// which serves nothing, so the docs build needs no key at all.
    /// </summary>
    public static JwtKeys Ephemeral()
    {
        var privateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = ECDsa.Create();
        publicKey.ImportSubjectPublicKeyInfo(privateKey.ExportSubjectPublicKeyInfo(), out _);
        return new JwtKeys(
            new SigningCredentials(new ECDsaSecurityKey(privateKey) { KeyId = EphemeralKeyId }, SecurityAlgorithms.EcdsaSha256),
            new Dictionary<string, SecurityKey>(StringComparer.Ordinal)
            {
                [EphemeralKeyId] = new ECDsaSecurityKey(publicKey) { KeyId = EphemeralKeyId },
            });
    }

    /// <summary>
    /// The key <paramref name="token"/> may have been signed with: for an ES256 token, the public key its key id names;
    /// for any other token, HS256 included, nothing. So a token is never checked against a key of another kind: an HMAC
    /// keyed with a public key's bytes, an HS256 token as they were signed before #801, or a token whose header names no
    /// algorithm at all, finds no key.
    /// </summary>
    public IEnumerable<SecurityKey> Resolve(SecurityToken token, string? keyId)
    {
        string? algorithm = (token as JsonWebToken)?.Alg;
        return string.Equals(algorithm, SecurityAlgorithms.EcdsaSha256, StringComparison.Ordinal)
               && keyId is not null && _validation.TryGetValue(keyId, out SecurityKey? key)
            ? [key]
            : [];
    }

    /// <summary>
    /// A key id is written into each token and is the name of a configuration key (and, in the chart, of an environment
    /// variable): letters, digits, '.', '_' and '-', at most 64.
    /// </summary>
    private static void CheckKeyId(string? keyId, string setting)
    {
        if (string.IsNullOrEmpty(keyId))
        {
            throw new InvalidOperationException(
                $"{setting} is not set: the key id identity writes into the tokens it signs, under which every service " +
                $"lists its public key, for example the month the key was made (2026-10). {HowToSet}");
        }

        if (keyId.Length > 64 || !keyId.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
        {
            throw new InvalidOperationException(
                $"{setting}: the key id '{keyId}' is not a plain name; use at most 64 letters, digits, '.', '_' and '-'. {HowToSet}");
        }
    }

    /// <summary>
    /// The EC P-256 key <paramref name="value"/> holds, a private key in PKCS#8 (or SEC1) when <paramref name="privateKey"/>
    /// and a public SubjectPublicKeyInfo otherwise, as PEM or as the base64 of its DER; or an exception naming
    /// <paramref name="setting"/>. Never the key itself in the message.
    /// </summary>
    private static ECDsa Load(string? value, string setting, bool privateKey)
    {
        string wanted = privateKey
            ? "an EC P-256 private key in PKCS#8, as PEM or as the base64 of its DER"
            : "an EC P-256 public key (a SubjectPublicKeyInfo), as the base64 of its DER or as PEM";
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{setting} is not set: it needs {wanted}. {HowToSet}");
        }

        var key = ECDsa.Create();
        string? problem;
        try
        {
            problem = Import(key, value.Trim(), privateKey);
        }
        catch (Exception e) when (e is CryptographicException or FormatException or ArgumentException)
        {
            problem = "it does not parse as one";
        }

        if (problem is not null)
        {
            key.Dispose();
            throw new InvalidOperationException($"{setting} must be {wanted}, but {problem}. {HowToSet}");
        }

        return key;
    }

    private static string? Import(ECDsa key, string text, bool privateKey)
    {
        if (text.StartsWith("-----BEGIN", StringComparison.Ordinal))
        {
            key.ImportFromPem(text);
        }
        else
        {
            byte[] der = Convert.FromBase64String(text);
            int read;
            if (privateKey)
                key.ImportPkcs8PrivateKey(der, out read);
            else
                key.ImportSubjectPublicKeyInfo(der, out read);
            if (read != der.Length)
                return "it has bytes after the key";
        }

        if (HasPrivateKey(key) != privateKey)
            return privateKey ? "it holds a public key only" : "it holds a private key, which must stay with identity";

        ECCurve curve = key.ExportParameters(includePrivateParameters: false).Curve;
        bool p256 = curve.IsNamed && (string.Equals(curve.Oid.Value, ECCurve.NamedCurves.nistP256.Oid.Value, StringComparison.Ordinal)
            || curve.Oid.FriendlyName is "nistP256" or "ECDSA_P256" or "secp256r1" or "prime256v1");
        return p256 ? null : "it is on another curve; ES256 signs with P-256";
    }

    private static bool HasPrivateKey(ECDsa key)
    {
        try
        {
            key.SignHash(new byte[32]);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
