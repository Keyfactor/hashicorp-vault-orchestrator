// Copyright 2022 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.

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
using Newtonsoft.Json;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault
{
    public class HcvKeyfactorClient : IHashiClient
    {
        //private IVaultClient _vaultClient { get; set; }

        private ILogger logger = LogHandler.GetClassLogger<HcvKeyfactorClient>();

        private string _vaultUrl { get; set; }

        private string _vaultToken { get; set; }

        private string _secretsEngine { get; set; }


        public HcvKeyfactorClient(string vaultToken, string serverUrl, string secretsEngine, string mountPoint)
        {
            _vaultToken = vaultToken;
            _secretsEngine = secretsEngine;
            _vaultUrl = $"{ serverUrl }/v1/{ mountPoint.Replace("/", string.Empty) }";
        }

        public async Task<CurrentInventoryItem> GetCertificate(string key, string storePath, string mountPoint = "keyfactor")
        {
            var fullPath = $"{ _vaultUrl }/cert/{ key }";

            try
            {
                try
                {
                    var req = WebRequest.Create(fullPath);
                    req.Headers.Add("X-Vault-Request", "true");
                    req.Headers.Add("X-Vault-Token", _vaultToken);
                    req.Method = WebRequestMethods.Http.Get;
                    var res = await req.GetResponseAsync();
                    CertResponse content = JsonConvert.DeserializeObject<CertResponse>(new StreamReader(res.GetResponseStream()).ReadToEnd());
                    string cert = content.data["certificate"];
                    string issuingCA = content.data["issuing_ca"];
                    string privateKey = content.data["private_key"];
                    string revokeTime = content.data["revocation_time"];

                    if (revokeTime.Equals(0))
                    {
                        var inventoryItem = new CurrentInventoryItem()
                        {
                            Alias = key,
                            Certificates = new string[] { cert },
                            ItemStatus = OrchestratorInventoryItemStatus.Unknown,
                            PrivateKeyEntry = !string.IsNullOrEmpty(privateKey),
                            UseChainLevel = !string.IsNullOrEmpty(issuingCA),
                        };
                        return inventoryItem;
                    }
                    return null;
                }
                catch (Exception ex)
                {
                    logger.LogWarning("Error getting certificate (deleted?)", ex);
                    return null;
                }
            }
            catch (Exception ex)
            {
                logger.LogError("Error getting certificate from Vault", ex);
                throw;
            }
        }

        public async Task<IEnumerable<CurrentInventoryItem>> GetCertificates(string storePath, string mountPoint = "keyfactor")
        {
            var getKeysPath = $"{ _vaultUrl }/v1/{ mountPoint }/certs?list=true";
            var certs = new List<CurrentInventoryItem>();
            var certNames = new List<string>();

            try
            {
                var req = WebRequest.Create(getKeysPath);
                req.Headers.Add("X-Vault-Request", "true");
                req.Headers.Add("X-Vault-Token", _vaultToken);
                req.Method = WebRequestMethods.Http.Get;
                var res = await req.GetResponseAsync();
                var content = JsonConvert.DeserializeObject<ListResponse>(new StreamReader(res.GetResponseStream()).ReadToEnd());
                string[] certKeys;

                content.data.TryGetValue("keys", out certKeys);

                certKeys.ToList().ForEach(k =>
                {
                    var cert = GetCertificate(k, storePath, mountPoint).Result;
                    if (cert != null) certs.Add(cert);
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);
            }
            return certs;
        }

        public Task<IEnumerable<string>> GetVaults(string storePath, string mountPoint = null)
        {
            throw new NotSupportedException();
        }

        public Task PutCertificate(string certName, string contents, string pfxPassword, string storePath, string mountPoint = null)
        {
            throw new NotSupportedException();
        }

        public Task<bool> DeleteCertificate(string certName, string storePath, string mountPoint = null)
        {
            throw new NotSupportedException();
        }

        public interface HashiResponse
        {
            string request_id { get; set; }
            bool renewable { get; set; }
            int lease_duration { get; set; }
            string wrap_info { get; set; }
            string warnings { get; set; }
            string auth { get; set; }
        }

        public interface CertResponse : HashiResponse
        {
            Dictionary<string, string> data { get; set; }
        }

        public interface ListResponse : HashiResponse
        {
            Dictionary<string, string[]> data { get; set; }
        }
    }
}