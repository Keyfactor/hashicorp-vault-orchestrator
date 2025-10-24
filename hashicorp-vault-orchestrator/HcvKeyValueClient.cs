
//  Copyright 2025 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
using VaultSharp.Core;
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

        private string _certPath { get; set; }
        private string _passphrasePath { get; set; }
        private string _certStorePropName { get; set; }
        private string _passphrasePropName { get; set; }
        private string _mountPoint { get; set; }
        private bool _subfolderInventory { get; set; }
        private string _storeType { get; set; }
        private string _namespace { get; set; }

        public HcvKeyValueClient(string vaultToken, string serverUrl, string mountPoint, string ns, string storeType, string certPath, string certPropName, string passphrasePath, string passphrasePropName, bool SubfolderInventory = false)
        {
            // Initialize one of the several auth methods.
            IAuthMethodInfo authMethod = new TokenAuthMethodInfo(vaultToken);
            _namespace = ns;

            // Initialize settings. You can also set proxies, custom delegates etc. here.
            var clientSettings = new VaultClientSettings(serverUrl, authMethod) { Namespace = _namespace, UseVaultTokenHeaderInsteadOfAuthorizationHeader = true };

            _vaultClient = new VaultClient(clientSettings);
            _mountPoint = mountPoint;
            _certPath = (!string.IsNullOrEmpty(certPath) && !certPath.StartsWith("/")) ? "/" + certPath.Trim() : certPath?.Trim();
            _passphrasePath = (!string.IsNullOrEmpty(passphrasePath) && !passphrasePath.StartsWith("/")) ? "/" + passphrasePath.Trim() : passphrasePath?.Trim();
            _certStorePropName = certPropName;
            _passphrasePropName = passphrasePropName;
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
            logger.MethodEntry();

            IFileStore fileStore;

            (var certParentPath, var certSecretName, var passphraseParentPath, var passphraseSecretName) = GetSecretPaths();
                        
            var certSecretIsJSON = !string.IsNullOrEmpty(_certStorePropName);
            if (certSecretIsJSON) logger.LogTrace($"the certificate data will be stored as a JSON object with the base64 encoded cert stored in the property '{_certStorePropName}'");

            var passphraseSecretIsJSON = !string.IsNullOrEmpty(_passphrasePropName);
            if (passphraseSecretIsJSON) logger.LogTrace($"the passphrase secret will be stored as a JSON object with the passphrase in the property '{_passphrasePropName}'");

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

                // create the cert secret
                Dictionary<string, object> newData;
                object certSecretContent;


                // the content will be either the base64 encoded cert, or a json object with a property containing the base64encoded cert
                if (certSecretIsJSON)
                {
                    // this means the cert should be stored as a JSON object with property _certStorePropName, as opposed to a raw base64 string.
                    certSecretContent = new Dictionary<string, object> { { _certStorePropName, Convert.ToBase64String(newStoreBytes) } };
                }
                else
                {
                    certSecretContent = Convert.ToBase64String(newStoreBytes);
                }

                newData = new Dictionary<string, object> { { certSecretName, certSecretContent } };

                // write the certificate secret
                await VaultClient.V1.Secrets.KeyValue.V2.WriteSecretAsync(certParentPath, newData, null, _mountPoint);


                // create the passphrase secret

                object passphraseSecretContent;

                if (passphraseSecretIsJSON)
                {
                    passphraseSecretContent = new Dictionary<string, object> { { _passphrasePropName, passphrase } };
                }
                else
                {
                    passphraseSecretContent = passphrase;
                }

                // write the passphrase secret
                await VaultClient.V1.Secrets.KeyValue.V2.WriteSecretAsync(passphraseParentPath, new Dictionary<string, object> { { passphraseSecretName, passphraseSecretContent } });

            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error writing cert to Vault: {ex.Message}");
                throw;
            }

        }
        private async Task CreatePemStore()
        {
            logger.MethodEntry();

            //without a certificate, the only thing to do is create the secret path in Vault with empty values
            var newData = new Dictionary<string, object> { { "certificate", string.Empty }, { "private_key", string.Empty } };

            try
            {
                if (_mountPoint == null)
                {
                    await VaultClient.V1.Secrets.KeyValue.V2.WriteSecretAsync(_certPath, newData);
                }
                else
                {
                    await VaultClient.V1.Secrets.KeyValue.V2.WriteSecretAsync(_certPath, newData, mountPoint: _mountPoint);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error creating the PEM certificate store at path {_certPath}");
                throw;
            }
        }

        public async Task<CurrentInventoryItem> GetCertificateFromPemStore(string key)
        {
            logger.MethodEntry();
            VaultClient.V1.Auth.ResetVaultToken();

            Dictionary<string, object> certData = new Dictionary<string, object>();
            Secret<SecretData> res;
            var fullPath = _certPath + key;


            try
            {
                res = await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretAsync(fullPath, mountPoint: _mountPoint);
                certData = (Dictionary<string, object>)res.Data.Data;
            }
            catch (VaultApiException ex)
            {
                if (ex.StatusCode == 404)
                {
                    logger.LogWarning($"No secret values exist at `{_mountPoint + fullPath}`. Vault returned '404'.  Has it been deleted?");
                    throw new PemException($"No secret values exist at {_mountPoint + fullPath}. Vault returned '404'.  Has it been deleted?", ex);
                }
                else
                {
                    logger.LogError(ex, $"Error reading PEM store certificate at {fullPath}.  Exception message: `{ex.Message}`");
                    throw new PemException($"Error reading PEM store certificate at {fullPath}.  Exception message: `{ex.Message}`", ex);
                }
            }

            try
            {
                string certificate = null;
                string privateKey = null;

                //Validates if the "certificate" and "private_key" keys exist in certFileObj
                if (certData.TryGetValue(StoreFileExtensions.HCVKVPEM, out object publicKeyObj))
                {
                    certificate = publicKeyObj.ToString();
                }

                var certs = new List<string>();

                if (certData.TryGetValue("private_key", out object privateKeyObj))
                {
                    privateKey = privateKeyObj.ToString();
                }

                // if either field is missing, don't include it in inventory

                if (string.IsNullOrEmpty(certificate) || string.IsNullOrEmpty(privateKey))
                {
                    if (!string.IsNullOrEmpty(certificate) || !string.IsNullOrEmpty(privateKey)) // logging cases where it has one, but not the other.
                    {
                        var missing = string.IsNullOrEmpty(certificate) ? StoreFileExtensions.HCVKVPEM : "private_key";
                        var exists = string.IsNullOrEmpty(certificate) ? "private_key" : StoreFileExtensions.HCVKVPEM;

                        logger.LogWarning($"The secret entry located at `{fullPath}` is missing `{missing}` but has `{exists}`.  Inventory will continue.");
                        throw new PemException($"The secret entry located at `{fullPath}` is missing `{missing}` but has `{exists}`");
                    }

                    return null;
                }

                //split the chain entries (if chain is included)
                logger.LogTrace("splitting the entries in the PEM certificate file.");

                certs = certificate.Split(new string[] { CertificateHeaders.PEM_FOOTER }, StringSplitOptions.RemoveEmptyEntries).ToList();

                for (int i = 0; i < certs.Count(); i++)
                {
                    certs[i] = certs[i].Trim() + CertificateHeaders.PEM_FOOTER;
                }

                logger.LogTrace($"Found {certs.Count()} certificates in the entry.");

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
                    logger.LogTrace($"No valid certificate data found in {fullPath}.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error parsing certificate data for PEM store certificate located at {fullPath}.  Exception message: `{ex.Message}`");
                throw;
            }
        }

        public async Task<(List<string>, List<string>)> GetVaults(string storePath)
        {
            logger.MethodEntry();

            // there are 4 store types that use the KV secrets engine.  HCVKVPEM uses the folder as the store path.  The others (KCVKVJKS,HCVKVPKCS12,HCVKVPFX) use the full file path.

            storePath = storePath ?? _certPath;

            if (!storePath.StartsWith("/")) storePath = "/" + storePath;
            if (!storePath.EndsWith("/")) storePath = storePath + "/";

            string suffix = StoreFileExtensions.ForStoreType(_storeType);
            var vaultPaths = new List<string>();
            var entryPaths = new List<string>();
            var entries = new List<string>();
            var subPaths = new List<string>();
            var warnings = new List<string>();

            logger.LogTrace($"starting vault discovery search in path: {_mountPoint + storePath}");
            try
            {
                var res = await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretPathsAsync(storePath, _mountPoint);
                entryPaths = res.Data.Keys.ToList();
                entries = entryPaths.Where(e => !e.EndsWith("/")).ToList();
                subPaths = entryPaths.Where(e => e.EndsWith("/")).ToList();

                logger.LogTrace($"Will check contents of these paths for secret keys ending with `{suffix}`: {string.Join(", ", entries)}");
            }
            catch (VaultApiException ex)
            {
                logger.LogTrace($"caught exception reading the child paths at {storePath} with mount point {_mountPoint}, exception type = {ex.GetType().Name} inner type = {ex.InnerException?.GetType().Name}. \n exception message: {ex.Message}\n inner exception message: {ex.InnerException?.Message}\nlogging a warning and continuing with inventory.");
                var warning = $"Error reading entry names at {storePath}\nStatus code: {ex.StatusCode}\n";
                if (ex.ApiErrors != null) warning += string.Join("\n", ex.ApiErrors);
                warnings.Add(warning);
                //throw;
            }

            for (var i = 0; i < entries.Count(); i++)
            {
                var path = entries[i];

                // get the sub-keys for the secret entry

                IDictionary<string, object> keys;
                try
                {
                    logger.LogTrace($"Making request to vault to read secret sub-keys at path: {storePath + path} and mountPoint: {_mountPoint}.");
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
                catch (VaultApiException ex)
                {
                    var warning = $"Error reading secret keys at {storePath + path} with mount point {_mountPoint} {(!string.IsNullOrEmpty(_namespace) ? $"and namespace {_namespace}" : "")}:\nStatus code: {ex.StatusCode}\n";
                    if (ex.ApiErrors != null) warning += string.Join("\n", ex.ApiErrors);
                    logger.LogWarning(warning);
                    warnings.Add(warning);
                }
            }
            for (var i = 0; i < subPaths.Count(); i++)
            {
                var path = subPaths[i];
                (var childStores, var childWarnings) = await GetVaults(storePath + path);
                vaultPaths.AddRange(childStores);
                warnings.AddRange(childWarnings);
            }
            vaultPaths = vaultPaths.Distinct().ToList();

            return (vaultPaths, warnings);
        }


        public async Task PutCertificate(string certName, string contents, string pfxPassword, string certPath, string certPropName, string keyPath, string keyPropName, bool includeChain)
        {
            logger.MethodEntry();
            try
            {
                if (_storeType != StoreType.HCVKVPEM)
                {
                    await PutCertificateIntoFileStore(certName, contents, pfxPassword, certPath, certPropName, keyPath, keyPropName, includeChain);
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

            // Extract private secretName
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
                    if (KeyEntry == null) throw new Exception("Unable to retrieve private secretName");

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
                logger.LogError(ex, $"Error parsing certificate content: {ex.Message}");
                throw;
            }
            try
            {
                logger.LogTrace("writing secret to vault.");
                VaultClient.V1.Auth.ResetVaultToken();

                var fullPath = _certPath + certName;

                await VaultClient.V1.Secrets.KeyValue.V2.WriteSecretAsync(fullPath, certDict, mountPoint: _mountPoint);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error writing cert to Vault: {ex.Message}");
                throw;
            }
            logger.MethodExit();
        }

        private async Task PutCertificateIntoFileStore(string newCertName, string contents, string pfxPassword, string certPath, string certPropName, string keyPath, string keyPropName, bool includeChain)
        {
            logger.MethodEntry();

            IFileStore fileStore;

            (var certParentPath, var certSecretName, var passphraseParentPath, var passphraseSecretName) = GetSecretPaths();

            var certSecretIsJSON = !string.IsNullOrEmpty(_certStorePropName);
            if (certSecretIsJSON) logger.LogTrace($"the certificate data will be stored as a JSON object with the base64 encoded cert stored in the property '{_certStorePropName}'");

            var passphraseSecretIsJSON = !string.IsNullOrEmpty(_passphrasePropName);
            if (passphraseSecretIsJSON) logger.LogTrace($"the passphrase secret will be stored as a JSON object with the passphrase in the property '{_passphrasePropName}'");


            Secret<SecretData> res;
            Dictionary<string, object> certFileObj;

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
                // first get cert contents
                logger.LogTrace("getting all secrets in the parent container for the store.");

                res = await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretAsync(certPath, mountPoint: _mountPoint);

                certFileObj = (Dictionary<string, object>)res.Data.Data;                
                logger.LogTrace($"got cert secret data..");

                if (certFileObj == null || certFileObj.Keys.Count == 0) {
                    logger.LogError($"no secret content was found at path {certPath}");
                    throw new DirectoryNotFoundException($"entry named {certSecretName} not found at {certParentPath} or is empty.");
                }

                foreach (var key in certFileObj.Keys) {
                    logger.LogTrace($"key = {key}, value = {certFileObj[key]}");
                }

                string certificate = null;
                string passphrase = null;

                // Validates if the certificate secret exists

                logger.LogTrace($"getting the contents of {certSecretName}");
                              

                if (certSecretIsJSON)
                {
                    var prop = certFileObj.GetType().GetProperty(certPropName);
                    certificate = prop.GetValue(certFileObj, null).ToString();
                }
                else
                {
                    certificate = certFileObj.ToString();
                }

                res = await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretAsync(passphraseParentPath, mountPoint: _mountPoint);
                
                // Validates that the passphrase secret exists 


                if (!certFileObj.TryGetValue("passphrase", out object passphraseObj))
                {
                    throw new DirectoryNotFoundException($"no passphrase entry found at {passphraseParentPath}");
                }
                passphrase = passphraseObj.ToString();

                logger.LogTrace("got passphrase and certificate store secrets from vault.");

                logger.LogTrace("calling method to add certificate to store file.");
                // get new store entry
                var newEntry = fileStore.AddCertificate(newCertName, pfxPassword, contents, includeChain, certificate, passphrase);
                logger.LogTrace("got new store file.");
                
                // write new store entry
                try
                {
                    //logger.LogTrace("writing file store with new certificate to vault.");
                    //VaultClient.V1.Auth.ResetVaultToken();

                    //var newData = new Dictionary<string, object> { { key, newEntry } };
                    //var patchReq = new PatchSecretDataRequest() { Data = newData };
                    //logger.LogTrace($"patching {key} to path {certParentPath} at mount point {_mountPoint}");
                    //await VaultClient.V1.Secrets.KeyValue.V2.PatchSecretAsync(certParentPath, patchReq, _mountPoint);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"Error writing cert to Vault: {ex.Message}");
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
            var parentPath = _certPath.Substring(0, _certPath.LastIndexOf("/"));
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
                logger.LogTrace("got secret data..");

                string certStoreContents = null;
                string passphrase = null;

                //Validates if the "certificate" and "private_key" keys exist in certFileObj

                var secretName = _certPath.Substring(_certPath.LastIndexOf("/"));
                secretName = secretName.TrimStart('/');

                logger.LogTrace($"getting the contents of {secretName}");

                if (!certData.TryGetValue(secretName, out object certFileObj))
                {
                    throw new DirectoryNotFoundException($"entry named {secretName} not found at {parentPath}");
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

                    var newData = new Dictionary<string, object> { { secretName, newEntry } };
                    var patchReq = new PatchSecretDataRequest() { Data = newData };
                    await VaultClient.V1.Secrets.KeyValue.V2.PatchSecretAsync(parentPath, patchReq, _mountPoint);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"Error writing file to Vault: {ex.Message}");
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
                var fullPath = _certPath + certName;
                await VaultClient.V1.Secrets.KeyValue.V2.DeleteSecretAsync(fullPath, _mountPoint);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error removing cert from Vault: {ex.Message}");
                throw;
            }
        }

        public async Task<(List<CurrentInventoryItem>, List<string>)> GetCertificates()
        {
            if (_storeType != StoreType.HCVKVPEM)
            {
                return await GetCertificatesFromFileStore();
            }

            // for PEM stores, the store path is the container name, not entry name as with file stores
            return await GetCertificatesFromPemStore();
        }

        private async Task<(List<CurrentInventoryItem>, List<string>)> GetCertificatesFromPemStore()
        {
            logger.MethodEntry();

            VaultClient.V1.Auth.ResetVaultToken();
            List<string> subPaths = new List<string>();
            var certs = new List<CurrentInventoryItem>();
            var entryNames = new List<string>();
            List<string> inventoryExceptions = new List<string>();

            //Grabs the list of subpaths to get certificates from, if SubFolder Inventory is turned on.
            //Otherwise just define the single path _certPath
            logger.LogDebug($"SubInventoryEnabled: {_subfolderInventory}");

            if (_subfolderInventory == true)
            {
                logger.LogTrace("getting all sub-paths for container");
                subPaths = await GetSubPaths(_certPath);
                subPaths.Add(_certPath);
            }
            else
            {
                subPaths.Add(_certPath);
            }

            logger.LogTrace($"got all subpaths for container {_certPath}");
            logger.LogTrace($"subPaths = {string.Join(", ", subPaths)}");


            foreach (var path in subPaths)
            {
                logger.LogTrace($"checking for entries at {path}");
                var relative_path = path.Substring(_certPath.Length);

                try
                {
                    entryNames = (await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretPathsAsync(path, mountPoint: _mountPoint)).Data.Keys.ToList();
                    entryNames.RemoveAll(en => en.EndsWith("/"));
                }
                catch (Exception ex)
                {
                    logger.LogTrace($"caught exception reading the child paths at {_mountPoint + path}, exception type = {ex.GetType().Name} inner type = {ex.InnerException?.GetType().Name}. \n exception message: {ex.Message}\n inner exception message: {ex.InnerException?.Message}\nlogging a warning and continuing with inventory.");
                    var warning = $"Error reading entry names at {_mountPoint + path}:\n";
                    warning += string.Join("\n", (ex as VaultApiException).ApiErrors);
                    logger.LogWarning(ex, warning);
                    inventoryExceptions.Add(warning);
                    // continuing on exception during inventory
                }

                logger.LogTrace($"got entry names in {path}, {string.Join(", ", entryNames)}");
                entryNames.ForEach(k =>
                {
                    logger.LogTrace($"calling getCertificateFromPemStore, passing path: {relative_path}{k}");
                    try
                    {
                        var cert = GetCertificateFromPemStore($"{relative_path}{k}").Result;
                        if (cert != null) certs.Add(cert);
                    }
                    catch (Exception ex)
                    {
                        if (ex.InnerException?.GetType() != typeof(PemException)) throw;
                        // if type is PemException, we continue and log a warning.

                        inventoryExceptions.Add(ex.InnerException.Message);
                    }
                });
            }

            return (certs, inventoryExceptions);
        }

        public async Task<(List<CurrentInventoryItem>, List<string>)> GetCertificatesFromFileStore()
        {
            Secret<SecretData> res;

            //file stores for JKS, PKCS12 and PFX will have a "passphrase" entry on the same level by convention.  We'll need this in order to extract the certificates for inventory.
            var pos = _certPath.LastIndexOf("/");
            var parentPath = _certPath.Substring(0, pos);
            logger.LogTrace($"reading secrets at path {parentPath}, which should include the secretName and certificate for {_certPath}");

            try
            {
                res = (await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretAsync(parentPath, mountPoint: _mountPoint));
            }
            catch (Exception ex)
            {
                var warning = $"Error getting {_storeType} certificate data from {parentPath}.  Exception message: {ex.Message}";
                logger.LogError(ex, warning);
                return (null, new List<string> { warning });
            }

            var certFields = (Dictionary<string, object>)res.Data.Data;

            logger.LogTrace("retrieved the following entries:");
            certFields.Keys?.ToList()?.ForEach(key =>
            {
                logger.LogTrace($"secretName: `{key}`, value: {certFields[key].ToString().Length} character long string (value hidden).");
            });

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
                return (fileStore.GetInventory(certFields).ToList(), null);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error performing inventory on {_certPath}: {ex.Message}");
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
                logger.LogTrace($"getting secret and path entries at this level: {storagePath}");

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
                logger.LogWarning(ex, $"Error while listing component paths: {ex.Message}");
            }
            logger.MethodExit();
            return componentPaths;
        }

        private (string, string, string, string) GetSecretPaths()
        {
            var certParentPath = _certPath.Substring(0, _certPath.LastIndexOf("/"));

            // if a seperate passphrase path is not provided, we use the same parent path as the certificate to store the passphrase.
            var passphraseParentPath = string.IsNullOrEmpty(_passphrasePath) ? certParentPath : _passphrasePath?[.._passphrasePath.LastIndexOf('/')];

            logger.LogTrace($"cert parent path = {certParentPath}");
            logger.LogTrace($"passphrase parent path = {passphraseParentPath}");

            var certSecretName = _certPath.Substring(_certPath.LastIndexOf('/')).TrimStart('/');
            var passphraseSecretName = string.IsNullOrEmpty(_passphrasePath) ? StoreFileExtensions.PASSPHRASE : _passphrasePath[_passphrasePath.LastIndexOf('/')..];

            logger.LogTrace($"cert secret name = {certSecretName}");
            logger.LogTrace($"passphrase secret name = {passphraseSecretName}");

            return (certParentPath, certSecretName, passphraseParentPath, passphraseSecretName);
        }
    }
}