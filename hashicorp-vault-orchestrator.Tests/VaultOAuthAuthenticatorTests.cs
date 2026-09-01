// Copyright 2025 Keyfactor
// Licensed under the Apache License, Version 2.0

using System;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault.Tests
{
    public class VaultOAuthAuthenticatorTests
    {
        private static readonly ILogger Logger = Mock.Of<ILogger>();

        public class GetJwtAsyncTests
        {
            [Fact]
            public async Task SendsClientCredentialsAsBodyParams_NotBasicAuth()
            {
                using var server = new SingleRequestHttpServer();
                var responseTask = server.RespondOnceAsync("{\"access_token\":\"abc.def.ghi\",\"token_type\":\"Bearer\",\"expires_in\":3600}");

                var resultTask = VaultOAuthAuthenticator.GetJwtAsync($"http://127.0.0.1:{server.Port}/token", "my-client-id", "my-secret", null, Logger);

                await Task.WhenAll(responseTask, resultTask);
                var jwt = await resultTask;

                jwt.Should().Be("abc.def.ghi");
                server.LastRequestBody.Should().Contain("grant_type=client_credentials");
                server.LastRequestBody.Should().Contain("client_id=my-client-id");
                server.LastRequestBody.Should().Contain("client_secret=my-secret");
                server.LastRequestRaw.Should().NotContain("Authorization:");
            }

            [Fact]
            public async Task IncludesScopeWhenProvided()
            {
                using var server = new SingleRequestHttpServer();
                var responseTask = server.RespondOnceAsync("{\"access_token\":\"abc\"}");

                var resultTask = VaultOAuthAuthenticator.GetJwtAsync($"http://127.0.0.1:{server.Port}/token", "id", "secret", "myscope", Logger);

                await Task.WhenAll(responseTask, resultTask);

                server.LastRequestBody.Should().Contain("scope=myscope");
            }

            [Fact]
            public async Task OmitsScopeWhenNotProvided()
            {
                using var server = new SingleRequestHttpServer();
                var responseTask = server.RespondOnceAsync("{\"access_token\":\"abc\"}");

                var resultTask = VaultOAuthAuthenticator.GetJwtAsync($"http://127.0.0.1:{server.Port}/token", "id", "secret", "", Logger);

                await Task.WhenAll(responseTask, resultTask);

                server.LastRequestBody.Should().NotContain("scope=");
            }

            [Fact]
            public async Task ThrowsWhenIdpReturnsNonSuccessStatus()
            {
                using var server = new SingleRequestHttpServer();
                var responseTask = server.RespondOnceAsync("{\"error\":\"invalid_client\"}", "400 Bad Request");

                var act = async () => await Task.WhenAll(responseTask, VaultOAuthAuthenticator.GetJwtAsync($"http://127.0.0.1:{server.Port}/token", "id", "secret", null, Logger));

                await act.Should().ThrowAsync<Exception>().WithMessage("*400*invalid_client*");
            }

            [Fact]
            public async Task ThrowsWhenAccessTokenMissingFromResponse()
            {
                using var server = new SingleRequestHttpServer();
                var responseTask = server.RespondOnceAsync("{\"token_type\":\"Bearer\"}");

                var act = async () => await Task.WhenAll(responseTask, VaultOAuthAuthenticator.GetJwtAsync($"http://127.0.0.1:{server.Port}/token", "id", "secret", null, Logger));

                await act.Should().ThrowAsync<Exception>().WithMessage("*access_token*");
            }
        }

        public class LoginWithJwtAsyncTests
        {
            [Fact]
            public async Task PostsToNormalizedMountPointLoginPath_WithRoleAndJwtInBody()
            {
                using var server = new SingleRequestHttpServer();
                var responseTask = server.RespondOnceAsync("{\"auth\":{\"client_token\":\"hvs.ABC123\"}}");

                var resultTask = VaultOAuthAuthenticator.LoginWithJwtAsync($"http://127.0.0.1:{server.Port}", "jwt/", "my-role", "the.jwt.value", null, Logger);

                await Task.WhenAll(responseTask, resultTask);
                var token = await resultTask;

                token.Should().Be("hvs.ABC123");
                server.LastRequestRaw.Should().StartWith("POST /v1/auth/jwt/login HTTP/1.1");

                using var body = JsonDocument.Parse(server.LastRequestBody);
                body.RootElement.GetProperty("role").GetString().Should().Be("my-role");
                body.RootElement.GetProperty("jwt").GetString().Should().Be("the.jwt.value");
            }

            [Fact]
            public async Task DefaultsToJwtMountPoint_WhenAuthMountPointNotProvided()
            {
                using var server = new SingleRequestHttpServer();
                var responseTask = server.RespondOnceAsync("{\"auth\":{\"client_token\":\"hvs.ABC123\"}}");

                var resultTask = VaultOAuthAuthenticator.LoginWithJwtAsync($"http://127.0.0.1:{server.Port}", null, "my-role", "the.jwt.value", null, Logger);

                await Task.WhenAll(responseTask, resultTask);

                server.LastRequestRaw.Should().StartWith("POST /v1/auth/jwt/login HTTP/1.1");
            }

            [Fact]
            public async Task SendsNamespaceHeader_WhenNamespaceConfigured()
            {
                using var server = new SingleRequestHttpServer();
                var responseTask = server.RespondOnceAsync("{\"auth\":{\"client_token\":\"hvs.ABC123\"}}");

                var resultTask = VaultOAuthAuthenticator.LoginWithJwtAsync($"http://127.0.0.1:{server.Port}", "jwt/", "my-role", "the.jwt.value", "engineering/team-a", Logger);

                await Task.WhenAll(responseTask, resultTask);

                server.LastRequestRaw.Should().Contain("X-Vault-Namespace: engineering/team-a");
            }

            [Fact]
            public async Task OmitsNamespaceHeader_WhenNamespaceNotConfigured()
            {
                using var server = new SingleRequestHttpServer();
                var responseTask = server.RespondOnceAsync("{\"auth\":{\"client_token\":\"hvs.ABC123\"}}");

                var resultTask = VaultOAuthAuthenticator.LoginWithJwtAsync($"http://127.0.0.1:{server.Port}", "jwt/", "my-role", "the.jwt.value", null, Logger);

                await Task.WhenAll(responseTask, resultTask);

                server.LastRequestRaw.Should().NotContain("X-Vault-Namespace");
            }

            [Fact]
            public async Task ThrowsWhenVaultReturnsNonSuccessStatus()
            {
                using var server = new SingleRequestHttpServer();
                var responseTask = server.RespondOnceAsync("{\"errors\":[\"permission denied\"]}", "403 Forbidden");

                var act = async () => await Task.WhenAll(responseTask, VaultOAuthAuthenticator.LoginWithJwtAsync($"http://127.0.0.1:{server.Port}", "jwt/", "my-role", "the.jwt.value", null, Logger));

                await act.Should().ThrowAsync<Exception>().WithMessage("*403*permission denied*");
            }

            [Fact]
            public async Task ThrowsWhenClientTokenMissingFromResponse()
            {
                using var server = new SingleRequestHttpServer();
                var responseTask = server.RespondOnceAsync("{\"auth\":{}}");

                var act = async () => await Task.WhenAll(responseTask, VaultOAuthAuthenticator.LoginWithJwtAsync($"http://127.0.0.1:{server.Port}", "jwt/", "my-role", "the.jwt.value", null, Logger));

                await act.Should().ThrowAsync<Exception>().WithMessage("*client_token*");
            }
        }
    }
}
