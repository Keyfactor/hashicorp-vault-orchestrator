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

        private string _mountPoint { get; set; }

        private string _storePath { get; set; }

        public HcvKeyfactorClient(string vaultToken, string serverUrl, string mountPoint, string storePath)
        {
            _vaultToken = vaultToken;
            _mountPoint = mountPoint ?? "keyfactor";
            _storePath = !string.IsNullOrEmpty(storePath) ? "/" + storePath : storePath;
            _vaultUrl = $"{ serverUrl }/v1/{ _mountPoint.Replace("/", string.Empty) }";
        }

        public async Task<CurrentInventoryItem> GetCertificate(string key)
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
                   
                    content.data.TryGetValue("certificate", out object cert);                                        
                    content.data.TryGetValue("ca_chain", out object caChain);                                        
                    content.data.TryGetValue("private_key", out object privateKey);                                        
                    content.data.TryGetValue("revocation_time", out object revokeTime);

                    List<string> certList = new List<string>() { cert as string };

                    // if the chain is available, include all certs

                    if (!string.IsNullOrEmpty(caChain as string))
                    {
                        string fullChain = caChain.ToString();
                        certList = fullChain.Split(new string[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries).ToList();
                    }
                    
                    // don't include them in inventory unless they haven't been revoked

                    if (revokeTime == null || Equals(revokeTime.ToString(), "0"))
                    {
                        var inventoryItem = new CurrentInventoryItem()
                        {
                            Alias = key,
                            Certificates = certList,
                            ItemStatus = OrchestratorInventoryItemStatus.Unknown,
                            PrivateKeyEntry = !string.IsNullOrEmpty(privateKey as string),
                            UseChainLevel = !string.IsNullOrEmpty(caChain as string),
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

        public async Task<IEnumerable<CurrentInventoryItem>> GetCertificates()
        {
            logger.MethodEntry();

            var getKeysPath = $"{ _vaultUrl }/certs?list=true";
            var certs = new List<CurrentInventoryItem>();
            var certNames = new List<string>();

            try
            {
                var req = WebRequest.Create(getKeysPath);
                req.Headers.Add("X-Vault-Request", "true");
                req.Headers.Add("X-Vault-Token", _vaultToken);
                req.Method = WebRequestMethods.Http.Get;
                
                logger.LogTrace("sending request to vault for certs", req);
                
                var res = await req.GetResponseAsync();
                
                logger.LogTrace("parsing response", res);

                var content = JsonConvert.DeserializeObject<ListResponse>(new StreamReader(res.GetResponseStream()).ReadToEnd());
                string[] certKeys;


                content.data.TryGetValue("keys", out certKeys);

                certKeys.ToList().ForEach(k =>
                {
                    var cert = GetCertificate(k).Result;
                    if (cert != null) certs.Add(cert);
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);
            }
            return certs;
        }

        public Task<List<string>> GetVaults(string storePath)
        {
            throw new NotSupportedException();
        }

        public Task PutCertificate(string certName, string contents, string pfxPassword, bool includeChain)
        {
            throw new NotSupportedException();
        }

        public Task<bool> DeleteCertificate(string certName)
        {
            throw new NotSupportedException();
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
    }
}