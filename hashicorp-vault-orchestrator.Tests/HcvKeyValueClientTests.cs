// Copyright 2025 Keyfactor
// Licensed under the Apache License, Version 2.0

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using VaultSharp.Core;
using VaultSharp.V1.Commons;
using VaultSharp.V1.SecretsEngines.KeyValue.V2;
using Xunit;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault.Tests
{
    public class GetKVVersionAsyncTests
    {
        // -----------------------------------------------------------------------
        // Happy-path: version 2
        // -----------------------------------------------------------------------
        [Fact]
        public async Task GetKVVersionAsync_ReturnsTwo_WhenMountReportsVersionTwo()
        {
            var builder = VaultMockBuilder.WithKvVersion("kv-v2", 2);
            var client = new TestableHcvKeyValueClient(builder.Client.Object, mountPoint: "kv-v2");

            var result = await client.GetKVVersionAsync();

            result.Should().Be(2);
        }

        // -----------------------------------------------------------------------
        // Happy-path: version 1
        // -----------------------------------------------------------------------
        [Fact]
        public async Task GetKVVersionAsync_ReturnsOne_WhenMountReportsVersionOne()
        {
            var builder = VaultMockBuilder.WithKvVersion("kv", 1);
            var client = new TestableHcvKeyValueClient(builder.Client.Object, mountPoint: "kv");

            var result = await client.GetKVVersionAsync();

            result.Should().Be(1);
        }

        // -----------------------------------------------------------------------
        // Bug fix: 403 on sys/mounts must NOT throw — must default to v2
        // -----------------------------------------------------------------------
        [Fact]
        public async Task GetKVVersionAsync_DefaultsToTwo_WhenSysMountsReturnsForbidden()
        {
            var builder = VaultMockBuilder.WithForbiddenMounts();
            var client = new TestableHcvKeyValueClient(builder.Client.Object, mountPoint: "kv-v2");

            // Should not throw
            var result = await client.GetKVVersionAsync();

            result.Should().Be(2, because: "a 403 on sys/mounts should fall back to KV v2, not fail the job");
        }

        // -----------------------------------------------------------------------
        // Caching: sys/mounts is called at most once per client lifetime
        // -----------------------------------------------------------------------
        [Fact]
        public async Task GetKVVersionAsync_UsesCache_AfterFirstSuccessfulCall()
        {
            var builder = VaultMockBuilder.WithKvVersion("kv-v2", 2);
            var client = new TestableHcvKeyValueClient(builder.Client.Object, mountPoint: "kv-v2");

            await client.GetKVVersionAsync(); // primes cache
            await client.GetKVVersionAsync(); // should hit cache, not call sys/mounts again

            // GetSecretBackendsAsync() takes zero parameters
            builder.System.Verify(s => s.GetSecretBackendsAsync(), Times.Once);
        }

        // -----------------------------------------------------------------------
        // Caching: 403 result also gets cached so we don't hammer sys/mounts
        // -----------------------------------------------------------------------
        [Fact]
        public async Task GetKVVersionAsync_UsesCachedDefault_AfterForbiddenResponse()
        {
            var builder = VaultMockBuilder.WithForbiddenMounts();
            var client = new TestableHcvKeyValueClient(builder.Client.Object, mountPoint: "kv-v2");

            await client.GetKVVersionAsync(); // 403 → default 2, cached
            await client.GetKVVersionAsync(); // should use cache

            builder.System.Verify(s => s.GetSecretBackendsAsync(), Times.Once);
        }

        // -----------------------------------------------------------------------
        // Non-403 errors (500, network error, etc.) should still throw
        // -----------------------------------------------------------------------
        [Fact]
        public async Task GetKVVersionAsync_Throws_WhenSysMountsReturnsServerError()
        {
            var builder = VaultMockBuilder.WithMountsError(HttpStatusCode.InternalServerError);
            var client = new TestableHcvKeyValueClient(builder.Client.Object, mountPoint: "kv-v2");

            Func<Task> act = () => client.GetKVVersionAsync();

            await act.Should().ThrowAsync<Exception>(because: "non-403 errors from sys/mounts are unexpected and should surface");
        }

        // -----------------------------------------------------------------------
        // Mount not found: should still throw (misconfiguration)
        // -----------------------------------------------------------------------
        [Fact]
        public async Task GetKVVersionAsync_Throws_WhenMountPointNotFoundInResponse()
        {
            var builder = VaultMockBuilder.WithMountNotFound(presentMount: "other/");
            var client = new TestableHcvKeyValueClient(builder.Client.Object, mountPoint: "kv-v2");

            Func<Task> act = () => client.GetKVVersionAsync();

            await act.Should().ThrowAsync<Exception>()
                .WithMessage("*kv-v2*", because: "the error message should include the configured mount point");
        }

        // -----------------------------------------------------------------------
        // Mount point without trailing slash is normalized before lookup
        // -----------------------------------------------------------------------
        [Fact]
        public async Task GetKVVersionAsync_NormalizesTrailingSlash_WhenMountPointLacksOne()
        {
            var builder = VaultMockBuilder.WithKvVersion("secret", 2);
            var client = new TestableHcvKeyValueClient(builder.Client.Object, mountPoint: "secret");

            var result = await client.GetKVVersionAsync();

            result.Should().Be(2);
        }
    }

    // ---------------------------------------------------------------------------

    public class ReadSecretAutoAsyncTests
    {
        // -----------------------------------------------------------------------
        // KV v2 path: should call V2 ReadSecretAsync
        // ReadSecretAsync signature: (string path, int? version = null, string mountPoint = null, string wrapTimeToLive = null)
        // All optional params must be matched explicitly to avoid CS0854.
        // -----------------------------------------------------------------------
        [Fact]
        public async Task ReadSecretAutoAsync_CallsV2_WhenKvVersionIsTwo()
        {
            var builder = VaultMockBuilder.WithKvVersion("kv-v2", 2);
            var expectedData = new Dictionary<string, object> { ["cert"] = "MIIB..." };

            builder.KvV2
                .Setup(k => k.ReadSecretAsync(
                    It.IsAny<string>(),     // path
                    It.IsAny<int?>(),       // version (optional)
                    It.IsAny<string>(),     // mountPoint (optional)
                    It.IsAny<string>()))    // wrapTimeToLive (optional)
                .ReturnsAsync(new Secret<SecretData>
                {
                    Data = new SecretData { Data = expectedData }
                });

            var client = new TestableHcvKeyValueClient(builder.Client.Object, mountPoint: "kv-v2");

            var result = await client.ReadSecretAutoAsync("/test/certs/mycert", "kv-v2");

            result.Should().ContainKey("cert");
            result["cert"].Should().Be("MIIB...");
        }

        // -----------------------------------------------------------------------
        // 404 on V2 read must rethrow (caller decides how to handle missing entries)
        // -----------------------------------------------------------------------
        [Fact]
        public async Task ReadSecretAutoAsync_Rethrows_OnV2NotFound()
        {
            var builder = VaultMockBuilder.WithKvVersion("kv-v2", 2);

            builder.KvV2
                .Setup(k => k.ReadSecretAsync(
                    It.IsAny<string>(),
                    It.IsAny<int?>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()))
                .ThrowsAsync(new VaultApiException(HttpStatusCode.NotFound, "{\"errors\":[\"secret not found\"]}"));

            var client = new TestableHcvKeyValueClient(builder.Client.Object, mountPoint: "kv-v2");

            Func<Task> act = () => client.ReadSecretAutoAsync("/test/certs/missing", "kv-v2");

            await act.Should().ThrowAsync<VaultApiException>()
                .Where(ex => ex.HttpStatusCode == HttpStatusCode.NotFound);
        }
    }

    // ---------------------------------------------------------------------------

    public class WriteSecretAutoAsyncTests
    {
        // -----------------------------------------------------------------------
        // KV v2 write dispatches to V2 WriteSecretAsync
        // WriteSecretAsync signature: (string path, IDictionary<string,object> data, int? cas = null, string mountPoint = null, string wrapTimeToLive = null)
        // -----------------------------------------------------------------------
        [Fact]
        public async Task WriteSecretAutoAsync_CallsV2_WhenKvVersionIsTwo()
        {
            var builder = VaultMockBuilder.WithKvVersion("kv-v2", 2);

            builder.KvV2
                .Setup(k => k.WriteSecretAsync(
                    It.IsAny<string>(),                         // path
                    It.IsAny<IDictionary<string, object>>(),   // data
                    It.IsAny<int?>(),                           // cas (optional)
                    It.IsAny<string>()))                        // mountPoint (optional)
                .ReturnsAsync(new Secret<CurrentSecretMetadata> { Data = new CurrentSecretMetadata() });

            var client = new TestableHcvKeyValueClient(builder.Client.Object, mountPoint: "kv-v2");
            var data = new Dictionary<string, object> { ["cert"] = "MIIB..." };

            Func<Task> act = () => client.WriteSecretAutoAsync("/test/certs/mycert", data, "kv-v2");

            await act.Should().NotThrowAsync();

            builder.KvV2.Verify(k => k.WriteSecretAsync(
                "/test/certs/mycert",
                It.Is<IDictionary<string, object>>(d => d.ContainsKey("cert")),
                It.IsAny<int?>(),
                "kv-v2"), Times.Once);
        }
    }

    // ---------------------------------------------------------------------------

    public class GetCertificatesTests
    {
        [Fact]
        public async Task GetCertificates_ReturnsEmptyList_WhenStoreContainsNoCertificates()
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
        public async Task GetCertificates_ReturnsWarnings_WhenPartialFailuresOccur()
        {
            var mockClient = new Mock<IHashiClient>();
            mockClient
                .Setup(c => c.GetCertificates())
                .ReturnsAsync((
                    new List<Keyfactor.Orchestrators.Extensions.CurrentInventoryItem>(),
                    new List<string> { "Could not read secret at /test/broken" }));

            var (certs, warnings) = await mockClient.Object.GetCertificates();

            warnings.Should().ContainSingle()
                .Which.Should().Contain("broken");
        }
    }
}
