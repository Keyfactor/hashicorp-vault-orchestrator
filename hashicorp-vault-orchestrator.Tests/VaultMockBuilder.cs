// Copyright 2025 Keyfactor
// Licensed under the Apache License, Version 2.0

using System.Collections.Generic;
using System.Net;
using Moq;
using VaultSharp;
using VaultSharp.Core;
using VaultSharp.V1;
using VaultSharp.V1.Commons;
using VaultSharp.V1.SecretsEngines;
using VaultSharp.V1.SecretsEngines.KeyValue;
using VaultSharp.V1.SecretsEngines.KeyValue.V2;
using VaultSharp.V1.SystemBackend;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault.Tests
{
    /// <summary>
    /// Fluent builder that wires up the VaultSharp mock hierarchy.
    /// Used by GetKVVersionAsyncTests which need to mock at the IVaultClient
    /// level — below the virtual-method seam used by TestableHcvKeyValueClient.
    /// </summary>
    internal class VaultMockBuilder
    {
        public Mock<IVaultClient> Client { get; } = new(MockBehavior.Strict);
        public Mock<IVaultClientV1> V1 { get; } = new(MockBehavior.Strict);
        public Mock<ISecretsEngine> Secrets { get; } = new(MockBehavior.Strict);
        public Mock<IKeyValueSecretsEngine> KeyValue { get; } = new(MockBehavior.Strict);
        public Mock<IKeyValueSecretsEngineV2> KvV2 { get; } = new(MockBehavior.Strict);
        public Mock<ISystemBackend> System { get; } = new(MockBehavior.Strict);

        private VaultMockBuilder() { }

        private static VaultApiException MakeVaultApiException(HttpStatusCode statusCode, string errorMessage = "vault error")
            => new VaultApiException(statusCode, $"{{\"errors\":[\"{errorMessage}\"]}}");

        public static VaultMockBuilder WithKvVersion(string mountPoint, int version)
        {
            var b = new VaultMockBuilder();
            b.WireHierarchy();

            var normalizedMount = mountPoint.EndsWith("/") ? mountPoint : mountPoint + "/";
            var backends = new Dictionary<string, SecretsEngine>
            {
                [normalizedMount] = new SecretsEngine
                {
                    Options = new Dictionary<string, object> { ["version"] = version.ToString() }
                }
            };

            b.System
                .Setup(s => s.GetSecretBackendsAsync())
                .ReturnsAsync(new Secret<Dictionary<string, SecretsEngine>> { Data = backends });

            return b;
        }

        public static VaultMockBuilder WithForbiddenMounts()
        {
            var b = new VaultMockBuilder();
            b.WireHierarchy();
            b.System
                .Setup(s => s.GetSecretBackendsAsync())
                .ThrowsAsync(MakeVaultApiException(HttpStatusCode.Forbidden, "permission denied"));
            return b;
        }

        public static VaultMockBuilder WithMountsError(HttpStatusCode statusCode = HttpStatusCode.InternalServerError)
        {
            var b = new VaultMockBuilder();
            b.WireHierarchy();
            b.System
                .Setup(s => s.GetSecretBackendsAsync())
                .ThrowsAsync(MakeVaultApiException(statusCode, "internal server error"));
            return b;
        }

        public static VaultMockBuilder WithMountNotFound(string presentMount = "other/")
        {
            var b = new VaultMockBuilder();
            b.WireHierarchy();
            var backends = new Dictionary<string, SecretsEngine>
            {
                [presentMount] = new SecretsEngine { Options = new Dictionary<string, object>() }
            };
            b.System
                .Setup(s => s.GetSecretBackendsAsync())
                .ReturnsAsync(new Secret<Dictionary<string, SecretsEngine>> { Data = backends });
            return b;
        }

        private void WireHierarchy()
        {
            Client.Setup(c => c.V1).Returns(V1.Object);
            V1.Setup(v => v.Secrets).Returns(Secrets.Object);
            Secrets.Setup(s => s.KeyValue).Returns(KeyValue.Object);
            KeyValue.Setup(k => k.V2).Returns(KvV2.Object);
            V1.Setup(v => v.System).Returns(System.Object);
            // V1.Auth intentionally not wired — IAuthMethodLoginProvider is internal in VaultSharp
        }
    }
}
