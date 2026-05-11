// Copyright 2025 Keyfactor
// Licensed under the Apache License, Version 2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Keyfactor.Extensions.Orchestrator.HashicorpVault;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Xunit;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault.Tests
{
    /// <summary>
    /// A testable subclass of HcvKeyValueClient that stubs all Vault I/O so path-level
    /// behaviour can be asserted without a real Vault connection.
    ///
    /// ReadSecretAutoAsync returns the entry from ReadResponses if present, otherwise an empty dict.
    /// All WriteSecretAutoAsync and PatchSecretAutoAsync calls are recorded for assertion.
    /// </summary>
    internal class TestableHcvKeyValueClient : HcvKeyValueClient
    {
        public List<(string path, Dictionary<string, object> data, string mountPoint)> WriteCalls { get; } =
            new List<(string, Dictionary<string, object>, string)>();

        public List<(string path, Dictionary<string, object> data, string mountPoint)> PatchCalls { get; } =
            new List<(string, Dictionary<string, object>, string)>();

        /// <summary>Path → response data for ReadSecretAutoAsync.  Paths not listed return an empty dict.</summary>
        public Dictionary<string, Dictionary<string, object>> ReadResponses { get; } =
            new Dictionary<string, Dictionary<string, object>>();

        public TestableHcvKeyValueClient(
            string certPath,
            string passphrasePath,
            string certPropName = null,
            string passphrasePropName = null,
            string storeType = "HCVKVPFX")
            : base(
                  vaultToken: "fake-token",
                  serverUrl: "http://127.0.0.1:8200",
                  mountPoint: "secret",
                  ns: null,
                  storeType: "Keyfactor.Extensions.Orchestrator.HashicorpVault.HCVKVPFX",
                  certPath: certPath,
                  certPropName: certPropName,
                  passphrasePath: passphrasePath,
                  passphrasePropName: passphrasePropName,
                  SubfolderInventory: false)
        {
            _storeType = storeType;
        }

        public override Task<int> GetKVVersionAsync() => Task.FromResult(2);

        public override Task WriteSecretAutoAsync(string path, Dictionary<string, object> data, string mountPoint)
        {
            WriteCalls.Add((path, data, mountPoint));
            return Task.CompletedTask;
        }

        public override Task PatchSecretAutoAsync(string path, Dictionary<string, object> data, string mountPoint)
        {
            PatchCalls.Add((path, data, mountPoint));
            return Task.CompletedTask;
        }

        public override Task<Dictionary<string, object>> ReadSecretAutoAsync(string path, string mountPoint)
        {
            return Task.FromResult(
                ReadResponses.TryGetValue(path, out var resp)
                    ? resp
                    : new Dictionary<string, object>());
        }
    }

    // ---------------------------------------------------------------------------
    // Helpers shared across test classes
    // ---------------------------------------------------------------------------
    internal static class TestCertHelper
    {
        private const string TestPassphrase = "testpass";

        /// <summary>Returns base64-encoded bytes of an empty PFX store protected by <paramref name="passphrase"/>.</summary>
        public static string EmptyPfxBase64(string passphrase = TestPassphrase)
        {
            var store = new Pkcs12StoreBuilder().Build();
            using var ms = new MemoryStream();
            store.Save(ms, passphrase.ToCharArray(), new SecureRandom());
            return Convert.ToBase64String(ms.ToArray());
        }

        /// <summary>Returns base64-encoded bytes of a PKCS12 containing a self-signed RSA cert for <paramref name="alias"/>.</summary>
        public static string SelfSignedPfxBase64(string alias, string passphrase = TestPassphrase)
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

        public const string Passphrase = TestPassphrase;
    }

    // ---------------------------------------------------------------------------
    // Tests for CreateFileStore (Management-Create path)
    // ---------------------------------------------------------------------------
    public class HcvKeyValueClientCreateFileStoreTests
    {
        private const string CertPathInput       = "stores/pfx";
        private const string PassphrasePathInput = "stores/pfx/passphrase";

        private const string ExpectedCertWritePath       = "/stores/pfx";
        private const string ExpectedPassphraseWritePath = "/stores/pfx/passphrase";

        [Fact]
        public async Task NonJson_KvV2_CertWrittenToFullCertPath()
        {
            var client = new TestableHcvKeyValueClient(CertPathInput, PassphrasePathInput);
            await client.CreateCertStore();

            Assert.NotEmpty(client.WriteCalls);
            Assert.Equal(ExpectedCertWritePath, client.WriteCalls[0].path);
        }

        [Fact]
        public async Task NonJson_KvV2_PassphraseWrittenToFullPassphrasePath()
        {
            var client = new TestableHcvKeyValueClient(CertPathInput, PassphrasePathInput);
            await client.CreateCertStore();

            Assert.True(client.WriteCalls.Count >= 2, "Expected cert + passphrase Write calls.");
            Assert.Equal(ExpectedPassphraseWritePath, client.WriteCalls[1].path);
        }

        [Fact]
        public async Task NonJson_KvV2_PassphraseUsesWriteNotPatch()
        {
            var client = new TestableHcvKeyValueClient(CertPathInput, PassphrasePathInput);
            await client.CreateCertStore();

            Assert.Empty(client.PatchCalls);
            Assert.Equal(2, client.WriteCalls.Count);
        }

        [Fact]
        public async Task Json_KvV2_CertWrittenToFullCertPath()
        {
            var client = new TestableHcvKeyValueClient(CertPathInput, PassphrasePathInput,
                certPropName: "certificate", passphrasePropName: "pass");
            await client.CreateCertStore();

            Assert.NotEmpty(client.WriteCalls);
            Assert.Equal(ExpectedCertWritePath, client.WriteCalls[0].path);
        }

        [Fact]
        public async Task NonJson_KvV2_PathsMatchGetCertificateAndPassphrasePaths()
        {
            var client = new TestableHcvKeyValueClient(CertPathInput, PassphrasePathInput);
            await client.CreateCertStore();

            Assert.Equal(2, client.WriteCalls.Count);
            Assert.Equal("/stores/pfx",           client.WriteCalls[0].path);
            Assert.Equal("/stores/pfx/passphrase", client.WriteCalls[1].path);
        }
    }

    // ---------------------------------------------------------------------------
    // Tests for PutCertificateIntoFileStore (Management-Add path)
    // ---------------------------------------------------------------------------
    public class HcvKeyValueClientPutCertificateTests
    {
        private const string CertPathInput       = "stores/pfx";
        private const string PassphrasePathInput = "stores/pfx/passphrase";

        // GetCertificateAndPassphrase reads:
        //   cert       from /stores/pfx
        //   passphrase from /stores/pfx/passphrase
        private const string NormCertPath       = "/stores/pfx";
        private const string NormPassphrasePath = "/stores/pfx/passphrase";

        private TestableHcvKeyValueClient MakeClient()
        {
            var client = new TestableHcvKeyValueClient(CertPathInput, PassphrasePathInput);

            // Seed an empty PFX store at the cert path so AddCertificate has something to load.
            client.ReadResponses[NormCertPath] = new Dictionary<string, object>
            {
                { "pfx", TestCertHelper.EmptyPfxBase64(TestCertHelper.Passphrase) }
            };
            // Seed the passphrase at the passphrase path.
            client.ReadResponses[NormPassphrasePath] = new Dictionary<string, object>
            {
                { "passphrase", TestCertHelper.Passphrase }
            };
            return client;
        }

        // Test 6: Non-JSON mode, Management-Add — cert is written to certParentPath/certSecretName, NOT certParentPath.
        [Fact]
        public async Task NonJson_KvV2_PutCertificate_CertWrittenToFullPath()
        {
            var client = new TestableHcvKeyValueClient(CertPathInput, PassphrasePathInput);
            client.ReadResponses[NormCertPath] = new Dictionary<string, object>
            {
                { "pfx", TestCertHelper.EmptyPfxBase64() }
            };
            client.ReadResponses[NormPassphrasePath] = new Dictionary<string, object>
            {
                { "passphrase", TestCertHelper.Passphrase }
            };

            var newCertPfx = TestCertHelper.SelfSignedPfxBase64("lab-pfx");
            await client.PutCertificate(
                certName: "lab-pfx",
                contents: newCertPfx,
                pfxPassword: TestCertHelper.Passphrase,
                certPath: CertPathInput,
                certPropName: null,
                keyPath: PassphrasePathInput,
                keyPropName: null,
                includeChain: false);

            // The cert write must target the full secret path, not the parent directory.
            Assert.NotEmpty(client.WriteCalls);
            var certWrite = client.WriteCalls[0];
            Assert.Equal(NormCertPath, certWrite.path);
            Assert.NotEqual("/stores", certWrite.path);
        }

        // Test 7: Non-JSON mode, Management-Add — cert write uses WriteSecretAutoAsync, not PatchSecretAutoAsync.
        [Fact]
        public async Task NonJson_KvV2_PutCertificate_UsesWriteNotPatch()
        {
            var client = new TestableHcvKeyValueClient(CertPathInput, PassphrasePathInput);
            client.ReadResponses[NormCertPath] = new Dictionary<string, object>
            {
                { "pfx", TestCertHelper.EmptyPfxBase64() }
            };
            client.ReadResponses[NormPassphrasePath] = new Dictionary<string, object>
            {
                { "passphrase", TestCertHelper.Passphrase }
            };

            var newCertPfx = TestCertHelper.SelfSignedPfxBase64("lab-pfx");
            await client.PutCertificate(
                certName: "lab-pfx",
                contents: newCertPfx,
                pfxPassword: TestCertHelper.Passphrase,
                certPath: CertPathInput,
                certPropName: null,
                keyPath: PassphrasePathInput,
                keyPropName: null,
                includeChain: false);

            // Non-JSON cert write must use Write (not Patch).
            Assert.NotEmpty(client.WriteCalls);
            Assert.Empty(client.PatchCalls);
        }
    }
}
