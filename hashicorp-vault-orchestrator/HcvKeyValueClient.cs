// Copyright 2023 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Keyfactor.Extensions.Orchestrator.HashicorpVault.FileStores;
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
using VaultSharp.V1.SecretsEngines.KeyValue.V2;

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

        public HcvKeyValueClient(string vaultToken, string serverUrl, string mountPoint, string storePath, string storeType, bool SubfolderInventory = false)
        {
            // Initialize one of the several auth methods.
            IAuthMethodInfo authMethod = new TokenAuthMethodInfo(vaultToken);

            // Initialize settings. You can also set proxies, custom delegates etc. here.
            var clientSettings = new VaultClientSettings(serverUrl, authMethod);
            _mountPoint = mountPoint;
            _storePath = (!string.IsNullOrEmpty(storePath) && !storePath.StartsWith("/")) ? "/" + storePath.Trim() : storePath?.Trim();
            _vaultClient = new VaultClient(clientSettings);
            _subfolderInventory = SubfolderInventory;
            _storeType = storeType?.Split('.')[1];
        }

        public async Task CreateCertStore()
        {
            logger.MethodEntry();
            try
            {
                if (_storeType != StoreType.HCVKVPEM)
                {
                    await CreateFileStore();
                    return;
                }
                // for PEM stores, the store path is the container name, not entry name as with file stores

                await CreatePemStore();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error when adding the new certificate.");
                throw;
            }
            logger.MethodExit();
        }

        private async Task CreateFileStore()
        {
            IFileStore fileStore;
            var parentPath = _storePath.Substring(0, _storePath.LastIndexOf("/"));
            logger.LogTrace($"parent path = {parentPath}");
            var entryName = _storePath.Substring(_storePath.LastIndexOf("/"));
            entryName = entryName.TrimStart('/');

            switch (_storeType)
            {
                case StoreType.HCVKVPFX:
                    fileStore = new PfxFileStore();
                    break;

                case StoreType.HCVKVPKCS12:
                    fileStore = new Pkcs12FileStore();
                    break;

                case StoreType.KCVKVJKS:
                    fileStore = new JksFileStore();
                    break;

                default:
                    throw new InvalidOperationException($"unrecognized store type value {_storeType}");
            }

            logger.LogTrace("generating a random string for the new store password.");
            var passphrase = CertUtility.GenerateRandomString(16);

            logger.LogTrace("Creating the new filestore with the generated passphrase.");
            var newStoreBytes = fileStore.CreateFileStore(passphrase);

            logger.LogTrace("Writing the passphrase and store file to the location in the store path.");

            try
            {
                VaultClient.V1.Auth.ResetVaultToken();

                var newData = new Dictionary<string, object> { { entryName, Convert.ToBase64String(newStoreBytes) }, { "passphrase", passphrase } };

                await VaultClient.V1.Secrets.KeyValue.V2.WriteSecretAsync(parentPath, newData, null, _mountPoint);
            }
            catch (Exception ex)
            {
                logger.LogError("Error writing cert to Vault", ex);
                throw;
            }

        }
        private async Task CreatePemStore()
        {
            //without a certificate, the only thing to do is create the secret path in Vault with empty values
            var newData = new Dictionary<string, object> { { "certificate", string.Empty }, { "private_key", string.Empty } };

            try
            {
                if (_mountPoint == null)
                {
                    await VaultClient.V1.Secrets.KeyValue.V2.WriteSecretAsync(_storePath, newData);
                }
                else
                {
                    await VaultClient.V1.Secrets.KeyValue.V2.WriteSecretAsync(_storePath, newData, mountPoint: _mountPoint);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error creating the PEM certificate store at path {_storePath}");
                throw;
            }
        }

        public async Task<CurrentInventoryItem> GetCertificateFromPemStore(string key)
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
                    res = await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretAsync(fullPath, mountPoint: _mountPoint);
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
                if (certData.TryGetValue(StoreFileExtensions.HCVKVPEM, out object publicKeyObj))
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
                    certs[i] = certs[i].Trim() + certFooter;
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
                logger.LogError(ex, "Error parsing certificate data");
                throw;
            }
        }

        public async Task<List<string>> GetVaults(string storePath)
        {
            logger.MethodEntry();
            // there are 4 store types that use the KV secrets engine.  HCVKVPEM uses the folder as the store path.  The others (KCVKVJKS,HCVKVPKCS12,HCVKVPFX) use the full file path.
            string suffix = "";
            storePath = storePath ?? _storePath;

            if (!storePath.StartsWith("/")) storePath = "/" + storePath;
            if (!storePath.EndsWith("/")) storePath = storePath + "/";

            logger.LogTrace($"starting search in path: {storePath}");
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
                    suffix = "certificate"; //PEM store
                    break;
            }

            try
            {
                logger.LogTrace("sending request to Vault.");
                var res = await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretPathsAsync(storePath, _mountPoint);
                entryPaths = res.Data.Keys.ToList();
                logger.LogTrace($"paths to check: ");
                entryPaths.ForEach(ep => { logger.LogTrace(ep); });
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);
                throw;
            }

            logger.LogTrace($"checking paths at this level. paths = {string.Join(", ", entryPaths)}");
            for (var i = 0; i < entryPaths.Count(); i++)
            {
                var path = entryPaths[i];
                if (!path.EndsWith("/"))
                { // it is a secret, not a folder
                  // get the sub-keys for the secret entry

                    IDictionary<string, object> keys;
                    try
                    {
                        var res = await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretSubkeysAsync(storePath + path, mountPoint: _mountPoint);
                        keys = res.Data.Subkeys;

                        // does it have an entry with the suffix we are looking for?
                        var key = keys.FirstOrDefault(k => k.Key.EndsWith(suffix));
                        if (key.Key != null)
                        {
                            if (_storeType == StoreType.HCVKVPEM)
                            {
                                // PEM stores paths are the folder/container name rather than the entry name.  
                                vaultPaths.Add(storePath);
                            }
                            else
                            {
                                vaultPaths.Add(storePath + path + "/" + key.Key);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError($"Error reading secret keys. {ex.Message}", ex);
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
            try
            {
                if (_storeType != StoreType.HCVKVPEM)
                {
                    await PutCertificateIntoFileStore(certName, contents, pfxPassword, includeChain);
                    return;
                }
                // for PEM stores, the store path is the container name, not entry name as with file stores

                await PutCertificateIntoPemStore(certName, contents, pfxPassword, includeChain);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error when adding the new certificate.");
                throw;
            }
            logger.MethodExit();
        }
        private async Task PutCertificateIntoPemStore(string certName, string contents, string pfxPassword, bool includeChain)
        {
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

            var pubCertPem = CertUtility.Pemify(Convert.ToBase64String(pubCert));

            logger.LogTrace("adding the chain certs");

            var pemChain = new List<string>();
            var chain = p.GetCertificateChain(alias).ToList();

            chain.ForEach(c =>
            {
                var cert = c.Certificate.GetEncoded();
                var encoded = CertUtility.Pemify(Convert.ToBase64String(cert));
                pemChain.Add(encoded);
            });

            try
            {
                certDict.Add("private_key", privateKeyString);

                // certDict.Add("revocation_time", 0);

                if (includeChain)
                {

                    certDict.Add(StoreFileExtensions.HCVKVPEM, String.Join("\n", pemChain));
                }
                else
                {
                    certDict.Add(StoreFileExtensions.HCVKVPEM, pubCertPem);
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
                VaultClient.V1.Auth.ResetVaultToken();

                var fullPath = _storePath + certName;

                await VaultClient.V1.Secrets.KeyValue.V2.WriteSecretAsync(fullPath, certDict, mountPoint: _mountPoint);
            }
            catch (Exception ex)
            {
                logger.LogError("Error writing cert to Vault", ex);
                throw;
            }
            logger.MethodExit();
        }

        private async Task PutCertificateIntoFileStore(string certName, string contents, string pfxPassword, bool includeChain)
        {
            logger.MethodEntry();

            IFileStore fileStore;
            var parentPath = _storePath.Substring(0, _storePath.LastIndexOf("/"));
            logger.LogTrace($"parent path = {parentPath}");
            Secret<SecretData> res;
            Dictionary<string, object> certData;

            switch (_storeType)
            {
                case StoreType.HCVKVPFX:
                    fileStore = new PfxFileStore();
                    break;

                case StoreType.HCVKVPKCS12:
                    fileStore = new Pkcs12FileStore();
                    break;

                case StoreType.KCVKVJKS:
                    fileStore = new JksFileStore();
                    break;

                default:
                    throw new InvalidOperationException($"unrecognized store type value {_storeType}");
            }

            try
            {
                // first get entry contents and passphrase
                logger.LogTrace("getting all secrets in the parent container for the store.");

                res = await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretAsync(parentPath, mountPoint: _mountPoint);

                certData = (Dictionary<string, object>)res.Data.Data;
                logger.LogTrace("got secret data.", certData);

                string certificate = null;
                string passphrase = null;

                //Validates if the "certificate" and "private_key" keys exist in certData

                var key = _storePath.Substring(_storePath.LastIndexOf("/"));
                key = key.TrimStart('/');

                logger.LogTrace($"getting the contents of {key}");

                if (!certData.TryGetValue(key, out object certFileObj))
                {
                    throw new DirectoryNotFoundException($"entry named {key} not found at {parentPath}");
                }
                certificate = certFileObj.ToString();

                if (!certData.TryGetValue("passphrase", out object passphraseObj))
                {
                    throw new DirectoryNotFoundException($"no passphrase entry found at {parentPath}");
                }
                passphrase = passphraseObj.ToString();

                logger.LogTrace("got passphrase and certificate store secrets from vault.");

                logger.LogTrace("calling method to add certificate to store file.");
                // get new store entry
                var newEntry = fileStore.AddCertificate(certName, pfxPassword, contents, includeChain, certificate, passphrase);
                logger.LogTrace("got new store file.");
                // write new store entry
                try
                {
                    logger.LogTrace("writing file store with new certificate to vault.");
                    VaultClient.V1.Auth.ResetVaultToken();

                    var newData = new Dictionary<string, object> { { key, newEntry } };
                    var patchReq = new PatchSecretDataRequest() { Data = newData };

                    await VaultClient.V1.Secrets.KeyValue.V2.PatchSecretAsync(parentPath, patchReq, _mountPoint);
                }
                catch (Exception ex)
                {
                    logger.LogError("Error writing cert to Vault", ex);
                    throw;
                }

            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error adding certificate to {_storeType}: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> RemoveCertificate(string certName)
        {
            logger.MethodEntry();
            try
            {
                if (_storeType != StoreType.HCVKVPEM)
                {
                    await RemoveCertificateFromFileStore(certName);
                    return true;
                }
                // for PEM stores, the store path is the container name, not entry name as with file stores

                await RemoveCertificateFromPemStore(certName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error when removing the certificate with alias {certName}.");
                throw;
            }
            logger.MethodExit();
            return true;
        }

        public async Task RemoveCertificateFromFileStore(string certName)
        {
            logger.MethodEntry();

            IFileStore fileStore;
            var parentPath = _storePath.Substring(0, _storePath.LastIndexOf("/"));
            logger.LogTrace($"parent path = {parentPath}");
            Secret<SecretData> res;
            Dictionary<string, object> certData;

            switch (_storeType)
            {
                case StoreType.HCVKVPFX:
                    fileStore = new PfxFileStore();
                    break;

                case StoreType.HCVKVPKCS12:
                    fileStore = new Pkcs12FileStore();
                    break;

                case StoreType.KCVKVJKS:
                    fileStore = new JksFileStore();
                    break;

                default:
                    throw new InvalidOperationException($"unrecognized store type value {_storeType}");
            }

            try
            {
                // first get entry contents and passphrase
                logger.LogTrace("getting all secrets in the parent container for the store.");

                res = await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretAsync(parentPath, mountPoint: _mountPoint);

                certData = (Dictionary<string, object>)res.Data.Data;
                logger.LogTrace("got secret data.", certData);

                string certStoreContents = null;
                string passphrase = null;

                //Validates if the "certificate" and "private_key" keys exist in certData

                var key = _storePath.Substring(_storePath.LastIndexOf("/"));
                key = key.TrimStart('/');

                logger.LogTrace($"getting the contents of {key}");

                if (!certData.TryGetValue(key, out object certFileObj))
                {
                    throw new DirectoryNotFoundException($"entry named {key} not found at {parentPath}");
                }
                certStoreContents = certFileObj.ToString();

                if (!certData.TryGetValue("passphrase", out object passphraseObj))
                {
                    throw new DirectoryNotFoundException($"no passphrase entry found at {parentPath}");
                }
                passphrase = passphraseObj.ToString();

                logger.LogTrace("got passphrase and certificate store secrets from vault.");

                logger.LogTrace("calling method to remove certificate from store file.");
                // get new store entry
                var newEntry = fileStore.RemoveCertificate(certName, passphrase, certStoreContents);
                logger.LogTrace("got new store file.");
                // write new store entry
                try
                {
                    logger.LogTrace("writing file store sans certificate to vault.");
                    VaultClient.V1.Auth.ResetVaultToken();

                    var newData = new Dictionary<string, object> { { key, newEntry } };
                    var patchReq = new PatchSecretDataRequest() { Data = newData };
                    await VaultClient.V1.Secrets.KeyValue.V2.PatchSecretAsync(parentPath, patchReq, _mountPoint);
                }
                catch (Exception ex)
                {
                    logger.LogError("Error writing file to Vault", ex);
                    throw;
                }

            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error removing certificate {certName} from {_storeType}: {ex.Message}");
                throw;
            }
        }

        public async Task RemoveCertificateFromPemStore(string certName)
        {
            VaultClient.V1.Auth.ResetVaultToken();

            try
            {
                var fullPath = _storePath + certName;
                await VaultClient.V1.Secrets.KeyValue.V2.DeleteSecretAsync(fullPath, _mountPoint);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error removing cert from Vault");
                throw;
            }
        }

        public async Task<IEnumerable<CurrentInventoryItem>> GetCertificates()
        {
            if (_storeType != StoreType.HCVKVPEM)
            {
                return await GetCertificatesFromFileStore();
            }
            // for PEM stores, the store path is the container name, not entry name as with file stores

            return await GetCertificatesFromPemStore();
        }

        private async Task<IEnumerable<CurrentInventoryItem>> GetCertificatesFromPemStore()
        {
            logger.MethodEntry();

            VaultClient.V1.Auth.ResetVaultToken();
            List<string> subPaths = new List<string>();
            var certs = new List<CurrentInventoryItem>();
            var entryNames = new List<string>();

            //Grabs the list of subpaths to get certificates from, if SubFolder Inventory is turned on.
            //Otherwise just define the single path _storePath
            logger.LogDebug($"SubInventoryEnabled: {_subfolderInventory}");

            if (_subfolderInventory == true)
            {
                logger.LogTrace("getting all sub-paths for container");
                subPaths = await GetSubPaths(_storePath);
                subPaths.Add(_storePath);
            }
            else
            {
                subPaths.Add(_storePath);
            }

            logger.LogTrace($"got all subpaths for container {_storePath}");
            logger.LogTrace($"subPaths = {string.Join(", ", subPaths)}");


            foreach (var path in subPaths)
            {
                logger.LogTrace($"checking for entries at {path}");
                var relative_path = path.Substring(_storePath.Length);

                try
                {
                    entryNames = (await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretPathsAsync(path, mountPoint: _mountPoint)).Data.Keys.ToList();
                    entryNames.RemoveAll(en => en.EndsWith("/"));

                    logger.LogTrace($"got entry names in {path}, {string.Join(", ", entryNames)}");
                    entryNames.ForEach(k =>
                    {
                        logger.LogTrace($"calling getCertificateFromPemStore, passing path: {relative_path}{k}");
                        var cert = GetCertificateFromPemStore($"{relative_path}{k}").Result;
                        if (cert != null) certs.Add(cert);

                    });
                }
                catch (Exception ex)
                {
                    logger.LogError($"error getting PEM certificate from {relative_path}, exception message = {ex.Message}");
                    throw ex;
                }
            }
            return certs;
        }

        public async Task<IEnumerable<CurrentInventoryItem>> GetCertificatesFromFileStore()
        {
            Secret<SecretData> res;

            //file stores for JKS, PKCS12 and PFX will have a "passphrase" entry on the same level by convention.  We'll need this in order to extract the certificates for inventory.
            var pos = _storePath.LastIndexOf("/");
            var parentPath = _storePath.Substring(0, pos);

            try
            {
                res = (await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretAsync(parentPath, mountPoint: _mountPoint));
            }
            catch (Exception ex)
            {
                logger.LogError($"Error getting certificate data from {parentPath}", ex);
                return null;
            }

            var certFields = (Dictionary<string, object>)res.Data.Data;

            IFileStore fileStore;
            switch (_storeType)
            {
                case StoreType.HCVKVPFX:
                    fileStore = new PfxFileStore();
                    break;

                case StoreType.HCVKVPKCS12:
                    fileStore = new Pkcs12FileStore();
                    break;

                case StoreType.KCVKVJKS:
                    fileStore = new JksFileStore();
                    break;

                default:
                    throw new InvalidOperationException($"unrecognized store type value {_storeType}");
            }

            try
            {
                return fileStore.GetInventory(certFields);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error performing inventory on store type {_storeType}: {ex.Message}");
                throw;
            }
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
                    if (path.EndsWith("/"))
                    {
                        string fullPath = $"{storagePath}{path}";
                        componentPaths.Add(fullPath);

                        List<string> subPaths = await GetSubPaths(fullPath);
                        componentPaths.AddRange(subPaths);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Error while listing component paths: {ex}");
            }
            logger.MethodExit();
            return componentPaths;
        }
    }
}