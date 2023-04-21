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
using System.Text;
using System.Threading.Tasks;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using VaultSharp;
using VaultSharp.V1.AuthMethods;
using VaultSharp.V1.AuthMethods.Token;
using VaultSharp.V1.Commons;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault
{
    public class HcvKeyValueClient : IHashiClient
    {
        private IVaultClient _vaultClient { get; set; }

        protected IVaultClient VaultClient => _vaultClient;

        private ILogger logger = LogHandler.GetClassLogger<HcvKeyValueClient>();

        private string _storePath { get; set; }
        private string _mountPoint { get; set; }

        //private VaultClientSettings clientSettings { get; set; }

        public HcvKeyValueClient(string vaultToken, string serverUrl, string mountPoint, string storePath)
        {
            // Initialize one of the several auth methods.
            IAuthMethodInfo authMethod = new TokenAuthMethodInfo(vaultToken);

            // Initialize settings. You can also set proxies, custom delegates etc. here.
            var clientSettings = new VaultClientSettings(serverUrl, authMethod);
            _mountPoint = mountPoint;
            _storePath = !string.IsNullOrEmpty(storePath) ? "/" + storePath : storePath;
            _vaultClient = new VaultClient(clientSettings);
        }

        public async Task<CurrentInventoryItem> GetCertificate(string key)
        {
            VaultClient.V1.Auth.ResetVaultToken();

            Dictionary<string, object> certData;
            Secret<SecretData> res;
            
            try
            {
                var fullPath = _storePath + key;

                try
                {
                    if (_mountPoint == null)
                    {
                        res = (await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretAsync(fullPath));
                    }
                    else
                    {
                        res = (await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretAsync(fullPath, mountPoint: _mountPoint));
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning("Error getting certificate (deleted?)", ex);
                    return null;
                }

                certData = (Dictionary<string, object>)res.Data.Data;
            }
            catch (Exception ex)
            {
                logger.LogError("Error getting certificate from Vault", ex);
                throw;
            }

            try
            {
                string publicKey = certData["PUBLIC_KEY"]?.ToString() ?? null;
                bool hasPrivateKey = certData["PRIVATE_KEY"] != null;

                var certs = new List<string>() { publicKey };

                var keys = certData.Keys.Where(k => k.StartsWith("PUBLIC_KEY_")).ToList();

                keys.ForEach(k => certs.Add(certData[k].ToString()));

                return new CurrentInventoryItem()
                {
                    Alias = key,
                    PrivateKeyEntry = hasPrivateKey,
                    ItemStatus = OrchestratorInventoryItemStatus.Unknown,
                    UseChainLevel = true,
                    Certificates = certs.ToArray()
                };
            }
            catch (Exception ex)
            {
                logger.LogError("Error parsing cert data", ex);
                throw;
            }
        }

        public async Task<IEnumerable<string>> GetVaults()
        {
            VaultClient.V1.Auth.ResetVaultToken();

            var vaults = new List<string>();

            try
            {
                if (_mountPoint == null)
                {
                    vaults = (await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretPathsAsync(_storePath)).Data.Keys.ToList();
                }
                else
                {
                    vaults = (await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretPathsAsync(_storePath, _mountPoint)).Data.Keys.ToList();
                }

            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);
            }

            return vaults;
        }

        public async Task PutCertificate(string certName, string contents, string pfxPassword)
        {
            VaultClient.V1.Auth.ResetVaultToken();

            var certDict = new Dictionary<string, string>();

            var pfxBytes = Convert.FromBase64String(contents);
            Pkcs12Store p;
            using (var pfxBytesMemoryStream = new MemoryStream(pfxBytes))
            {
                p = new Pkcs12Store(pfxBytesMemoryStream,
                    pfxPassword.ToCharArray());
            }

            // Extract private key
            string alias;
            string privateKeyString;
            using (var memoryStream = new MemoryStream())
            {
                using (TextWriter streamWriter = new StreamWriter(memoryStream))
                {
                    logger.LogTrace("Extracting Private Key...");
                    var pemWriter = new PemWriter(streamWriter);
                    logger.LogTrace("Created pemWriter...");
                    alias = p.Aliases.Cast<string>().SingleOrDefault(a => p.IsKeyEntry(a));
                    logger.LogTrace($"Alias = {alias}");
                    var publicKey = p.GetCertificate(alias).Certificate.GetPublicKey();
                    logger.LogTrace($"publicKey = {publicKey}");
                    var KeyEntry = p.GetKey(alias);
                    // logger.LogTrace($"KeyEntry = {KeyEntry}");
                    if (KeyEntry == null) throw new Exception("Unable to retrieve private key");

                    var privateKey = KeyEntry.Key;
                    // logger.LogTrace($"privateKey = {privateKey}");
                    var keyPair = new AsymmetricCipherKeyPair(publicKey, privateKey);

                    pemWriter.WriteObject(keyPair.Private);
                    streamWriter.Flush();
                    privateKeyString = Encoding.ASCII.GetString(memoryStream.GetBuffer()).Trim()
                        .Replace("\r", "").Replace("\0", "");
                    // logger.LogTrace($"Got Private Key String {privateKeyString}");
                    logger.LogTrace($"Got Private Key String");
                    memoryStream.Close();
                    streamWriter.Close();
                    logger.LogTrace("Finished Extracting Private Key...");
                }
            }
            var pubCertPem = Pemify(Convert.ToBase64String(p.GetCertificate(alias).Certificate.GetEncoded()));

            try
            {
                certDict.Add("PRIVATE_KEY", privateKeyString);
                certDict.Add("PUBLIC_KEY", pubCertPem);
            }
            catch (Exception ex)
            {
                logger.LogError("Error parsing certificate content", ex);
                throw;
            }
            try
            {                
                var fullPath = _storePath + certName;

                if (_mountPoint == null)
                {
                    await VaultClient.V1.Secrets.KeyValue.V2.WriteSecretAsync(fullPath, certDict);
                }
                else
                {
                    await VaultClient.V1.Secrets.KeyValue.V2.WriteSecretAsync(fullPath, certDict, mountPoint: _mountPoint);
                }
            }
            catch (Exception ex)
            {
                logger.LogError("Error writing cert to Vault", ex);
                throw;
            }
        }

        public async Task<bool> DeleteCertificate(string certName)
        {
            VaultClient.V1.Auth.ResetVaultToken();

            try
            {
                var fullPath = _storePath + certName;

                if (_mountPoint == null)
                {
                    await VaultClient.V1.Secrets.KeyValue.V2.DeleteSecretAsync(fullPath);
                }
                else
                {
                    await VaultClient.V1.Secrets.KeyValue.V2.DeleteSecretAsync(fullPath, _mountPoint);
                }
            }
            catch (Exception ex)
            {
                logger.LogError("Error removing cert from Vault", ex);
                throw;
            }
            return true;
        }

        public async Task<IEnumerable<CurrentInventoryItem>> GetCertificates()
        {
            VaultClient.V1.Auth.ResetVaultToken();
            _storePath = _storePath.TrimStart('/');
            var certs = new List<CurrentInventoryItem>();
            var certNames = new List<string>();
            try
            {
                if (string.IsNullOrEmpty(_mountPoint))
                {
                    certNames = (await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretPathsAsync(_storePath)).Data.Keys.ToList();
                }
                else
                {
                    certNames = (await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretPathsAsync(_storePath, mountPoint: _mountPoint)).Data.Keys.ToList();
                }

                certNames.ForEach(k =>
                {
                    var cert = GetCertificate(k).Result;
                    if (cert != null) certs.Add(cert);
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);
                throw ex;
            }

            return certs;
        }
        private static Func<string, string> Pemify = base64Cert =>
        {
            string FormatBase64(string ss) =>
                ss.Length <= 64 ? ss : ss.Substring(0, 64) + "\n" + FormatBase64(ss.Substring(64));

            string header = "-----BEGIN CERTIFICATE-----\n";
            string footer = "\n-----END CERTIFICATE-----";

            return header + FormatBase64(base64Cert) + footer;
        };
    }
}
