// Copyright 2025 Keyfactor
// Licensed under the Apache License, Version 2.0

using System.Collections.Generic;
using System.Threading.Tasks;
using VaultSharp;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault.Tests
{
    /// <summary>
    /// Subclass used by GetKVVersionAsyncTests. Injects a mock IVaultClient via
    /// the protected VaultClient setter so GetKVVersionAsync can be tested against
    /// a controlled VaultSharp hierarchy without a real Vault connection.
    /// </summary>
    internal class VaultClientInjectableHcvClient : HcvKeyValueClient
    {
        public VaultClientInjectableHcvClient(IVaultClient mockVaultClient, string mountPoint = "kv-v2")
            : base(
                vaultToken: "test-token",
                serverUrl: "http://localhost:8200",
                mountPoint: mountPoint,
                ns: "",
                storeType: "Keyfactor.Extensions.Orchestrator.HashicorpVault.HCVKVPEM",
                certPath: "/test/certs",
                certPropName: "",
                passphrasePath: null,
                passphrasePropName: "")
        {
            VaultClient = mockVaultClient;
        }
    }

    /// <summary>
    /// Subclass used by CreateFileStore and PutCertificate tests. Overrides the
    /// virtual Vault I/O methods so path-level behaviour can be asserted without
    /// a real Vault connection. ReadResponses seeds canned responses; all Write
    /// and Patch calls are recorded for assertion.
    /// </summary>
    internal class TestableHcvKeyValueClient : HcvKeyValueClient
    {
        public List<(string path, Dictionary<string, object> data, string mountPoint)> WriteCalls { get; }
            = new List<(string, Dictionary<string, object>, string)>();

        public List<(string path, Dictionary<string, object> data, string mountPoint)> PatchCalls { get; }
            = new List<(string, Dictionary<string, object>, string)>();

        /// <summary>Path → canned response. Paths not listed return an empty dict.</summary>
        public Dictionary<string, Dictionary<string, object>> ReadResponses { get; }
            = new Dictionary<string, Dictionary<string, object>>();

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
}
