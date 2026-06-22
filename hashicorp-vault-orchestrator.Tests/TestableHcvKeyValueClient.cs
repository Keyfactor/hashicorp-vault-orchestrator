// Copyright 2025 Keyfactor
// Licensed under the Apache License, Version 2.0

using VaultSharp;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault.Tests
{
    /// <summary>
    /// Subclass of HcvKeyValueClient used in unit tests.
    /// Bypasses the real VaultClient constructor by accepting an injected IVaultClient
    /// and pointing the protected VaultClient property at it.
    /// </summary>
    internal class TestableHcvKeyValueClient : HcvKeyValueClient
    {
        // We call the base constructor with a dummy token/url so VaultSharp creates its own
        // real client internally — then we immediately overwrite VaultClient with the mock.
        // The dummy token and url are never used because every test replaces the client.
        public TestableHcvKeyValueClient(
            IVaultClient mockVaultClient,
            string mountPoint = "kv-v2",
            string storeType = "Keyfactor.Extensions.Orchestrator.HashicorpVault.HCVKVPEM",
            string certPath = "/test/certs",
            string ns = "")
            : base(
                vaultToken: "test-token",
                serverUrl: "http://localhost:8200",
                mountPoint: mountPoint,
                ns: ns,
                storeType: storeType,
                certPath: certPath,
                certPropName: "",
                passphrasePath: null,
                passphrasePropName: "")
        {
            // Replace the internally-created VaultClient with the injected mock
            VaultClient = mockVaultClient;
        }
    }
}
