
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault
{
    public class HcvKeyfactorClient : IHashiClient
    {
        private ILogger logger = LogHandler.GetClassLogger<HcvKeyfactorClient>();

        private string _vaultUrl { get; set; }

        private string _vaultToken { get; set; }

        private string _mountPoint { get; set; }

        private string _storePath { get; set; }

        private string _namespace { get; set; }

        public HcvKeyfactorClient(string vaultToken, string serverUrl, string mountPoint, string storePath, string ns = null)
        {
            _vaultToken = vaultToken;
            _mountPoint = mountPoint ?? "keyfactor"; // the mount point, including the namespace.

            _storePath = !string.IsNullOrEmpty(storePath) ? "/" + storePath : storePath;
            _vaultUrl = $"{ serverUrl }/v1/{ _mountPoint.Replace("//", "/") }";
            _namespace = ns;
        }

        private void AddVaultHeaders(WebRequest req)
        {
            req.Headers.Add("X-Vault-Request", "true");
            req.Headers.Add("X-Vault-Token", _vaultToken);
            if (!string.IsNullOrEmpty(_namespace))
            {
                req.Headers.Add("X-Vault-Namespace", _namespace);
            }
        }

        // System.Text.Json deserializes Dictionary<string,object> values as boxed JsonElement,
        // not native CLR primitives (unlike Newtonsoft.Json) — a plain `as string` cast on them
        // always yields null. This extracts the string content regardless of the underlying
        // JsonValueKind (or returns null if the value is genuinely absent/JSON null).
        private static string AsString(object value)
        {
            if (value is JsonElement je)
            {
                return je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString();
            }
            return value?.ToString();
        }

        public async Task<CurrentInventoryItem> GetCertificateFromPemStore(string key)
        {
            var fullPath = $"{ _vaultUrl }/cert/{ key }";

            try
            {
                try
                {
                    var req = WebRequest.Create(fullPath);
                    AddVaultHeaders(req);
                    req.Method = WebRequestMethods.Http.Get;
                    var res = await req.GetResponseAsync();
                    CertResponse content = JsonSerializer.Deserialize<CertResponse>(new StreamReader(res.GetResponseStream()).ReadToEnd());

                    content.data.TryGetValue("certificate", out object cert);
                    content.data.TryGetValue("ca_chain", out object caChain);
                    content.data.TryGetValue("private_key", out object privateKey);
                    content.data.TryGetValue("revocation_time", out object revokeTime);

                    var certString = AsString(cert);
                    var caChainString = AsString(caChain);
                    var privateKeyString = AsString(privateKey);

                    List<string> certList = new List<string>() { certString };

                    // if the chain is available, include all certs

                    if (!string.IsNullOrEmpty(caChainString))
                    {
                        certList = caChainString.Split(new string[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries).ToList();
                    }

                    // don't include them in inventory unless they haven't been revoked

                    if (revokeTime == null || Equals(AsString(revokeTime), "0"))
                    {
                        var inventoryItem = new CurrentInventoryItem()
                        {
                            Alias = key,
                            Certificates = certList,
                            ItemStatus = OrchestratorInventoryItemStatus.Unknown,
                            PrivateKeyEntry = !string.IsNullOrEmpty(privateKeyString),
                            UseChainLevel = !string.IsNullOrEmpty(caChainString),
                        };
                        return inventoryItem;
                    }
                    return null;
                }
                catch (Exception ex)
                {
                    logger.LogWarning($"Error getting certificate \"{fullPath}\" from Vault", ex);

                    return null;
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"Error getting certificate \"{fullPath}\" from Vault", ex);
                throw;
            }
        }

        public async Task<(List<CurrentInventoryItem>, List<string>)> GetCertificates()
        {
            logger.MethodEntry();

            var getKeysPath = $"{ _vaultUrl }/certs?list=true";
            var certs = new List<CurrentInventoryItem>();
            var certNames = new List<string>();

            try
            {
                var req = WebRequest.Create(getKeysPath);
                AddVaultHeaders(req);
                req.Method = WebRequestMethods.Http.Get;

                logger.LogTrace("sending request to vault for certs", req);

                var res = await req.GetResponseAsync();

                logger.LogTrace("parsing response", res);

                var content = JsonSerializer.Deserialize<ListResponse>(new StreamReader(res.GetResponseStream()).ReadToEnd());
                string[] certKeys;

                content.data.TryGetValue("keys", out certKeys);

                certKeys.ToList().ForEach(k =>
                {
                    var cert = GetCertificateFromPemStore(k).Result;
                    if (cert != null) certs.Add(cert);
                });
            }
            catch (Exception ex)
            {
                logger.LogError($"Error getting certificates from {getKeysPath}.  Exception message: `{ex.Message}`");
            }
            return (certs, null);
        }       

        public class HashiResponse
        {
            public string request_id { get; set; }
            public bool renewable { get; set; }
            public int lease_duration { get; set; }
            public string wrap_info { get; set; }
            public string warnings { get; set; }
            public string auth { get; set; }
        }

        public class CertResponse : HashiResponse
        {
            public Dictionary<string, object> data { get; set; }
        }

        public class ListResponse : HashiResponse
        {
            public Dictionary<string, string[]> data { get; set; }
        }

        public Task<(List<string>, List<string>)> GetVaults(string storePath)
        {
            throw new NotSupportedException();
        }

        public Task PutCertificate(string alias, string contents, string pfxPassword, string certSecretPath, string certSecretPropName, string passphrasePath, string passphrasePropName,  bool includeChain)            
        {
            throw new NotSupportedException();
        }

        public Task<bool> RemoveCertificate(string certName)
        {
            throw new NotSupportedException();
        }

        public Task CreateCertStore()
        {
            throw new NotSupportedException();
        }

        public async Task<List<string>> GetTokenPoliciesAsync()
        {
            throw new NotSupportedException();
        }
    }
}