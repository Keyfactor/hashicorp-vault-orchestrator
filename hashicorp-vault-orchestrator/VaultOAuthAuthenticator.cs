
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault
{
    // Implements the non-interactive machine-to-machine login used by "Use OAuth 2.0 (Client Credentials)":
    // 1) trade client_id/client_secret for a JWT at the third-party IdP's (PingFederate, Entra ID, etc.) token endpoint
    // 2) trade that JWT for a short-lived Vault client token via Vault's JWT auth method (auth/<mount>/login)
    // The resulting Vault client token is then used exactly like a statically-configured "Server Password" token —
    // neither HcvKeyValueClient nor HcvKeyfactorClient need to know OAuth was involved.
    internal static class VaultOAuthAuthenticator
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        internal static async Task<string> GetJwtAsync(string tokenUrl, string clientId, string clientSecret, string scope, ILogger logger)
        {
            logger.LogTrace($"requesting an OAuth 2.0 access token from {tokenUrl}");

            // client_id/client_secret sent as POST body params (RFC 6749 client_secret_post) rather than HTTP
            // Basic auth: this is the documented default for Microsoft Entra ID's token endpoint, and PingFederate
            // supports it too, whereas not every IdP accepts Basic auth for the Client Credentials grant.
            var form = new Dictionary<string, string>
            {
                { "grant_type", "client_credentials" },
                { "client_id", clientId },
                { "client_secret", clientSecret }
            };
            if (!string.IsNullOrEmpty(scope)) form["scope"] = scope;

            using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl) { Content = new FormUrlEncodedContent(form) };
            request.Headers.Add("Accept", "application/json");

            using var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"OAuth token request to {tokenUrl} failed with status {(int)response.StatusCode}: {body}");
            }

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("access_token", out var accessTokenElement))
            {
                throw new Exception($"OAuth token response from {tokenUrl} did not include an 'access_token' field.");
            }

            logger.LogTrace("received an access token from the IdP.");
            return accessTokenElement.GetString();
        }

        internal static async Task<string> LoginWithJwtAsync(string vaultServerUrl, string authMountPoint, string roleName, string jwt, string ns, ILogger logger)
        {
            var mountPoint = string.IsNullOrEmpty(authMountPoint) ? "jwt" : authMountPoint.Trim('/');
            var loginUrl = $"{vaultServerUrl.TrimEnd('/')}/v1/auth/{mountPoint}/login";

            logger.LogTrace($"logging in to Vault's JWT auth method at {loginUrl} with role '{roleName}'");

            var payload = JsonSerializer.Serialize(new { role = roleName, jwt });

            using var request = new HttpRequestMessage(HttpMethod.Post, loginUrl) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
            if (!string.IsNullOrEmpty(ns)) request.Headers.Add("X-Vault-Namespace", ns);

            using var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Vault JWT login at {loginUrl} failed with status {(int)response.StatusCode}: {body}");
            }

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("auth", out var authElement) || !authElement.TryGetProperty("client_token", out var tokenElement))
            {
                throw new Exception($"Vault JWT login response from {loginUrl} did not include an 'auth.client_token' field.");
            }

            logger.LogTrace("received a Vault client token from the JWT login.");
            return tokenElement.GetString();
        }
    }
}
