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
        private bool _subfolderInventory { get; set; }
        private string _storeType { get; set; }

        //private VaultClientSettings clientSettings { get; set; }

        public HcvKeyValueClient(string vaultToken, string serverUrl, string mountPoint, string storePath, string storeType, bool SubfolderInventory = false)
        {
            // Initialize one of the several auth methods.
            IAuthMethodInfo authMethod = new TokenAuthMethodInfo(vaultToken);

            // Initialize settings. You can also set proxies, custom delegates etc. here.
            var clientSettings = new VaultClientSettings(serverUrl, authMethod);
            _mountPoint = mountPoint;
            _storePath = !string.IsNullOrEmpty(storePath) ? "/" + storePath : storePath;
            _vaultClient = new VaultClient(clientSettings);
            _subfolderInventory = SubfolderInventory;
            _storeType = storeType?.Split('.')[1];
        }

        public async Task<CurrentInventoryItem> GetCertificate(string key)
        {
            logger.MethodEntry();

            VaultClient.V1.Auth.ResetVaultToken();

            Dictionary<string, object> certData;
            Secret<SecretData> res;
            var fullPath = _storePath + key;

            try
            {
                try
                {
                    if (string.IsNullOrEmpty(_mountPoint))
                    {
                        res = await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretAsync(fullPath);
                    }
                    else
                    {
                        res = await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretAsync(fullPath, mountPoint: _mountPoint);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError($"Error getting certificate {fullPath}", ex);
                    throw;                    
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
                string certificate = null;

                //Validates if the "certificate" and "private_key" keys exist in certData
                if (certData.TryGetValue("certificate", out object publicKeyObj))
                {
                    certificate = publicKeyObj.ToString();
                }

                var certs = new List<string>() { certificate };

                certData.TryGetValue("private_key", out object privateKeyObj);

                // if either field is missing, don't include it in inventory

                if (publicKeyObj == null || privateKeyObj == null) return null;

                //split the chain entries (if chain is included)

                var certFooter = "\n-----END CERTIFICATE-----";

                certs = certificate.Split(new string[] { certFooter }, StringSplitOptions.RemoveEmptyEntries).ToList();

                for (int i = 0; i < certs.Count(); i++)
                {
                    certs[i] = certs[i] + certFooter;
                }

                // if the certs have not been revoked, include them

                if (certs.Count() > 0)
                {
                    return new CurrentInventoryItem()
                    {
                        Alias = key,
                        PrivateKeyEntry = privateKeyObj != null,
                        ItemStatus = OrchestratorInventoryItemStatus.Unknown,
                        UseChainLevel = certs.Count() > 1,
                        Certificates = certs
                    };
                }
                else
                {
                    return null;
                }
            }
            catch (Exception ex)
            {
                logger.LogError("Error parsing cert data", ex);
                throw;
            }
        }

        public async Task<List<string>> GetVaults(string storePath)
        {
            logger.MethodEntry();
            // there are 4 store types that use the KV secrets engine.  HCVKVPEM uses the folder as the store path.  The others use the full file path (KCVKVJKS,HCVKVPKCS12,HCVKVPFX).
            string suffix = "";
            storePath = storePath ?? _storePath;

            if (string.IsNullOrEmpty(storePath)) { storePath = "/"; }

            var vaultPaths = new List<string>();
            var entryPaths = new List<string>();

            logger.LogTrace("getting key suffix for store type. ", _storeType);

            switch (_storeType)
            {
                case StoreType.KCVKVJKS:
                    suffix = StoreFileExtensions.HCVKVJKS;
                    break;
                case StoreType.HCVKVPFX:
                    suffix = StoreFileExtensions.HCVKVPFX;
                    break;
                case StoreType.HCVKVPKCS12:
                    suffix = StoreFileExtensions.HCVKVPKCS12;
                    break;
                default:
                    suffix = "certificate";
                    break;
            }

            try
            {
                logger.LogTrace("sending request to Vault.");

                if (_mountPoint == null)
                {
                    var res = await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretPathsAsync(storePath);
                    entryPaths = res.Data.Keys.ToList();
                }
                else
                {
                    var res = await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretPathsAsync(storePath, _mountPoint);
                    entryPaths = res.Data.Keys.ToList();
                }

            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);
                throw;
            }

            logger.LogTrace("checking paths at this level.", entryPaths);

            for (var i = 0; i < entryPaths.Count(); i++)
            {
                var path = entryPaths[i];
                if (!path.EndsWith("/"))
                { // it is a secret, not a folder
                  // get the sub-keys for the secret entry

                    IDictionary<string, object> keys;
                    try
                    {
                        if (_mountPoint == null)
                        {
                            var res = await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretSubkeysAsync(storePath + path);
                            keys = res.Data.Subkeys;
                        }
                        else
                        {
                            var res = await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretSubkeysAsync(storePath + path, mountPoint: _mountPoint);
                            keys = res.Data.Subkeys;
                        }
                        // does it have an entry with the suffix we are looking for?
                        if (keys.Any(k => k.Key.Contains(suffix)))
                        {
                            if (_storeType == StoreType.HCVKVPEM)
                            {
                                // PEM stores paths are the folder/container name rather than the entry name.  
                                vaultPaths.Add(storePath);
                            }
                            else
                            {
                                vaultPaths.Add(storePath + path);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError("Error reading secret keys.", ex);
                        throw;
                    }
                }
                else
                { //it is a sub-folder.  Recurse.
                    var subPaths = await GetVaults(storePath + path);
                    vaultPaths.AddRange(subPaths);
                }
            }
            logger.MethodExit();
            vaultPaths = vaultPaths.Distinct().ToList();
            return vaultPaths;
        }


        public async Task PutCertificate(string certName, string contents, string pfxPassword, bool includeChain)
        {
            logger.MethodEntry();

            VaultClient.V1.Auth.ResetVaultToken();

            var certDict = new Dictionary<string, object>();

            var pfxBytes = Convert.FromBase64String(contents);
            Pkcs12Store p;
            using (var pfxBytesMemoryStream = new MemoryStream(pfxBytes))
            {
                Pkcs12StoreBuilder storeBuilder = new Pkcs12StoreBuilder();
                p = storeBuilder.Build();
                p.Load(pfxBytesMemoryStream, pfxPassword.ToCharArray());
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

                    logger.LogTrace($"Got Private Key String");
                    memoryStream.Close();
                    streamWriter.Close();
                    logger.LogTrace("Finished Extracting Private Key...");
                }
            }

            var pubCert = p.GetCertificate(alias).Certificate.GetEncoded();

            logger.LogTrace("converting to PEM format.");

            var pubCertPem = Pemify(Convert.ToBase64String(pubCert));

            logger.LogTrace("adding the chain certs");

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
                certDict.Add("private_key", privateKeyString);

                // certDict.Add("revocation_time", 0);

                if (includeChain)
                {

                    certDict.Add("certificate", String.Join("\n", pemChain));
                }
                else
                {
                    certDict.Add("certificate", pubCertPem);
                }
            }
            catch (Exception ex)
            {
                logger.LogError("Error parsing certificate content", ex);
                throw;
            }
            try
            {
                logger.LogTrace("writing secret to vault.");

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
            logger.MethodExit();
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
            if (_storeType != StoreType.HCVKVPEM)
            {
                return await GetCertificatesFromFileStore();
            }
            // for PEM stores, the store path 
            VaultClient.V1.Auth.ResetVaultToken();
            _storePath = _storePath.TrimStart('/');
            List<string> subPaths = new List<string>();
            var certs = new List<CurrentInventoryItem>();
            var entryNames = new List<string>();

            //Grabs the list of subpaths to get certificates from, if SubFolder Inventory is turned on.
            //Otherwise just define the single path _storePath

            if (_subfolderInventory == true)
            {
                subPaths = (await GetSubPaths(_storePath));
                subPaths.Add(_storePath);
            }
            else
            {
                subPaths.Add(_storePath);
            }

            logger.LogDebug($"SubInventoryEnabled: {_subfolderInventory}");
            foreach (var path in subPaths)
            {

                var relative_path = path.Substring(_storePath.Length);
                try
                {
                    var pos = _storePath.LastIndexOf("/");
                    var parentPath = _storePath.Substring(pos);
                    if (string.IsNullOrEmpty(_mountPoint))
                    {
                        entryNames = (await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretPathsAsync(path)).Data.Keys.ToList();
                    }
                    else
                    {
                        entryNames = (await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretPathsAsync(path, mountPoint: _mountPoint)).Data.Keys.ToList();
                    }

                    entryNames.ForEach(k =>
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


        public async Task<IEnumerable<CurrentInventoryItem>> GetCertificatesFromFileStore()
        {
            Secret<SecretData> res;

            //file stores for JKS, PKCS12 and PFX will have a "password" entry on the same level by convention.  We'll need this in order to extract the certificates for inventory.
            var pos = _storePath.LastIndexOf("/");
            var parentPath = _storePath.Substring(pos);

            try
            {
                if (string.IsNullOrEmpty(_mountPoint))
                {
                    res = (await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretAsync(parentPath));
                }
                else
                {
                    res = (await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretAsync(parentPath, mountPoint: _mountPoint));
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"Error getting certificate data from {parentPath}", ex);
                return null;
            }

            var certFields = (Dictionary<string, object>)res.Data.Data;

            switch (_storeType)
            {
                case StoreType.HCVKVPFX:
                    return GetCertsFromPFX(certFields);
                case StoreType.HCVKVPKCS12:
                    return GetCertsFromPKCS12(certFields);
                case StoreType.KCVKVJKS:
                    return GetCertsFromJKS(certFields);
            }

            throw new InvalidOperationException($"Cannot get certificates from filestore for storeType {_storeType}.");
        }

        private List<CurrentInventoryItem> GetCertsFromJKS(Dictionary<string, object> certFields)
        {
            logger.MethodEntry();
            // certFields should contain two entries.  The certificate with the "jks-contents" suffix, and "password"

            string password;
            string base64encodedCert;
            var certs = new List<CurrentInventoryItem>();

            try
            {
                var certKey = certFields.Keys.First(f => f.Contains(StoreFileExtensions.HCVKVJKS));

                if (certKey == null)
                {
                    throw new Exception($"No entry with extension '{StoreFileExtensions.HCVKVJKS}' found");
                }
                else
                {
                    base64encodedCert = certFields[certKey].ToString();
                }

                if (certFields.TryGetValue("password", out object filePasswordObj))
                {
                    password = filePasswordObj.ToString();
                }
                else
                {
                    throw new Exception($"No password entry found for PFX store '{certKey}'.");
                }

                logger.LogTrace("converting base64 encoded cert to binary.");
                var jksBytes = Convert.FromBase64String(base64encodedCert);
                var pkcs12Store = new JksUtility().JksToPkcs12Store(jksBytes, password, logger);
                certs = CurrentInventoryFromPkcs12(pkcs12Store);
                logger.MethodExit();
                return certs;

            }
            catch (Exception ex)
            {
                logger.LogError("Could not read JKS file", ex);
                throw;
            }
        }

        private List<CurrentInventoryItem> GetCertsFromPKCS12(Dictionary<string, object> certFields)
        {
            logger.MethodEntry();
            string password;
            string base64encodedCert;
            var certs = new List<CurrentInventoryItem>();

            try
            {
                var certKey = certFields.Keys.First(f => f.Contains(StoreFileExtensions.HCVKVPKCS12));

                if (certKey == null)
                {
                    throw new Exception($"No entry with extension '{StoreFileExtensions.HCVKVPKCS12}' found");
                }
                else
                {
                    base64encodedCert = certFields[certKey].ToString();
                }

                if (certFields.TryGetValue("password", out object filePasswordObj))
                {
                    password = filePasswordObj.ToString();
                }
                else
                {
                    throw new Exception($"No password entry found for PKCS12 store '{certKey}'.");
                }

                // certFields should contain two entries.  The certificate with the "p12-contents" suffix, and "password"
                logger.LogTrace("converting base64 encoded cert to binary.");
                var bytes = Convert.FromBase64String(base64encodedCert);

                Pkcs12Store pkcs12Store;

                using (var stream = new MemoryStream(bytes))
                {
                    logger.LogTrace("creating pkcs12 store for working with the certificate.");
                    Pkcs12StoreBuilder storeBuilder = new Pkcs12StoreBuilder();
                    pkcs12Store = storeBuilder.Build();
                    pkcs12Store.Load(stream, password.ToCharArray());
                }
                certs = CurrentInventoryFromPkcs12(pkcs12Store);
                logger.MethodExit();
                return certs;
            }
            catch (Exception ex)
            {
                logger.LogError("Unable to read PKCS12 file.", ex);
                throw;
            }
        }

        private List<CurrentInventoryItem> GetCertsFromPFX(Dictionary<string, object> certFields)
        {
            logger.MethodEntry();
            // certFields should contain two entries.  The certificate with the "pfx-contents" suffix, and "password"
            string password;
            string base64encodedCert;
            var certs = new List<CurrentInventoryItem>();


            var certKey = certFields.Keys.First(f => f.Contains(StoreFileExtensions.HCVKVPFX));

            if (certKey == null)
            {
                throw new Exception($"No entry with extension '{StoreFileExtensions.HCVKVPFX}' found");
            }
            else
            {
                base64encodedCert = certFields[certKey].ToString();
            }

            if (certFields.TryGetValue("password", out object filePasswordObj))
            {
                password = filePasswordObj.ToString();
            }
            else
            {
                throw new Exception($"No password entry found for PFX store '{certKey}'.");
            }
            logger.LogTrace("converting base64 encoded cert to binary format.");

            var pfxBytes = Convert.FromBase64String(base64encodedCert);
            Pkcs12Store p;
            using (var pfxBytesMemoryStream = new MemoryStream(pfxBytes))
            {
                logger.LogTrace("creating pkcs12 store for working with the certificate.");
                Pkcs12StoreBuilder storeBuilder = new Pkcs12StoreBuilder();
                p = storeBuilder.Build();
                p.Load(pfxBytesMemoryStream, password.ToCharArray());
            }

            certs = CurrentInventoryFromPkcs12(p);
            logger.MethodExit();
            return certs;
        }

        private async Task<List<string>> GetSubPaths(string storagePath)
        {
            logger.MethodEntry();

            VaultClient.V1.Auth.ResetVaultToken();
            List<string> componentPaths = new List<string> { };
            try
            {
                logger.LogTrace("getting secret and path entries at this level.", storagePath);

                Secret<ListInfo> listInfo = await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretPathsAsync(storagePath, _mountPoint);

                foreach (var path in listInfo.Data.Keys)
                {
                    if (!path.EndsWith("/"))
                    {
                        continue;
                    }

                    string fullPath = $"{storagePath}{path}";
                    componentPaths.Add(fullPath);

                    List<string> subPaths = await GetSubPaths(fullPath);
                    componentPaths.AddRange(subPaths);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Error while listing component paths: {ex}");
            }
            logger.MethodExit();
            return componentPaths;
        }

        private static Func<string, string> Pemify = base64Cert =>
        {
            string FormatBase64(string ss) =>
                ss.Length <= 64 ? ss : ss.Substring(0, 64) + "\n" + FormatBase64(ss.Substring(64));

            string header = "-----BEGIN CERTIFICATE-----\n";
            string footer = "\n-----END CERTIFICATE-----";

            return header + FormatBase64(base64Cert) + footer;
        };

        private List<CurrentInventoryItem> CurrentInventoryFromPkcs12(Pkcs12Store store)
        {
            logger.MethodEntry();
            var certs = new List<CurrentInventoryItem>();

            try
            {
                using (var memoryStream = new MemoryStream())
                {
                    using (TextWriter streamWriter = new StreamWriter(memoryStream))
                    {
                        logger.LogTrace("Extracting Private Key...");
                        var pemWriter = new PemWriter(streamWriter);
                        logger.LogTrace("Created pemWriter...");
                        var aliases = store.Aliases.Cast<string>().Where(a => store.IsKeyEntry(a));
                        //logger.LogTrace($"Alias = {alias}");
                        foreach (var alias in aliases)
                        {
                            var certInventoryItem = new CurrentInventoryItem { Alias = alias };

                            var entryCerts = new List<string>();
                            logger.LogTrace("extracting public key");
                            var publicKey = store.GetCertificate(alias).Certificate.GetPublicKey();
                            var privateKeyEntry = store.GetKey(alias);
                            if (privateKeyEntry != null) certInventoryItem.PrivateKeyEntry = true;
                            pemWriter.WriteObject(publicKey);
                            streamWriter.Flush();
                            var publicKeyString = Encoding.ASCII.GetString(memoryStream.GetBuffer()).Trim()
                                .Replace("\r", "").Replace("\0", "");
                            entryCerts.Add(publicKeyString);

                            var pemChain = new List<string>();

                            logger.LogTrace("getting chain certs");

                            var chain = store.GetCertificateChain(alias).ToList();

                            chain.ForEach(c =>
                            {
                                var cert = c.Certificate.GetEncoded();
                                var encoded = Pemify(Convert.ToBase64String(cert));
                                pemChain.Add(encoded);
                            });

                            if (chain.Count() > 0)
                            {
                                certInventoryItem.UseChainLevel = true;
                                entryCerts.AddRange(pemChain);
                            }
                            certInventoryItem.Certificates = pemChain;
                            certs.Add(certInventoryItem);
                        }
                        memoryStream.Close();
                        streamWriter.Close();
                    }
                    logger.MethodExit();
                    return certs;
                }
            }
            catch (Exception ex)
            {
                logger.LogError("error extracting certs from pkcs12", ex);
                throw;
            }
        }
    }
}