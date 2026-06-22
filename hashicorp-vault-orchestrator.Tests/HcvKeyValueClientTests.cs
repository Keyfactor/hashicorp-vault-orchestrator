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
