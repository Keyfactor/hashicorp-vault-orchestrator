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
    public class HcvClient
    {
        private IVaultClient _vaultClient { get; set; }

        protected IVaultClient VaultClient => _vaultClient;

        private ILogger logger = LogHandler.GetClassLogger<HcvClient>();

        private string _storePath { get; set; }

        private VaultClientSettings clientSettings { get; set; }

        private static readonly string privKeyStart = "-----BEGIN RSA PRIVATE KEY-----\n";
        private static readonly string privKeyEnd = "\n-----END RSA PRIVATE KEY-----";

        public HcvClient(string vaultToken, string serverUrl)
        {
            // Initialize one of the several auth methods.
            IAuthMethodInfo authMethod = new TokenAuthMethodInfo(vaultToken);

            // Initialize settings. You can also set proxies, custom delegates etc. here.
            clientSettings = new VaultClientSettings(serverUrl, authMethod);

            _vaultClient = new VaultClient(clientSettings);
        }

        public async Task<CurrentInventoryItem> GetCertificate(string key, string storePath, string mountPoint = null)
        {
            VaultClient.V1.Auth.ResetVaultToken();

            Dictionary<string, object> certData;
            Secret<SecretData> res;

            storePath = !string.IsNullOrEmpty(storePath) ? "/" + storePath : storePath; //add the slash back in.
            try
            {
                var fullPath = storePath + "/" + key;

                try
                {
                    if (mountPoint == null)
                    {
                        res = (await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretAsync(fullPath));
                    }
                    else
                    {
                        res = (await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretAsync(fullPath, mountPoint: mountPoint));
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

        public async Task<IEnumerable<string>> GetVaults(string storePath, string mountPoint = null)
        {
            VaultClient.V1.Auth.ResetVaultToken();

            var vaults = new List<string>();

            try
            {
                if (mountPoint == null)
                {
                    vaults = (await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretPathsAsync(storePath)).Data.Keys.ToList();
                }
                else
                {
                    vaults = (await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretPathsAsync(storePath, mountPoint)).Data.Keys.ToList();
                }

            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);
            }

            return vaults;
        }

        public async Task PutCertificate(string certName, string contents, string pfxPassword, string storePath, string mountPoint = null)
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
                    logger.LogTrace($"KeyEntry = {KeyEntry}");
                    if (KeyEntry == null) throw new Exception("Unable to retrieve private key");

                    var privateKey = KeyEntry.Key;
                    logger.LogTrace($"privateKey = {privateKey}");
                    var keyPair = new AsymmetricCipherKeyPair(publicKey, privateKey);

                    pemWriter.WriteObject(keyPair.Private);
                    streamWriter.Flush();
                    privateKeyString = Encoding.ASCII.GetString(memoryStream.GetBuffer()).Trim()
                        .Replace("\r", "").Replace("\0", "");
                    logger.LogTrace($"Got Private Key String {privateKeyString}");
                    memoryStream.Close();
                    streamWriter.Close();
                    logger.LogTrace("Finished Extracting Private Key...");
                }
            }
            var pubCertPem = Pemify(Convert.ToBase64String(p.GetCertificate(alias).Certificate.GetEncoded()));

            try
            {
                privateKeyString = privateKeyString.Replace(privKeyStart, "").Replace(privKeyEnd, "");
                certDict.Add("PRIVATE_KEY", privateKeyString);
                certDict.Add("PUBLIC_KEY", pubCertPem);
                certDict.Add("KEY_SECRET", pfxPassword);
            }
            catch (Exception ex)
            {
                logger.LogError("Error parsing certificate content", ex);
                throw;
            }
            try
            {
                storePath = !string.IsNullOrEmpty(storePath) ? "/" + storePath : storePath; //add the slash back in.
                var fullPath = storePath + "/" + certName;

                if (mountPoint == null)
                {
                    await VaultClient.V1.Secrets.KeyValue.V2.WriteSecretAsync(fullPath, certDict);
                }
                else
                {
                    await VaultClient.V1.Secrets.KeyValue.V2.WriteSecretAsync(fullPath, certDict, mountPoint: mountPoint);
                }
            }
            catch (Exception ex)
            {
                logger.LogError("Error writing cert to Vault", ex);
                throw;
            }
        }

        public async Task<bool> DeleteCertificate(string certName, string storePath, string mountPoint = null)
        {
            VaultClient.V1.Auth.ResetVaultToken();

            try
            {
                storePath = !string.IsNullOrEmpty(storePath) ? "/" + storePath : storePath; //add the slash back in.
                var fullPath = storePath + "/" + certName;

                if (mountPoint == null)
                {
                    await VaultClient.V1.Secrets.KeyValue.V2.DeleteSecretAsync(fullPath);
                }
                else
                {
                    await VaultClient.V1.Secrets.KeyValue.V2.DeleteSecretAsync(fullPath, mountPoint);
                }
            }
            catch (Exception ex)
            {
                logger.LogError("Error removing cert from Vault", ex);
                throw;
            }
            return true;
        }

        public async Task<IEnumerable<CurrentInventoryItem>> GetCertificates(string storePath, string mountPoint = null)
        {
            VaultClient.V1.Auth.ResetVaultToken();
            storePath = !string.IsNullOrEmpty(storePath) ? "/" + storePath : storePath; //add the slash back in.

            var certs = new List<CurrentInventoryItem>();
            var certNames = new List<string>();
            try
            {
                if (string.IsNullOrEmpty(mountPoint))
                {
                    certNames = (await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretPathsAsync(storePath)).Data.Keys.ToList();
                }
                else
                {
                    certNames = (await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretPathsAsync(storePath, mountPoint)).Data.Keys.ToList();
                }

                certNames.ForEach(k =>
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

        private static Func<string, string> Pemify = ss =>
            ss.Length <= 64 ? ss : ss.Substring(0, 64) + "\n" + Pemify(ss.Substring(64));

        //private string GetCertPem(string alias, string contents, string password, ref string privateKeyString)
        //{
        //    logger.MethodEntry(LogLevel.Debug);
        //    logger.LogTrace($"alias {alias} privateKeyString {privateKeyString}");
        //    string certPem = null;
        //    try
        //    {
        //        if (!string.IsNullOrEmpty(password))
        //        {
        //            logger.LogTrace($"Certificate and Key exist for {alias}");
        //            var certData = Convert.FromBase64String(contents);

        //            var ms = new MemoryStream(certData);
        //            Pkcs12Store store = new Pkcs12Store(ms,
        //                password.ToCharArray());

                   
        //            string storeAlias;
        //            TextWriter streamWriter;
        //            using (var memoryStream = new MemoryStream())
        //            {
        //                streamWriter = new StreamWriter(memoryStream);
        //                var pemWriter = new PemWriter(streamWriter);

        //                storeAlias = store.Aliases.Cast<string>().SingleOrDefault(a => store.IsKeyEntry(a));
        //                var publicKey = store.GetCertificate(storeAlias).Certificate.GetPublicKey();
        //                var privateKey = store.GetKey(storeAlias).Key;
        //                var keyPair = new AsymmetricCipherKeyPair(publicKey, privateKey);

        //                var pkStart = "-----BEGIN RSA PRIVATE KEY-----\n";
        //                var pkEnd = "\n-----END RSA PRIVATE KEY-----";


        //                pemWriter.WriteObject(keyPair.Private);
        //                streamWriter.Flush();
        //                privateKeyString = Encoding.ASCII.GetString(memoryStream.GetBuffer()).Trim()
        //                    .Replace("\r", "")
        //                    .Replace("\0", "");
        //                privateKeyString = privateKeyString.Replace(pkStart, "").Replace(pkEnd, "");

        //                memoryStream.Close();
        //            }

        //            streamWriter.Close();

        //            // Extract server certificate
        //            certPem = Pemify(
        //                Convert.ToBase64String(store.GetCertificate(storeAlias).Certificate.GetEncoded()));                                      
        //        }
        //        else
        //        {
        //            logger.LogTrace($"Certificate ONLY for {alias}");
        //            certPem = Pemify(contents);
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        logger.LogError($"Error Generating PEM: Error {LogHandler.FlattenException(ex)}");
        //    }

        //    logger.LogTrace($"PEM {certPem}");
        //    logger.MethodEntry(LogLevel.Debug);
        //    return certPem;
        //}

    }
}
