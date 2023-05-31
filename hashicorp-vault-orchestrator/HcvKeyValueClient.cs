﻿// Copyright 2022 Keyfactor
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
        private bool _subfolderInventory { get; set; }

        //private VaultClientSettings clientSettings { get; set; }

        public HcvKeyValueClient(string vaultToken, string serverUrl, string mountPoint, string storePath, bool SubfolderInventory = false)
        {
            // Initialize one of the several auth methods.
            IAuthMethodInfo authMethod = new TokenAuthMethodInfo(vaultToken);

            // Initialize settings. You can also set proxies, custom delegates etc. here.
            var clientSettings = new VaultClientSettings(serverUrl, authMethod);
            _mountPoint = mountPoint;
            _storePath = !string.IsNullOrEmpty(storePath) ? "/" + storePath : storePath;
            _vaultClient = new VaultClient(clientSettings);
            _subfolderInventory = SubfolderInventory;
        }
        public async Task<List<string>> ListComponentPathsAsync(string storagePath)
        {
            VaultClient.V1.Auth.ResetVaultToken();
            List<string> componentPaths = new List<string> { };
            try
            {
                Secret<ListInfo> listInfo = (await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretPathsAsync(storagePath, _mountPoint));

                foreach (var path in listInfo.Data.Keys)
                {
                    if (!path.EndsWith("/"))
                    {
                        continue;
                    }

                    string fullPath = $"{storagePath}{path}";
                    componentPaths.Add(fullPath);

                    List<string> subPaths = await ListComponentPathsAsync(fullPath);
                    componentPaths.AddRange(subPaths);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Error while listing component paths: {ex}");
            }
            return componentPaths;
        }
        public async Task<CurrentInventoryItem> GetCertificate(string key)
        {
            VaultClient.V1.Auth.ResetVaultToken();

            Dictionary<string, object> certData;
            Secret<SecretData> res;
            var fullPath = _storePath + key;
            var relativePath = fullPath.Substring(_storePath.Length);
            try
            {


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
                    logger.LogWarning($"Error getting certificate (deleted?) {fullPath}", ex);
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
                string publicKey;
                bool hasPrivateKey;

                //Validates if the "PUBLIC_KEY" and "PRIVATE_KEY" keys exist in certData
                if (certData.TryGetValue("PUBLIC_KEY", out object publicKeyObj))
                {
                    publicKey = publicKeyObj?.ToString();
                }
                else
                {
                    publicKey = null;
                }

                if (certData.TryGetValue("PRIVATE_KEY", out object privateKeyObj))
                {
                    hasPrivateKey = true;
                }
                else
                {
                    hasPrivateKey = false;
                }

                var certs = new List<string>() { publicKey };

                var keys = certData.Keys.Where(k => k.StartsWith("PUBLIC_KEY_")).ToList();

                keys.ForEach(k => certs.Add(certData[k].ToString()));

                return new CurrentInventoryItem()
                {
                    Alias = relativePath,
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
                    if (KeyEntry == null) throw new Exception("Unable to retrieve private key");

                    var privateKey = KeyEntry.Key;
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

            var pubCert = p.GetCertificate(alias).Certificate.GetEncoded();
            var pubCertPem = Pemify(Convert.ToBase64String(pubCert));

            // add the certs in the chain

            var pemChain = new List<string>();
            var chain = p.GetCertificateChain(alias).ToList();

            chain.ForEach(c =>
            {
                var cert = c.Certificate.GetEncoded();
                var encoded = Pemify(Convert.ToBase64String(cert));
                pemChain.Add(encoded);
            });

            try
            {
                certDict.Add("PRIVATE_KEY", privateKeyString);
                certDict.Add("PUBLIC_KEY", pubCertPem);

                var i = 1;
                pemChain.ForEach(pc =>
                {
                    if (pc != pubCertPem)
                    {
                        certDict.Add($"PUBLIC_KEY_{i}", pc);
                        i++;
                    }
                });
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
            List<string> subPaths = new List<string>();
            //Grabs the list of subpaths to get certificates from, if SubFolder Inventory is turned on.
            //Otherwise just define the single path _storePath
            if (_subfolderInventory == true)
            {
                subPaths = (await ListComponentPathsAsync(_storePath));
                subPaths.Add(_storePath);
            }
            else
            {
                subPaths.Add(_storePath);
            }
            var certs = new List<CurrentInventoryItem>();
            var certNames = new List<string>();
            logger.LogDebug($"SubInventoryEnabled: {_subfolderInventory}");
            foreach (var path in subPaths)
            {
                var relative_path = path.Substring(_storePath.Length);
                try
                {

                    if (string.IsNullOrEmpty(_mountPoint))
                    {
                        certNames = (await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretPathsAsync(path)).Data.Keys.ToList();
                    }
                    else
                    {
                        certNames = (await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretPathsAsync(path, mountPoint: _mountPoint)).Data.Keys.ToList();
                    }

                    certNames.ForEach(k =>
                {
                    var cert = GetCertificate($"{relative_path}{k}").Result;
                    if (cert != null) certs.Add(cert);
                });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex.Message);
                    throw ex;
                }
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