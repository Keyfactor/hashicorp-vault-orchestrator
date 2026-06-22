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
    /// Use the static factory methods as the starting point.
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

        /// <summary>
        /// Builds a VaultApiException with the given HTTP status code.
        /// VaultApiException(HttpStatusCode, string) is the public constructor in VaultSharp 1.x.
        /// </summary>
        private static VaultApiException MakeVaultApiException(HttpStatusCode statusCode, string errorMessage = "vault error")
        {
            return new VaultApiException(statusCode, $"{{\"errors\":[\"{errorMessage}\"]}}");
        }

        /// <summary>
        /// Creates a builder whose sys/mounts call returns a backend dictionary
        /// that includes the given mountPoint with the specified KV version.
        /// </summary>
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

            var mountsResponse = new Secret<Dictionary<string, SecretsEngine>>
            {
                Data = backends
            };

            // GetSecretBackendsAsync() takes zero parameters — no optional args.
            b.System
                .Setup(s => s.GetSecretBackendsAsync())
                .ReturnsAsync(mountsResponse);

            return b;
        }

        /// <summary>
        /// Creates a builder whose sys/mounts call throws a 403 VaultApiException.
        /// </summary>
        public static VaultMockBuilder WithForbiddenMounts()
        {
            var b = new VaultMockBuilder();
            b.WireHierarchy();

            b.System
                .Setup(s => s.GetSecretBackendsAsync())
                .ThrowsAsync(MakeVaultApiException(HttpStatusCode.Forbidden, "permission denied"));

            return b;
        }

        /// <summary>
        /// Creates a builder whose sys/mounts call throws an unexpected (non-403) error.
        /// </summary>
        public static VaultMockBuilder WithMountsError(HttpStatusCode statusCode = HttpStatusCode.InternalServerError)
        {
            var b = new VaultMockBuilder();
            b.WireHierarchy();

            b.System
                .Setup(s => s.GetSecretBackendsAsync())
                .ThrowsAsync(MakeVaultApiException(statusCode, "internal server error"));

            return b;
        }

        /// <summary>
        /// Creates a builder whose sys/mounts succeeds but the specified mountPoint
        /// is not in the returned dictionary.
        /// </summary>
        public static VaultMockBuilder WithMountNotFound(string presentMount = "other/")
        {
            var b = new VaultMockBuilder();
            b.WireHierarchy();

            var backends = new Dictionary<string, SecretsEngine>
            {
                [presentMount] = new SecretsEngine { Options = new Dictionary<string, object>() }
            };

            var mountsResponse = new Secret<Dictionary<string, SecretsEngine>> { Data = backends };
            b.System
                .Setup(s => s.GetSecretBackendsAsync())
                .ReturnsAsync(mountsResponse);

            return b;
        }

        private void WireHierarchy()
        {
            Client.Setup(c => c.V1).Returns(V1.Object);
            V1.Setup(v => v.Secrets).Returns(Secrets.Object);
            Secrets.Setup(s => s.KeyValue).Returns(KeyValue.Object);
            KeyValue.Setup(k => k.V2).Returns(KvV2.Object);
            V1.Setup(v => v.System).Returns(System.Object);
            // V1.Auth is intentionally NOT wired — IAuthMethodLoginProvider is internal
            // in VaultSharp and cannot be Moq'd.
        }
    }
}
