using System.Formats.Asn1;
using System.Security.Cryptography.X509Certificates;

namespace Avalon.Infrastructure.GameAuth;

/// <summary>
/// Trust in the private Avalon CA: a leaf is accepted only when it chains to exactly the given CA (the system stores are
/// never consulted), and a world's server ID is read from its <c>urn:avalon:server:&lt;id&gt;</c> URI SAN.
/// </summary>
public static class AvalonCaTrust
{
    private const string SubjectAlternativeNameOid = "2.5.29.17";

    public const string ServerIdUriPrefix = "urn:avalon:server:";

    public static bool Chains(X509Certificate2 leaf, X509Certificate2 ca, DateTime nowUtc)
    {
        if (leaf.RawData.AsSpan().SequenceEqual(ca.RawData)) return false;
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(ca);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationTime = nowUtc;
        chain.ChainPolicy.VerificationTimeIgnored = false;
        if (!chain.Build(leaf)) return false;
        X509ChainElementCollection elements = chain.ChainElements;
        return elements.Count >= 2 && elements[^1].Certificate.RawData.AsSpan().SequenceEqual(ca.RawData);
    }

    public static string? ServerIdOf(X509Certificate2 leaf)
    {
        string? id = null;
        foreach (X509Extension extension in leaf.Extensions)
        {
            if (extension.Oid?.Value != SubjectAlternativeNameOid) continue;
            // GeneralNames ::= SEQUENCE OF GeneralName; uniformResourceIdentifier is context tag [6], an IA5String.
            AsnReader names = new AsnReader(extension.RawData, AsnEncodingRules.DER).ReadSequence();
            while (names.HasData)
            {
                Asn1Tag tag = names.PeekTag();
                if (tag.TagClass != TagClass.ContextSpecific || tag.TagValue != 6)
                {
                    names.ReadEncodedValue();
                    continue;
                }
                string uri = names.ReadCharacterString(UniversalTagNumber.IA5String, tag);
                if (!uri.StartsWith(ServerIdUriPrefix, StringComparison.Ordinal)) continue;
                if (id is not null) return null;
                id = uri[ServerIdUriPrefix.Length..];
            }
        }
        return string.IsNullOrEmpty(id) ? null : id;
    }
}
