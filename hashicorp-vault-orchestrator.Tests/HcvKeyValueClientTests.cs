// Copyright 2025 Keyfactor
// Licensed under the Apache License, Version 2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using VaultSharp.Core;
using Xunit;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault.Tests
{
    // ============================================================
    // Shared test helpers
    // ============================================================

    internal static class TestCertHelper
    {
        public const string Passphrase = "testpass";

        /// <summary>Returns base64-encoded bytes of an empty PFX store.</summary>
        public static string EmptyPfxBase64(string passphrase = Passphrase)
        {
            var store = new Pkcs12StoreBuilder().Build();
            using var ms = new MemoryStream();
            store.Save(ms, passphrase.ToCharArray(), new SecureRandom());
            return Convert.ToBase64String(ms.ToArray());
        }

        /// <summary>Returns base64-encoded bytes of a PKCS12 containing a self-signed RSA cert.</summary>
        public static string SelfSignedPfxBase64(string alias, string passphrase = Passphrase)
        {
            var keyGen = new RsaKeyPairGenerator();
            keyGen.Init(new KeyGenerationParameters(new SecureRandom(), 1024));
            AsymmetricCipherKeyPair keyPair = keyGen.GenerateKeyPair();

            var certGen = new X509V3CertificateGenerator();
            var dn = new X509Name($"CN={alias}");
            certGen.SetIssuerDN(dn);
            certGen.SetSubjectDN(dn);
            certGen.SetSerialNumber(BigInteger.ProbablePrime(64, new Random()));
            certGen.SetNotBefore(DateTime.UtcNow.AddDays(-1));
            certGen.SetNotAfter(DateTime.UtcNow.AddYears(1));
            certGen.SetPublicKey(keyPair.Public);
            var cert = certGen.Generate(new Asn1SignatureFactory("SHA256WithRSA", keyPair.Private));

            var pfx = new Pkcs12StoreBuilder().Build();
            pfx.SetKeyEntry(alias, new AsymmetricKeyEntry(keyPair.Private), new[] { new X509CertificateEntry(cert) });
            using var ms = new MemoryStream();
            pfx.Save(ms, passphrase.ToCharArray(), new SecureRandom());
            return Convert.ToBase64String(ms.ToArray());
        }

        /// <summary>Returns base64-encoded bytes of a PKCS12 containing a self-signed cert with NO private key entry (e.g. a CA/chain-only certificate).</summary>
        public static string SelfSignedCertOnlyPfxBase64(string alias, string passphrase = Passphrase)
        {
            var keyGen = new RsaKeyPairGenerator();
            keyGen.Init(new KeyGenerationParameters(new SecureRandom(), 1024));
            AsymmetricCipherKeyPair keyPair = keyGen.GenerateKeyPair();

            var certGen = new X509V3CertificateGenerator();
            var dn = new X509Name($"CN={alias}");
            certGen.SetIssuerDN(dn);
            certGen.SetSubjectDN(dn);
            certGen.SetSerialNumber(BigInteger.ProbablePrime(64, new Random()));
            certGen.SetNotBefore(DateTime.UtcNow.AddDays(-1));
            certGen.SetNotAfter(DateTime.UtcNow.AddYears(1));
            certGen.SetPublicKey(keyPair.Public);
            var cert = certGen.Generate(new Asn1SignatureFactory("SHA256WithRSA", keyPair.Private));

            var pfx = new Pkcs12StoreBuilder().Build();
            pfx.SetCertificateEntry(alias, new X509CertificateEntry(cert));
            using var ms = new MemoryStream();
            pfx.Save(ms, passphrase.ToCharArray(), new SecureRandom());
            return Convert.ToBase64String(ms.ToArray());
        }

        /// <summary>
        /// A syntactically valid (but not cryptographically real) single PEM certificate block.
        /// Header/footer literals are duplicated here rather than referencing the main project's
        /// internal CertificateHeaders class — InternalsVisibleTo targets an assembly name
        /// ("hashicorp-vault-orchestrator.Tests") that doesn't match this test project's actual
        /// &lt;AssemblyName&gt; ("Keyfactor.Extensions.Orchestrators.HCV.Tests"), so internal types
        /// aren't actually visible here. Pre-existing mismatch, out of scope for this change.
        /// </summary>
        public static string FakePemCert(string marker = "FAKECERTDATA")
            => "-----BEGIN CERTIFICATE-----\n" + marker + "\n-----END CERTIFICATE-----";
    }

    // ============================================================
    // GetKVVersionAsync tests
    // ============================================================

    public class GetKVVersionAsyncTests
    {
        [Fact]
        public async Task ReturnsTwo_WhenMountReportsVersionTwo()
        {
            var builder = VaultMockBuilder.WithKvVersion("kv-v2", 2);
            var client = new VaultClientInjectableHcvClient(builder.Client.Object, "kv-v2");

            (await client.GetKVVersionAsync()).Should().Be(2);
        }

        [Fact]
        public async Task ReturnsOne_WhenMountReportsVersionOne()
        {
            var builder = VaultMockBuilder.WithKvVersion("kv", 1);
            var client = new VaultClientInjectableHcvClient(builder.Client.Object, "kv");

            (await client.GetKVVersionAsync()).Should().Be(1);
        }

        [Fact]
        public async Task DefaultsToTwo_WhenSysMountsReturnsForbidden()
        {
            var builder = VaultMockBuilder.WithForbiddenMounts();
            var client = new VaultClientInjectableHcvClient(builder.Client.Object, "kv-v2");

            // Must not throw — 403 is non-fatal, defaults to v2
            (await client.GetKVVersionAsync()).Should().Be(2,
                because: "a 403 on sys/mounts should fall back to KV v2 rather than fail the job");
        }

        [Fact]
        public async Task UsesCache_AfterFirstSuccessfulCall()
        {
            var builder = VaultMockBuilder.WithKvVersion("kv-v2", 2);
            var client = new VaultClientInjectableHcvClient(builder.Client.Object, "kv-v2");

            await client.GetKVVersionAsync();
            await client.GetKVVersionAsync();

            builder.System.Verify(s => s.GetSecretBackendsAsync(), Moq.Times.Once);
        }

        [Fact]
        public async Task CachesForbiddenDefault_SoSysMountsCalledOnlyOnce()
        {
            var builder = VaultMockBuilder.WithForbiddenMounts();
            var client = new VaultClientInjectableHcvClient(builder.Client.Object, "kv-v2");

            await client.GetKVVersionAsync();
            await client.GetKVVersionAsync();

            builder.System.Verify(s => s.GetSecretBackendsAsync(), Moq.Times.Once);
        }

        [Fact]
        public async Task Throws_WhenSysMountsReturnsServerError()
        {
            var builder = VaultMockBuilder.WithMountsError(HttpStatusCode.InternalServerError);
            var client = new VaultClientInjectableHcvClient(builder.Client.Object, "kv-v2");

            Func<Task> act = () => client.GetKVVersionAsync();
            await act.Should().ThrowAsync<Exception>()
                .WithMessage("*",
                    because: "non-403 errors from sys/mounts are unexpected and must surface");
        }

        [Fact]
        public async Task Throws_WhenMountPointNotFoundInResponse()
        {
            var builder = VaultMockBuilder.WithMountNotFound(presentMount: "other/");
            var client = new VaultClientInjectableHcvClient(builder.Client.Object, "kv-v2");

            Func<Task> act = () => client.GetKVVersionAsync();
            await act.Should().ThrowAsync<Exception>()
                .WithMessage("*kv-v2*",
                    because: "the error should name the configured mount point");
        }

        [Fact]
        public async Task NormalizesTrailingSlash_WhenMountPointLacksOne()
        {
            var builder = VaultMockBuilder.WithKvVersion("secret", 2);
            var client = new VaultClientInjectableHcvClient(builder.Client.Object, "secret");

            (await client.GetKVVersionAsync()).Should().Be(2);
        }
    }

    // ============================================================
    // CreateFileStore tests (Management-Create path)
    // ============================================================

    public class CreateFileStoreTests
    {
        private const string CertPath           = "stores/pfx";
        private const string PassphrasePath     = "stores/pfx/passphrase";
        private const string ExpectedCertPath   = "/stores/pfx";
        private const string ExpectedPassPath   = "/stores/pfx/passphrase";

        [Fact]
        public async Task NonJson_CertWrittenToFullCertPath()
        {
            var client = new TestableHcvKeyValueClient(CertPath, PassphrasePath);
            await client.CreateCertStore();

            client.WriteCalls.Should().NotBeEmpty();
            client.WriteCalls[0].path.Should().Be(ExpectedCertPath);
        }

        [Fact]
        public async Task NonJson_PassphraseWrittenToFullPassphrasePath()
        {
            var client = new TestableHcvKeyValueClient(CertPath, PassphrasePath);
            await client.CreateCertStore();

            client.WriteCalls.Should().HaveCountGreaterThanOrEqualTo(2);
            client.WriteCalls[1].path.Should().Be(ExpectedPassPath);
        }

        [Fact]
        public async Task NonJson_PassphraseUsesWriteNotPatch()
        {
            var client = new TestableHcvKeyValueClient(CertPath, PassphrasePath);
            await client.CreateCertStore();

            client.PatchCalls.Should().BeEmpty();
            client.WriteCalls.Should().HaveCount(2);
        }

        [Fact]
        public async Task Json_CertWrittenToFullCertPath()
        {
            var client = new TestableHcvKeyValueClient(CertPath, PassphrasePath,
                certPropName: "certificate", passphrasePropName: "pass");
            await client.CreateCertStore();

            client.WriteCalls.Should().NotBeEmpty();
            client.WriteCalls[0].path.Should().Be(ExpectedCertPath);
        }

        [Fact]
        public async Task NonJson_PathsMatchGetCertificateAndPassphrasePaths()
        {
            var client = new TestableHcvKeyValueClient(CertPath, PassphrasePath);
            await client.CreateCertStore();

            client.WriteCalls.Should().HaveCount(2);
            client.WriteCalls[0].path.Should().Be("/stores/pfx");
            client.WriteCalls[1].path.Should().Be("/stores/pfx/passphrase");
        }
    }

    // ============================================================
    // PutCertificate tests (Management-Add path)
    // ============================================================

    public class PutCertificateIntoFileStoreTests
    {
        private const string CertPath       = "stores/pfx";
        private const string PassphrasePath = "stores/pfx/passphrase";
        private const string NormCertPath   = "/stores/pfx";
        private const string NormPassPath   = "/stores/pfx/passphrase";

        private TestableHcvKeyValueClient MakeSeededClient()
        {
            var client = new TestableHcvKeyValueClient(CertPath, PassphrasePath);
            client.ReadResponses[NormCertPath]  = new Dictionary<string, object> { { "pfx", TestCertHelper.EmptyPfxBase64() } };
            client.ReadResponses[NormPassPath] = new Dictionary<string, object> { { "passphrase", TestCertHelper.Passphrase } };
            return client;
        }

        [Fact]
        public async Task NonJson_CertWrittenToFullPath()
        {
            var client = MakeSeededClient();
            await client.PutCertificate(
                certName: "lab-pfx",
                contents: TestCertHelper.SelfSignedPfxBase64("lab-pfx"),
                pfxPassword: TestCertHelper.Passphrase,
                certPath: CertPath, certPropName: null,
                keyPath: PassphrasePath, keyPropName: null,
                includeChain: false);

            client.WriteCalls.Should().NotBeEmpty();
            client.WriteCalls[0].path.Should().Be(NormCertPath,
                because: "the cert must be written to the full secret path, not the parent directory");
        }

        [Fact]
        public async Task NonJson_CertUsesWriteNotPatch()
        {
            var client = MakeSeededClient();
            await client.PutCertificate(
                certName: "lab-pfx",
                contents: TestCertHelper.SelfSignedPfxBase64("lab-pfx"),
                pfxPassword: TestCertHelper.Passphrase,
                certPath: CertPath, certPropName: null,
                keyPath: PassphrasePath, keyPropName: null,
                includeChain: false);

            client.PatchCalls.Should().BeEmpty(
                because: "non-JSON cert writes must use Write, not Patch");
        }
    }

    // ============================================================
    // CreatePemStore tests (Management-Create path, HCVKVPEM)
    // ============================================================

    public class CreatePemStoreTests
    {
        private const string CertPath = "certs/mycert_pem";
        private const string ExpectedCertPath = "/certs/mycert_pem";

        [Fact]
        public async Task NoPassphrasePath_SeedsCertSecretOnly()
        {
            var client = new TestableHcvKeyValueClient(CertPath, null, storeType: "HCVKVPEM");
            await client.CreateCertStore();

            client.WriteCalls.Should().HaveCount(1,
                because: "an omitted PassphrasePath means no private key secret should be seeded");
            client.WriteCalls[0].path.Should().Be(ExpectedCertPath);
        }

        [Fact]
        public async Task WithPassphrasePath_SeedsBothSecrets()
        {
            const string keyPath = "certs/mycert_pem_key";
            var client = new TestableHcvKeyValueClient(CertPath, keyPath, storeType: "HCVKVPEM");
            await client.CreateCertStore();

            client.WriteCalls.Should().HaveCount(2);
            client.WriteCalls[0].path.Should().Be(ExpectedCertPath);
            client.WriteCalls[1].path.Should().Be("/certs/mycert_pem_key");
        }

        [Fact]
        public async Task JsonPropertyMode_UsesConfiguredPropertyNames()
        {
            const string keyPath = "certs/mycert_pem_key";
            var client = new TestableHcvKeyValueClient(CertPath, keyPath,
                certPropName: "certdata", passphrasePropName: "keydata", storeType: "HCVKVPEM");
            await client.CreateCertStore();

            client.WriteCalls[0].data.Should().ContainKey("certdata");
            client.WriteCalls[1].data.Should().ContainKey("keydata");
        }
    }

    // ============================================================
    // PutCertificateIntoPemStore tests (Management-Add path, HCVKVPEM)
    // ============================================================

    public class PutCertificateIntoPemStoreTests
    {
        private const string CertPath = "certs/mycert_pem";
        private const string KeyPath = "certs/mycert_pem_key";
        private const string ExpectedCertPath = "/certs/mycert_pem";
        private const string ExpectedKeyPath = "/certs/mycert_pem_key";

        [Fact]
        public async Task CertAndKey_WrittenToSeparateSecrets()
        {
            var client = new TestableHcvKeyValueClient(CertPath, KeyPath, storeType: "HCVKVPEM");

            await client.PutCertificate(
                certName: "mycert",
                contents: TestCertHelper.SelfSignedPfxBase64("mycert"),
                pfxPassword: TestCertHelper.Passphrase,
                certPath: CertPath, certPropName: null,
                keyPath: KeyPath, keyPropName: null,
                includeChain: false);

            client.WriteCalls.Should().Contain(c => c.path == ExpectedCertPath);
            client.WriteCalls.Should().Contain(c => c.path == ExpectedKeyPath);
        }

        [Fact]
        public async Task CertOnly_NoKeyEntryInPfx_SucceedsWithoutWritingKey()
        {
            var client = new TestableHcvKeyValueClient(CertPath, KeyPath, storeType: "HCVKVPEM");

            await client.PutCertificate(
                certName: "cacert",
                contents: TestCertHelper.SelfSignedCertOnlyPfxBase64("cacert"),
                pfxPassword: TestCertHelper.Passphrase,
                certPath: CertPath, certPropName: null,
                keyPath: KeyPath, keyPropName: null,
                includeChain: false);

            client.WriteCalls.Should().ContainSingle(c => c.path == ExpectedCertPath,
                because: "a certificate-only entry (no private key) should still write the cert");
            client.WriteCalls.Should().NotContain(c => c.path == ExpectedKeyPath,
                because: "there is no private key to write");
        }

        [Fact]
        public async Task KeyPresent_NoPassphrasePathConfigured_Throws()
        {
            var client = new TestableHcvKeyValueClient(CertPath, null, storeType: "HCVKVPEM");

            Func<Task> act = () => client.PutCertificate(
                certName: "mycert",
                contents: TestCertHelper.SelfSignedPfxBase64("mycert"),
                pfxPassword: TestCertHelper.Passphrase,
                certPath: CertPath, certPropName: null,
                keyPath: null, keyPropName: null,
                includeChain: false);

            await act.Should().ThrowAsync<InvalidOperationException>(
                because: "adding a cert with a private key but no configured PassphrasePath must fail clearly, not silently drop the key");
        }

        [Fact]
        public async Task JsonPropertyMode_UsesPatchNotWrite()
        {
            var client = new TestableHcvKeyValueClient(CertPath, KeyPath,
                certPropName: "certdata", passphrasePropName: "keydata", storeType: "HCVKVPEM");

            await client.PutCertificate(
                certName: "mycert",
                contents: TestCertHelper.SelfSignedPfxBase64("mycert"),
                pfxPassword: TestCertHelper.Passphrase,
                certPath: CertPath, certPropName: "certdata",
                keyPath: KeyPath, keyPropName: "keydata",
                includeChain: false);

            client.PatchCalls.Should().Contain(c => c.path == ExpectedCertPath && c.data.ContainsKey("certdata"));
            client.PatchCalls.Should().Contain(c => c.path == ExpectedKeyPath && c.data.ContainsKey("keydata"));
            client.WriteCalls.Should().BeEmpty();
        }
    }

    // ============================================================
    // GetCertificateFromPemStore / GetCertificates tests (Inventory path, HCVKVPEM)
    // ============================================================

    public class GetCertificateFromPemStoreTests
    {
        private const string CertPath = "certs/mycert_pem";
        private const string ExpectedCertPath = "/certs/mycert_pem";
        private const string KeyPath = "certs/mycert_pem_key";
        private const string ExpectedKeyPath = "/certs/mycert_pem_key";

        [Fact]
        public async Task CertOnly_NoPassphrasePath_PrivateKeyEntryFalse()
        {
            var client = new TestableHcvKeyValueClient(CertPath, null, storeType: "HCVKVPEM");
            client.ReadResponses[ExpectedCertPath] = new Dictionary<string, object> { { "mycert_pem", TestCertHelper.FakePemCert() } };

            var (certs, warnings) = await client.GetCertificates();

            warnings.Should().BeEmpty();
            certs.Should().ContainSingle();
            certs[0].PrivateKeyEntry.Should().BeFalse(
                because: "a missing private key is a normal case now (e.g. a CA trust chain), not an error");
            certs[0].Alias.Should().Be("mycert_pem");
        }

        [Fact]
        public async Task CertAndKey_PrivateKeyEntryTrue()
        {
            var client = new TestableHcvKeyValueClient(CertPath, KeyPath, storeType: "HCVKVPEM");
            client.ReadResponses[ExpectedCertPath] = new Dictionary<string, object> { { "mycert_pem", TestCertHelper.FakePemCert() } };
            client.ReadResponses[ExpectedKeyPath] = new Dictionary<string, object> { { "mycert_pem_key", "-----BEGIN PRIVATE KEY-----\nFAKEKEY\n-----END PRIVATE KEY-----" } };

            var (certs, warnings) = await client.GetCertificates();

            warnings.Should().BeEmpty();
            certs.Should().ContainSingle();
            certs[0].PrivateKeyEntry.Should().BeTrue();
        }

        [Fact]
        public async Task JsonPropertyMode_ReadsConfiguredProperties()
        {
            var client = new TestableHcvKeyValueClient(CertPath, KeyPath,
                certPropName: "certdata", passphrasePropName: "keydata", storeType: "HCVKVPEM");
            client.ReadResponses[ExpectedCertPath] = new Dictionary<string, object> { { "certdata", TestCertHelper.FakePemCert() }, { "other", "ignored" } };
            client.ReadResponses[ExpectedKeyPath] = new Dictionary<string, object> { { "keydata", "-----BEGIN PRIVATE KEY-----\nFAKEKEY\n-----END PRIVATE KEY-----" } };

            var (certs, warnings) = await client.GetCertificates();

            certs.Should().ContainSingle();
            certs[0].PrivateKeyEntry.Should().BeTrue();
        }

        [Fact]
        public async Task NoCertificateFound_ReturnsEmptyInventory()
        {
            var client = new TestableHcvKeyValueClient(CertPath, null, storeType: "HCVKVPEM");
            // no ReadResponses seeded — cert secret reads back empty

            var (certs, warnings) = await client.GetCertificates();

            certs.Should().BeEmpty();
            warnings.Should().BeEmpty();
        }
    }

    // ============================================================
    // RemoveCertificateFromPemStore tests (Management-Remove path, HCVKVPEM)
    // ============================================================

    public class RemoveCertificateFromPemStoreTests
    {
        private const string CertPath = "certs/mycert_pem";
        private const string ExpectedCertPath = "/certs/mycert_pem";
        private const string KeyPath = "certs/mycert_pem_key";
        private const string ExpectedKeyPath = "/certs/mycert_pem_key";

        [Fact]
        public async Task NoPassphrasePath_DeletesCertSecretOnly()
        {
            var client = new TestableHcvKeyValueClient(CertPath, null, storeType: "HCVKVPEM");

            await client.RemoveCertificate("mycert");

            client.DeleteCalls.Should().ContainSingle();
            client.DeleteCalls[0].path.Should().Be(ExpectedCertPath);
        }

        [Fact]
        public async Task WithPassphrasePath_DeletesBothSecrets()
        {
            var client = new TestableHcvKeyValueClient(CertPath, KeyPath, storeType: "HCVKVPEM");

            await client.RemoveCertificate("mycert");

            client.DeleteCalls.Should().HaveCount(2);
            client.DeleteCalls.Should().Contain(c => c.path == ExpectedCertPath);
            client.DeleteCalls.Should().Contain(c => c.path == ExpectedKeyPath);
        }

        [Fact]
        public async Task SharedSecretJsonMode_DeletesOnce()
        {
            // cert and key stored as two properties on the SAME secret
            var client = new TestableHcvKeyValueClient(CertPath, CertPath,
                certPropName: "certdata", passphrasePropName: "keydata", storeType: "HCVKVPEM");

            await client.RemoveCertificate("mycert");

            client.DeleteCalls.Should().ContainSingle(
                because: "the cert and key live in the same secret, so only one delete should happen");
        }
    }

    // ============================================================
    // GetVaults (Discovery) tests — all 4 KV store types
    // ============================================================

    public class GetVaultsDiscoveryTests
    {
        [Fact]
        public async Task WholeSecretMode_MatchingKeyNameEqualsSecretName_DiscoversSecretPathDirectly()
        {
            var client = new TestableHcvKeyValueClient("/certs/", null, storeType: "HCVKVPEM");
            client.SecretPaths["/certs/"] = new List<string> { "mycert_pem" };
            client.SecretSubKeys["/certs/mycert_pem"] = new List<string> { "mycert_pem" };

            (var paths, var warnings) = await client.GetVaults("/certs/");

            warnings.Should().BeEmpty();
            paths.Should().ContainSingle().Which.Should().Be("/certs/mycert_pem",
                because: "when the matched key equals the secret's own name, the discovered path is just the secret itself");
        }

        [Fact]
        public async Task JsonPropertyMode_KeyDiffersFromSecretName_DiscoversWithPropNameSuffix()
        {
            var client = new TestableHcvKeyValueClient("/certs/", null, storeType: "HCVKVPEM");
            client.SecretPaths["/certs/"] = new List<string> { "certdata" };
            client.SecretSubKeys["/certs/certdata"] = new List<string> { "special_pem" };

            (var paths, var warnings) = await client.GetVaults("/certs/");

            paths.Should().ContainSingle().Which.Should().Be("/certs/certdata?special_pem",
                because: "a JSON sub-property distinct from the secret's own name needs the ?propName suffix to be addressable");
        }

        [Fact]
        public async Task PemStore_NoLongerSpecialCased_ReturnsRealSecretPathNotContainingFolder()
        {
            var client = new TestableHcvKeyValueClient("/certs/", null, storeType: "HCVKVPEM");
            client.SecretPaths["/certs/"] = new List<string> { "mycert_pem" };
            client.SecretSubKeys["/certs/mycert_pem"] = new List<string> { "mycert_pem" };

            (var paths, _) = await client.GetVaults("/certs/");

            paths.Should().NotContain("/certs/",
                because: "PEM discovery must no longer return the containing folder as the store path");
        }

        [Fact]
        public async Task DiscoverySuffixOverride_ChangesWhichKeysMatch()
        {
            var client = new TestableHcvKeyValueClient("/certs/", null, storeType: "HCVKVPEM", discoverySuffix: "_mycustom");
            client.SecretPaths["/certs/"] = new List<string> { "mycert_mycustom" };
            client.SecretSubKeys["/certs/mycert_mycustom"] = new List<string> { "mycert_mycustom" };

            (var paths, _) = await client.GetVaults("/certs/");

            paths.Should().ContainSingle().Which.Should().Be("/certs/mycert_mycustom");
        }

        [Fact]
        public async Task DefaultSuffix_DoesNotMatchNonMatchingCustomSuffix()
        {
            var client = new TestableHcvKeyValueClient("/certs/", null, storeType: "HCVKVPEM");
            client.SecretPaths["/certs/"] = new List<string> { "mycert_mycustom" };
            client.SecretSubKeys["/certs/mycert_mycustom"] = new List<string> { "mycert_mycustom" };

            (var paths, _) = await client.GetVaults("/certs/");

            paths.Should().BeEmpty(
                because: "the default '_pem' suffix should not match a key using a different, non-overridden suffix");
        }

        [Fact]
        public async Task RecursesIntoSubfolders()
        {
            var client = new TestableHcvKeyValueClient("/certs/", null, storeType: "HCVKVPEM");
            client.SecretPaths["/certs/"] = new List<string> { "sub/" };
            client.SecretPaths["/certs/sub/"] = new List<string> { "nested_pem" };
            client.SecretSubKeys["/certs/sub/nested_pem"] = new List<string> { "nested_pem" };

            (var paths, _) = await client.GetVaults("/certs/");

            paths.Should().ContainSingle().Which.Should().Be("/certs/sub/nested_pem");
        }

        [Fact]
        public async Task PfxDefaultSuffix_StillWorks()
        {
            var client = new TestableHcvKeyValueClient("/certs/", "/certs/passphrase", storeType: "HCVKVPFX");
            client.SecretPaths["/certs/"] = new List<string> { "mystore_pfx" };
            client.SecretSubKeys["/certs/mystore_pfx"] = new List<string> { "mystore_pfx" };

            (var paths, _) = await client.GetVaults("/certs/");

            paths.Should().ContainSingle().Which.Should().Be("/certs/mystore_pfx");
        }
    }

    // ============================================================
    // MountPoint / Namespace parsing tests
    // ============================================================

    internal static class MountPointParser
    {
        /// <summary>
        /// Replicates the InitProps MountPoint/Namespace parsing block exactly,
        /// so the algorithm can be unit-tested without a full job pipeline.
        /// </summary>
        public static (string ns, string mountPoint) Parse(
            string rawMountPoint,
            string existingNamespace = null)
        {
            var ns = existingNamespace;
            string resolvedMount = null;

            if (!string.IsNullOrEmpty(rawMountPoint))
            {
                var trimmed = rawMountPoint.TrimEnd('/');
                var lastSlash = trimmed.LastIndexOf('/');
                if (lastSlash > 0)
                {
                    if (string.IsNullOrEmpty(ns))
                        ns = trimmed.Substring(0, lastSlash).Trim('/');
                    resolvedMount = trimmed.Substring(lastSlash + 1).Trim();
                }
                else
                {
                    resolvedMount = trimmed.Trim('/');
                }
            }

            return (ns, resolvedMount);
        }
    }

    public class MountPointNamespaceParsingTests
    {
        [Fact]
        public void BareMountName_NoSplit()
        {
            var (ns, mount) = MountPointParser.Parse("secret");
            mount.Should().Be("secret");
            ns.Should().BeNullOrEmpty();
        }

        [Fact]
        public void SimpleNamespaceAndMount_TwoSegments()
        {
            var (ns, mount) = MountPointParser.Parse("myns/kv-v2");
            ns.Should().Be("myns");
            mount.Should().Be("kv-v2");
        }

        [Fact]
        public void NestedNamespace_IkeaCase_LastSlashWins()
        {
            var (ns, mount) = MountPointParser.Parse("ep/common/secret");
            ns.Should().Be("ep/common",
                because: "everything left of the last slash is the namespace in Vault Enterprise nested namespaces");
            mount.Should().Be("secret");
        }

        [Fact]
        public void DeeplyNestedNamespace_ThreeLevels()
        {
            var (ns, mount) = MountPointParser.Parse("root/level1/level2/mymount");
            ns.Should().Be("root/level1/level2");
            mount.Should().Be("mymount");
        }

        [Fact]
        public void PreExistingNamespace_NotOverwritten()
        {
            var (ns, mount) = MountPointParser.Parse("ep/common/secret", existingNamespace: "already-set");
            ns.Should().Be("already-set",
                because: "a namespace resolved by Discovery must not be overwritten by InitProps");
            mount.Should().Be("secret");
        }

        [Fact]
        public void NullMountPoint_ReturnsNulls()
        {
            var (ns, mount) = MountPointParser.Parse(null);
            mount.Should().BeNull();
            ns.Should().BeNull();
        }

        [Fact]
        public void LeadingSlash_StrippedWithoutEmptyNamespace()
        {
            var (ns, mount) = MountPointParser.Parse("/secret");
            mount.Should().Be("secret");
            ns.Should().BeNullOrEmpty();
        }

        [Fact]
        public void TrailingSlash_NormalisedAway()
        {
            var (ns, mount) = MountPointParser.Parse("ep/common/secret/");
            ns.Should().Be("ep/common");
            mount.Should().Be("secret");
        }
    }

    // ============================================================
    // IHashiClient contract smoke tests
    // ============================================================

    public class GetCertificatesTests
    {
        [Fact]
        public async Task ReturnsEmptyList_WhenStoreContainsNoCertificates()
        {
            var mockClient = new Mock<IHashiClient>();
            mockClient
                .Setup(c => c.GetCertificates())
                .ReturnsAsync((new List<Keyfactor.Orchestrators.Extensions.CurrentInventoryItem>(), new List<string>()));

            var (certs, warnings) = await mockClient.Object.GetCertificates();

            certs.Should().NotBeNull().And.BeEmpty();
            warnings.Should().NotBeNull().And.BeEmpty();
        }

        [Fact]
        public async Task ReturnsWarnings_WhenPartialFailuresOccur()
        {
            var mockClient = new Mock<IHashiClient>();
            mockClient
                .Setup(c => c.GetCertificates())
                .ReturnsAsync((
                    new List<Keyfactor.Orchestrators.Extensions.CurrentInventoryItem>(),
                    new List<string> { "Could not read secret at /test/broken" }));

            var (certs, warnings) = await mockClient.Object.GetCertificates();

            warnings.Should().ContainSingle().Which.Should().Contain("broken");
        }
    }
}
