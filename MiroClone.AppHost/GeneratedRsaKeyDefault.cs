using System.Security.Cryptography;
using Aspire.Hosting.ApplicationModel;

namespace MiroClone.AppHost;

/// <summary>
/// A fresh RSA-2048 signing key as a PKCS#8 PEM, generated once and then stored in the AppHost's user secrets by Aspire
/// (<c>persist: true</c>). A developer therefore never has to create or paste a key (docs/AUTH.md, ADR 0049). The value is
/// generated on the developer's machine and never written to the repository.
/// </summary>
internal sealed class GeneratedRsaKeyDefault : ParameterDefault
{
    public override string GetDefaultValue()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportPkcs8PrivateKeyPem();
    }

    public override void WriteToManifest(Aspire.Hosting.Publishing.ManifestPublishingContext context) { }
}
