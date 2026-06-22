
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
using System.Text;
using System.Threading.Tasks;
using Keyfactor.Extensions.Orchestrator.HashicorpVault.FileStores;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
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

        protected IVaultClient VaultClient
        {
            get => _vaultClient;
            set => _vaultClient = value; // settable for unit-test subclass injection
        }

        private ILogger logger = LogHandler.GetClassLogger<HcvKeyValueClient>();

        private string _certPath { get; set; }
        private string _passphrasePath { get; set; }
        private string _certPropName { get; set; }
        private string _passphrasePropName { get; set; }
        private string _mountPoint { get; set; }
        private bool _subfolderInventory { get; set; }
        protected string _storeType { get; set; }
        private string _namespace { get; set; }
        private int _kvVersionCache { get; set; }

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
            _certPropName = certPropName;
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
                logger.LogError($"Error when adding the new certificate: {LogHandler.FlattenException(ex)}");
                throw;
            }
            logger.MethodExit();
        }

        private async Task CreateFileStore()
        {
            logger.MethodEntry();

            IFileStore fileStore;

            (var certParentPath, var certSecretName, var passphraseParentPath, var passphraseSecretName) = ParsedSecretPaths();

            var certSecretIsJSON = !string.IsNullOrEmpty(_certPropName);
            if (certSecretIsJSON) logger.LogTrace($"the certificate data will be stored as a JSON object with the base64 encoded cert stored in the property '{_certPropName}'");
            else logger.LogTrace($"the certificate data will be stored as the entire secret content at '{certParentPath}/{certSecretName}' and contain the base64 encoded cert.");
            var passphraseSecretIsJSON = !string.IsNullOrEmpty(_passphrasePropName);
            if (passphraseSecretIsJSON) logger.LogTrace($"the passphrase secret will be stored as a JSON object with the passphrase in the property '{_passphrasePropName}'");
            else logger.LogTrace($"the passphrase string will be stored as the entire secret content at '{passphraseParentPath}/{passphraseSecretName}'");
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
                var kvVersion = await GetKVVersionAsync();
                if (!certSecretIsJSON && kvVersion == 1)
                {
                    _certPropName = "value"; // kv v1 secrets are _always_ stored as JSON; setting generic "value" property
                    certSecretIsJSON = true;
                }

                // create the cert secret                
                Dictionary<string, object> certSecretContent;
                var pathToWriteCert = string.Empty;


                // the content will be either the base64 encoded cert, or a json object with a property containing the base64encoded cert
                if (certSecretIsJSON)
                {
                    // this means the cert should be stored as a JSON object with property _certPropName, as opposed to a raw base64 string.
                    certSecretContent = new Dictionary<string, object> { { _certPropName, Convert.ToBase64String(newStoreBytes) } }; // the content includes the property name
                    pathToWriteCert = $"{certParentPath}/{certSecretName}"; // we write to the secret
                }
                else
                {
                    certSecretContent = new Dictionary<string, object> { { certSecretName, Convert.ToBase64String(newStoreBytes) } }; // the content includes the secret name..
                    pathToWriteCert = $"{certParentPath}/{certSecretName}"; // we write to the full secret path
                }

                logger.LogTrace($"we will send the request to write the cert secret at the path {pathToWriteCert}, keyed by the secret or property name: '{certSecretContent.Keys.First()}'");

                // write the certificate secret

                logger.LogTrace($"sending request to write new cert store secret");

                await WriteSecretAutoAsync(pathToWriteCert, certSecretContent, _mountPoint);

                logger.LogTrace($"request to write certificate secret was successful");

                // create the passphrase secret

                if (!passphraseSecretIsJSON && kvVersion == 1)
                {
                    _passphrasePropName = "value"; // kv v1 secrets are _always_ stored as JSON; setting generic "value" property
                    passphraseSecretIsJSON = true;
                }

                Dictionary<string, object> passphraseSecretContent;
                var pathToWritePassphrase = string.Empty;

                if (passphraseSecretIsJSON)
                {
                    passphraseSecretContent = new Dictionary<string, object> { { _passphrasePropName, passphrase } };
                    pathToWritePassphrase = $"{passphraseParentPath}/{passphraseSecretName}";
                }
                else
                {
                    passphraseSecretContent = new Dictionary<string, object> { { passphraseSecretName, passphrase } };
                    pathToWritePassphrase = $"{passphraseParentPath}/{passphraseSecretName}"; // we write to the full secret path
                }

                logger.LogTrace($"we will send the request to write the passphrase secret at the path {pathToWritePassphrase}, keyed by the secret or property name: '{passphraseSecretContent.Keys.First()}'");

                // write the passphrase secret — use Write (not Patch) for a brand-new secret; Patch on a non-existent KV v2 path returns 404

                logger.LogTrace($"sending request to write new cert store passphrase");

                if (passphraseSecretIsJSON)
                {
                    await PatchSecretAutoAsync(pathToWritePassphrase, passphraseSecretContent, _mountPoint);
                }
                else
                {
                    await WriteSecretAutoAsync(pathToWritePassphrase, passphraseSecretContent, _mountPoint);
                }

                logger.LogTrace($"request to write passphrase secret was successful");

            }
            catch (Exception ex)
            {
                logger.LogError($"Error writing cert to Vault: {LogHandler.FlattenException(ex)}");
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
                await WriteSecretAutoAsync(_certPath, newData, _mountPoint);
            }
            catch (Exception ex)
            {
                logger.LogError($"Error creating the PEM certificate store at path {_certPath}: {LogHandler.FlattenException(ex)}");
                throw;
            }
        }

        public async Task<CurrentInventoryItem> GetCertificateFromPemStore(string key)
        {
            logger.MethodEntry();

            Dictionary<string, object> certData = new Dictionary<string, object>();

            var fullPath = _certPath + key;

            try
            {
                certData = await ReadSecretAutoAsync(fullPath, _mountPoint);
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

            var entries = new List<string>();
            var subPaths = new List<string>();
            var warnings = new List<string>();

            logger.LogTrace($"starting vault discovery search in path: {_mountPoint + storePath}");
            try
            {
                //var res = await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretPathsAsync(storePath, _mountPoint);
                var paths = await ReadSecretPathsAutoAsync(storePath, _mountPoint);

                entries = paths.Where(e => !e.EndsWith("/")).ToList();
                subPaths = paths.Where(e => e.EndsWith("/")).ToList();

                logger.LogTrace($"Will check contents of these paths for secret keys ending with `{suffix}`: {string.Join(", ", entries)}");
            }
            catch (VaultApiException ex)
            {
                logger.LogTrace($"caught exception reading the child paths at {storePath} with mount point {_mountPoint}, exception type = {ex.GetType().Name} inner type = {ex.InnerException?.GetType().Name}. \n exception message: {ex.Message}\n inner exception message: {ex.InnerException?.Message}\nlogging a warning and continuing with inventory.");
                var warning = $"Error reading entry names at {storePath}\nStatus code: {ex.StatusCode}\n";
                if (ex.ApiErrors != null) warning += string.Join("\n", ex.ApiErrors);
                warnings.Add(warning);
                //we continue searching rather than throw on individual error(s)
            }

            for (var i = 0; i < entries.Count(); i++)
            {
                var path = entries[i];

                // get the sub-keys for the secret entry

                try
                {
                    var fullPath = storePath + path;
                    logger.LogTrace($"Making request to vault to read secret sub-keys at path: {fullPath} and mountPoint: {_mountPoint}.");
                    var res = await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretSubkeysAsync(storePath + path, mountPoint: _mountPoint);

                    var keys = await ReadSecretSubKeysAutoAsync(fullPath, _mountPoint);


                    // does it have an entry with the suffix we are looking for?
                    var key = keys.FirstOrDefault(k => k.EndsWith(suffix));
                    if (key != null)
                    {
                        if (_storeType == StoreType.HCVKVPEM)
                        {
                            // PEM stores paths are the folder/container name rather than the entry name.  
                            vaultPaths.Add(storePath);
                        }
                        else
                        {
                            vaultPaths.Add(fullPath + "/" + key);
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
                    await PutCertificateIntoFileStore(certName, contents, pfxPassword, includeChain);
                    return;
                }
                // for PEM stores, the store path is the container name, not entry name as with file stores

                await PutCertificateIntoPemStore(certName, contents, pfxPassword, includeChain);
            }
            catch (Exception ex)
            {
                logger.LogError($"An error occurred when attempting to add the new certificate: {LogHandler.FlattenException(ex)}");
                throw;
            }
            logger.MethodExit();
        }

        private async Task PutCertificateIntoPemStore(string certName, string contents, string pfxPassword, bool includeChain)
        {
            logger.MethodEntry();

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

                var fullPath = _certPath + certName;

                await WriteSecretAutoAsync(fullPath, certDict, _mountPoint);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error writing cert to Vault: {ex.Message}");
                throw;
            }
            logger.MethodExit();
        }

        private async Task PutCertificateIntoFileStore(string newCertName, string contents, string pfxPassword, bool includeChain)
        {
            logger.MethodEntry();

            IFileStore fileStore;

            (var certParentPath, var certSecretName, var passphraseParentPath, var passphraseSecretName) = ParsedSecretPaths();

            (var certificate, var passphrase) = await GetCertificateAndPassphrase();

            // Create-if-not-exist: when neither the cert blob nor the passphrase exists yet
            // in Vault, the file-format store has never been seeded. Rather than failing the
            // Management-Add, seed an empty store + random passphrase so the first Add can
            // proceed exactly as the second one would. This mirrors how HCVKVPEM behaves
            // (CreatePemStore writes an empty PEM secret as part of CreateCertStore) and lets
            // Management-Add succeed even when the explicit "Create" op was skipped or has
            // never run.
            if (string.IsNullOrEmpty(certificate) && string.IsNullOrEmpty(passphrase))
            {
                logger.LogTrace("No existing store/passphrase found at the configured path — seeding an empty file store with a fresh passphrase before adding the new cert (create-if-not-exist).");
                await CreateFileStore();
                (certificate, passphrase) = await GetCertificateAndPassphrase();
                if (string.IsNullOrEmpty(certificate) || string.IsNullOrEmpty(passphrase))
                {
                    throw new InvalidOperationException(
                        $"Auto-create of file-format store at {certParentPath}/{certSecretName} did not produce a readable store + passphrase pair. Check Vault token policy permits create + read on '{_mountPoint}/{certParentPath}'.");
                }
            }
            else if (string.IsNullOrEmpty(passphrase))
            {
                throw new DirectoryNotFoundException(
                    $"Existing store found at {certParentPath}/{certSecretName} but no passphrase at the configured path. Refusing to overwrite — set PassphrasePath correctly or delete the orphaned store.");
            }

            var certSecretIsJSON = !string.IsNullOrEmpty(_certPropName);

            if (certSecretIsJSON) logger.LogTrace($"the certificate data will be stored at '{_certPath}' as a JSON object with the base64 encoded cert stored in the property '{_certPropName}'");
            else logger.LogTrace($"the certificate secret will be stored at '{_certPath}' with the contents being the base64 encoded certificate.");

            var passphraseSecretIsJSON = !string.IsNullOrEmpty(_passphrasePropName);

            if (passphraseSecretIsJSON) logger.LogTrace($"the passphrase secret will be stored at '{passphraseParentPath}/{passphraseSecretName}' as a JSON object with the passphrase in the property '{_passphrasePropName}'");
            else logger.LogTrace($"the passphrase secret will be stored at '{passphraseParentPath}/{passphraseSecretName}' as a string containing the passphrase for the certificate store");

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
                logger.LogTrace("got passphrase and certificate store secrets from vault.");
                logger.LogTrace("calling method to add certificate to store file.");

                // get new store entry
                var newCertFileStore = fileStore.AddCertificate(newCertName, pfxPassword, contents, includeChain, certificate, passphrase);

                logger.LogTrace("got new store file.");

                // write new store entry
                try
                {
                    logger.LogTrace("writing file store with new certificate to vault.");

                    // if the certificate and/or passphrase is stored as a property in a JSON secret..
                    // then we need to write the full path to the secret, and pass a dictionary of the object for the PATCH operation

                    // if the cert or passphrase is the full contents of the secret.. 
                    // then we need to write to the _parent_ path, a dictionary with a key of the secret name and value of the contents

                    // first write the certificate
                    var newCertSecretData = new Dictionary<string, object>();
                    var newPassphraseSecretData = new Dictionary<string, object>();
                    var certPathToWrite = string.Empty;
                    var passphrasePathToWrite = string.Empty;

                    logger.LogTrace($"creating the patch request for the certificate secret...");
                    if (certSecretIsJSON)
                    {
                        // we will create a dictionary to represent the secret itself..
                        newCertSecretData = new Dictionary<string, object> { { _certPropName, newCertFileStore } };

                        // and write it to the full path of the secret
                        certPathToWrite = certParentPath + "/" + certSecretName;
                    }
                    else
                    {
                        // we will create a dictionary to represent the contents of the secret
                        newCertSecretData = new Dictionary<string, object> { { certSecretName, newCertFileStore } };

                        // write to the full secret path (not the parent)
                        certPathToWrite = $"{certParentPath}/{certSecretName}";
                    }

                    logger.LogTrace($"writing {newCertSecretData.Keys.First()} to path {certPathToWrite} at mount point {_mountPoint}");

                    if (certSecretIsJSON)
                    {
                        // JSON mode: PATCH to merge the cert property into an existing shared secret
                        await PatchSecretAutoAsync(certPathToWrite, newCertSecretData, _mountPoint);
                    }
                    else
                    {
                        // non-JSON mode: WRITE (create-or-replace) — the cert is the entire secret; PATCH would 404 if the secret does not yet exist
                        await WriteSecretAutoAsync(certPathToWrite, newCertSecretData, _mountPoint);
                    }

                    logger.LogTrace("The certificate and passphrase have been successfully written to Vault.");

                    // since this is an existing store, no update needs to be made to the passphrase
                }
                catch (Exception ex)
                {
                    logger.LogError($"An error occurred when attempting  to Vault: {ex.Message}");
                    logger.LogError($"{LogHandler.FlattenException(ex)}");
                    throw;
                }

            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"An error occurred when trying to update the secret for {_storeType}: {ex.Message}");
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
            logger.MethodEntry();
            Secret<SecretData> res = null;
            string certStore = string.Empty;
            string passphrase = string.Empty;

            try
            {
                (certStore, passphrase) = await GetCertificateAndPassphrase();
            }
            catch (Exception ex)
            {
                var warning = $"Vault returned an error when attempting to read the secret from {_certPath}.  Exception message: {ex.Message}";
                logger.LogError(LogHandler.FlattenException(ex));
                res?.Warnings?.ForEach(w => logger.LogTrace(w));
                return (null, new List<string> { warning });
            }

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
                return (fileStore.GetInventory(certStore, passphrase).ToList(), null);
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

            List<string> componentPaths = new List<string> { };
            try
            {
                logger.LogTrace($"getting secret and path entries at this level: {storagePath}");

                var paths = await ReadSecretPathsAutoAsync(storagePath, _mountPoint);

                foreach (var path in paths)
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
                logger.LogWarning($"An error occurred when attempting to read the paths: {LogHandler.FlattenException(ex)}");
                throw;
            }
            logger.MethodExit();
            return componentPaths;
        }

        private (string, string, string, string) ParsedSecretPaths()
        {
            logger.MethodEntry();
            logger.LogTrace("extracting the JSON property names from the secret paths..");
            var certParentPath = _certPath.Substring(0, _certPath.LastIndexOf("/"));

            // if a seperate passphrase path is not provided, we use the same parent path as the certificate to store the passphrase.
            var passphraseParentPath = string.IsNullOrEmpty(_passphrasePath) ? certParentPath : _passphrasePath?[.._passphrasePath.LastIndexOf('/')];

            logger.LogTrace($"cert parent path = {certParentPath}");
            logger.LogTrace($"passphrase parent path = {passphraseParentPath}");

            var certSecretName = _certPath.Substring(_certPath.LastIndexOf('/')).TrimStart('/');
            certSecretName = certSecretName.Split('?')[0]; // we want the name of the secret without the optional property name parameter
            var passphraseSecretName = string.IsNullOrEmpty(_passphrasePath) ? StoreFileExtensions.PASSPHRASE : _passphrasePath[_passphrasePath.LastIndexOf('/')..];
            passphraseSecretName = passphraseSecretName.Split('?')[0].TrimStart('/'); // we want the name of the secret without the optional property name parameter
            logger.LogTrace($"cert secret name = {certSecretName}");
            logger.LogTrace($"passphrase secret name = {passphraseSecretName}");

            return (certParentPath, certSecretName, passphraseParentPath, passphraseSecretName);
        }

        private async Task<(string, string)> GetCertificateAndPassphrase()
        {
            (var certParentPath, var certSecretName, var passphraseParentPath, var passphraseSecretName) = ParsedSecretPaths();
            var certSecretIsJSON = !string.IsNullOrEmpty(_certPropName);
            var passphraseSecretIsJSON = !string.IsNullOrEmpty(_passphrasePropName);

            string certContent = string.Empty;
            string passphrase = string.Empty;

            var kvVersion = await GetKVVersionAsync();
            if (kvVersion == 1)
            {
                if (!certSecretIsJSON) { _certPropName = "value"; certSecretIsJSON = true; }
                if (!passphraseSecretIsJSON) { _passphrasePropName = "value"; passphraseSecretIsJSON = true; }
            }

            // Read existing cert — may not exist for a fresh/empty store; that is OK.
            try
            {
                var secretPath = certParentPath + "/" + certSecretName;
                logger.LogTrace($"retreiving cert from {secretPath} on mount {_mountPoint}");
                var certFileObj = await ReadSecretAutoAsync(secretPath, _mountPoint);

                if (certFileObj != null && certFileObj.Keys.Count > 0)
                {
                    certContent = certSecretIsJSON
                        ? certFileObj[_certPropName]?.ToString()
                        : certFileObj.First().Value?.ToString();
                    logger.LogTrace($"retrieved existing cert of length {certContent?.Length ?? 0}");
                }
            }
            catch (VaultApiException ex)
            {
                // Use a plain catch + StatusCode test rather than the `when`
                // filter — the filter has been observed not to fire reliably for
                // exceptions raised from inside the async state machine on
                // .NET 10 + VaultSharp 1.17, sending the flow into the generic
                // `catch (Exception)` branch even for 404s.
                if (ex.StatusCode == 404 || ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    logger.LogTrace($"No existing certificate at {certParentPath}/{certSecretName} — treating as empty store for Management-Add.");
                }
                else
                {
                    logger.LogError($"Error reading certificate (status={ex.StatusCode}): {LogHandler.FlattenException(ex)}");
                    throw;
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"Error reading certificate: {LogHandler.FlattenException(ex)}");
                throw;
            }

            // Read passphrase — may not exist for a fresh/empty store. Callers detect
            // a missing passphrase (empty string) together with a missing certificate
            // (empty certContent) as the create-if-not-exist signal.
            var passphraseReadPath = !string.IsNullOrEmpty(_passphrasePath)
                ? _passphrasePath
                : passphraseParentPath + "/" + passphraseSecretName;
            try
            {
                logger.LogTrace($"retreiving passphrase from {passphraseReadPath}");
                var passphraseObj = await ReadSecretAutoAsync(passphraseReadPath, _mountPoint);

                if (passphraseObj != null && passphraseObj.Keys.Count > 0)
                {
                    passphrase = passphraseSecretIsJSON
                        ? passphraseObj[_passphrasePropName]?.ToString()
                        : passphraseObj.First().Value?.ToString();
                    logger.LogTrace($"retrieved passphrase of length {passphrase?.Length ?? 0}");
                }
            }
            catch (VaultApiException ex)
            {
                if (ex.StatusCode == 404 || ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    logger.LogTrace($"No existing passphrase at {passphraseReadPath} — treating as fresh store for create-if-not-exist.");
                }
                else
                {
                    logger.LogError($"Error reading passphrase (status={ex.StatusCode}): {LogHandler.FlattenException(ex)}");
                    throw;
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"Error reading passphrase: {LogHandler.FlattenException(ex)}");
                throw;
            }

            logger.LogTrace("successfully retreived the secrets..");
            return (certContent, passphrase);
        }

        public async Task<List<string>> GetTokenPoliciesAsync()
        {
            logger.MethodEntry();
            try
            {
                var tokenInfo = await VaultClient.V1.Auth.Token.LookupSelfAsync();
                return tokenInfo.Data.Policies;
            }
            catch (VaultApiException ex)
            {
                logger.LogError($"Status code: {ex.StatusCode}");
                logger.LogError($"Message: {ex.Message}");
                logger.LogError($"API Errors: {string.Join(", ", ex.ApiErrors ?? new List<string>())}");
                logger.LogError($"Help Link: {ex.HelpLink}");
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError($"There was an error attempting to retreive the active token policies: {LogHandler.FlattenException(ex)}");
                throw;
            }
        }

        public virtual async Task<int> GetKVVersionAsync()
        {
            if (_kvVersionCache > 0)
            {
                return _kvVersionCache;
            }
            logger.MethodEntry();
            logger.LogTrace("determining the Key-Value secrets engine version (v1 or v2)");
            try
            {
                // Get all mounted secrets engines
                var mounts = await _vaultClient.V1.System.GetSecretBackendsAsync();

                // Normalize mount point (add trailing slash if not present)
                var normalizedMount = _mountPoint.EndsWith("/") ? _mountPoint : $"{_mountPoint}/";
                logger.LogTrace($"got {mounts.Data.Count} secret engine mounts.. looking for {normalizedMount}");

                if (mounts.Data.TryGetValue(normalizedMount, out var mountConfig))
                {
                    logger.LogTrace($"found {normalizedMount}!");
                    logger.LogTrace($"serialized values: {JsonConvert.SerializeObject(mountConfig)}");
                    // Check the options for version info
                    if (mountConfig.Options != null &&
                        mountConfig.Options.TryGetValue("version", out var version))
                    {
                        var kvVersion = int.Parse(version.ToString());
                        _kvVersionCache = kvVersion;
                        logger.LogTrace($"using version {kvVersion} of the Key-Value secrets engine.");
                        return kvVersion;
                    }

                    // If no version in options, it's KV v1
                    _kvVersionCache = 1;
                    return 1;
                }

                throw new Exception($"Mount point '{_mountPoint}' not found");
            }
            catch (VaultApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                // The token does not have permission to list secret engine mounts (sys/mounts).
                // This is a non-fatal condition: we default to KV v2 and warn so the operator
                // can grant the permission or explicitly configure the mount point version.
                logger.LogWarning(
                    $"The Vault token does not have permission to read sys/mounts (HTTP 403). " +
                    $"Unable to auto-detect the KV secrets engine version for mount '{_mountPoint}'. " +
                    $"Defaulting to KV v2. To suppress this warning, grant the token 'read' access " +
                    $"to 'sys/mounts' or ensure the mount point is a KV v2 engine.");
                _kvVersionCache = 2;
                return 2;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to determine KV version for mount '{_mountPoint}': {ex.Message}", ex);
            }
        }



        /// <summary>
        /// Read a secret from KV engine, automatically detecting the version
        /// </summary>
        public virtual async Task<Dictionary<string, object>> ReadSecretAutoAsync(
            string path,
            string mountPoint)
        {
            logger.MethodEntry();
            try
            {
                var kvVersion = await GetKVVersionAsync();

                if (kvVersion == 2)
                {
                    logger.LogTrace($"making request to read secret at {mountPoint}{path}..");


                    var secret = await _vaultClient.V1.Secrets.KeyValue.V2.ReadSecretAsync(
                        path: path,
                        mountPoint: mountPoint
                    );

                    return secret.Data.Data as Dictionary<string, object>;
                }
                else // v1
                {
                    logger.LogTrace($"making request to read secret at {mountPoint}{path}..");

                    var secretv1 = await _vaultClient.V1.Secrets.KeyValue.V1.ReadSecretAsync(
                    path,
                    mountPoint: mountPoint);
                    logger.LogTrace($"response: {JsonConvert.SerializeObject(secretv1)}");
                    return secretv1.Data;
                }
            }
            catch (VaultApiException ex)
            {
                if (ex.StatusCode == 404)
                {
                    logger.LogError($"no secret was found at path '{path}' of the KV secrets engine mount point '{mountPoint}'.. The server returned 404");
                }
                else
                {
                    logger.LogError($"There was an error reading the secret with mountpoint = '{mountPoint}' and path = '{path}'");
                }

                logger.LogError($"Status code: {ex.StatusCode}");
                logger.LogError($"Message: {ex.Message}");
                logger.LogError($"API Errors: {string.Join(", ", ex.ApiErrors ?? new List<string>())}");
                logger.LogError($"API Warnings: {string.Join(", ", ex.ApiWarnings ?? new List<string>())}");
                logger.LogError($"Help Link: {ex.HelpLink}");
                logger.LogTrace($"full exception: {JsonConvert.SerializeObject(ex)}");
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError($"There was an error attempting to retreive the secret: {LogHandler.FlattenException(ex)}");
                throw;
            }
        }

        /// <summary>
        /// Write a secret to KV engine, automatically detecting the version
        /// </summary>
        public virtual async Task WriteSecretAutoAsync(
            string path,
            Dictionary<string, object> data,
            string mountPoint)
        {
            logger.MethodEntry();
            logger.LogTrace($"writing secret to path {mountPoint}/{path}");
            try
            {
                var kvVersion = await GetKVVersionAsync();

                if (kvVersion == 2)
                {
                    await _vaultClient.V1.Secrets.KeyValue.V2.WriteSecretAsync(
                        path: path,
                        data: data,
                        mountPoint: mountPoint
                    );
                }
                else // v1
                {
                    await _vaultClient.V1.Secrets.KeyValue.V1.WriteSecretAsync(
                        path: path,
                        values: data,
                        mountPoint: mountPoint
                    );
                }
            }
            catch (VaultApiException ex)
            {
                logger.LogError($"Status code: {ex.StatusCode}");
                logger.LogError($"Message: {ex.Message}");
                logger.LogError($"API Errors: {string.Join(", ", ex.ApiErrors ?? new List<string>())}");
                logger.LogError($"Help Link: {ex.HelpLink}");
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError($"There was an error attempting to write the secret: {LogHandler.FlattenException(ex)}");
                throw;
            }
        }

        /// <summary>
        /// Patch a secret (update specific keys without overwriting others)
        /// For KV v1, this does a read-modify-write operation
        /// For KV v2, this uses native patch support
        /// </summary>
        public virtual async Task PatchSecretAutoAsync(
            string path,
            Dictionary<string, object> keysToUpdate,
            string mountPoint)
        {
            try
            {
                var kvVersion = await GetKVVersionAsync();

                if (kvVersion == 2)
                {
                    // KV v2 requires PatchSecretDataRequest
                    var patchRequest = new PatchSecretDataRequest
                    {
                        Data = keysToUpdate
                    };

                    try
                    {
                        await _vaultClient.V1.Secrets.KeyValue.V2.PatchSecretAsync(
                            path: path,
                            patchSecretDataRequest: patchRequest,
                            mountPoint: mountPoint
                        );
                    }
                    catch (VaultApiException ex)
                    {
                        if (ex.StatusCode == 404 || ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
                        {
                            // KV v2 Patch returns 404 when the secret does not yet exist.
                            // Fall through to Write so the first call to Patch creates the
                            // secret with the supplied keys — same effective behavior,
                            // idempotent. Plain catch + StatusCode test rather than `when`
                            // filter (see GetCertificateAndPassphrase for rationale).
                            logger.LogTrace($"Patch at {mountPoint}/{path} returned 404; falling back to Write (create-if-not-exist).");
                            await WriteSecretAutoAsync(path, keysToUpdate, mountPoint);
                        }
                        else
                        {
                            throw;
                        }
                    }
                }
                else // v1
                {
                    Dictionary<string, object> existing = null;
                    // KV v1 requires read-modify-write
                    try
                    {
                        existing = await ReadSecretAutoAsync(path, mountPoint);
                    }
                    catch (VaultApiException ex) {
                        if (ex.StatusCode != 404) throw;
                        // if it's not found, that's ok.  we'll create a new secret
                    }
                    if (existing == null) existing = new Dictionary<string, object>();
                    // Merge with new data
                    foreach (var kvp in keysToUpdate)
                    {
                        existing[kvp.Key] = kvp.Value;
                    }

                    // Write back the merged data
                    await WriteSecretAutoAsync(path, existing, mountPoint);
                }
            }
            catch (VaultApiException ex)
            {
                logger.LogError($"Status code: {ex.StatusCode}");
                logger.LogError($"Message: {ex.Message}");
                logger.LogError($"API Errors: {string.Join(", ", ex.ApiErrors ?? new List<string>())}");
                logger.LogError($"Help Link: {ex.HelpLink}");
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError($"There was an error attempting to patch the secret: {LogHandler.FlattenException(ex)}");
                throw;
            }
        }

        /// <summary>
        /// Delete specific keys from a secret
        /// For both KV v1 and v2, this does a read-modify-write operation
        /// </summary>
        public async Task DeleteKeysFromSecretAsync(
            string path,
            IEnumerable<string> keysToDelete,
            string mountPoint)
        {
            logger.MethodEntry();
            logger.LogTrace($"deleting the keys {string.Join(',', keysToDelete)} from secret at path {mountPoint}/{path}");
            // Read existing data
            var existing = await ReadSecretAutoAsync(path, mountPoint);

            // Remove specified keys
            foreach (var key in keysToDelete)
            {
                existing.Remove(key);
            }

            // Write back the modified data
            await WriteSecretAutoAsync(path, existing, mountPoint);
        }

        /// <summary>
        /// Delete an entire secret
        /// </summary>
        public async Task DeleteSecretAutoAsync(
            string path,
            string mountPoint)
        {
            logger.MethodEntry();
            logger.LogTrace($"deleting the secret at {mountPoint}/{path}");
            try
            {
                var kvVersion = await GetKVVersionAsync();

                if (kvVersion == 2)
                {
                    await _vaultClient.V1.Secrets.KeyValue.V2.DeleteSecretAsync(
                        path: path,
                        mountPoint: mountPoint
                    );
                }
                else // v1
                {
                    await _vaultClient.V1.Secrets.KeyValue.V1.DeleteSecretAsync(
                        path: path,
                        mountPoint: mountPoint
                    );
                }
            }
            catch (VaultApiException ex)
            {
                logger.LogError($"Status code: {ex.StatusCode}");
                logger.LogError($"Message: {ex.Message}");
                logger.LogError($"API Errors: {string.Join(", ", ex.ApiErrors ?? new List<string>())}");
                logger.LogError($"Help Link: {ex.HelpLink}");
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError($"There was an error attempting to delete the secret: {LogHandler.FlattenException(ex)}");
                throw;
            }
        }

        /// <summary>
        /// List all secret paths at a given path
        /// </summary>
        public async Task<List<string>> ReadSecretPathsAutoAsync(
            string path,
            string mountPoint)
        {
            logger.MethodEntry();
            logger.LogTrace($"reading the secret paths under the root path of {mountPoint}/{path}");
            var kvVersion = await GetKVVersionAsync();

            Secret<ListInfo> result;
            try
            {
                if (kvVersion == 2)
                {
                    result = await _vaultClient.V1.Secrets.KeyValue.V2.ReadSecretPathsAsync(
                        path: path,
                        mountPoint: mountPoint
                    );
                }
                else // v1
                {
                    result = await _vaultClient.V1.Secrets.KeyValue.V1.ReadSecretPathsAsync(
                        path: path,
                        mountPoint: mountPoint
                    );
                }

                return result?.Data?.Keys?.ToList() ?? new List<string>();
            }
            catch (VaultApiException ex)
            {
                logger.LogError($"Status code: {ex.StatusCode}");
                logger.LogError($"Message: {ex.Message}");
                logger.LogError($"API Errors: {string.Join(", ", ex.ApiErrors ?? new List<string>())}");
                logger.LogError($"Help Link: {ex.HelpLink}");
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError($"There was an error attempting to read the paths: {LogHandler.FlattenException(ex)}");
                throw;
            }
        }

        /// <summary>
        /// List all secret paths at a given path
        /// </summary>
        public async Task<List<string>> ReadSecretSubKeysAutoAsync(
            string path,
            string mountPoint)
        {
            logger.MethodEntry();
            logger.LogTrace($"reading the secret subkeys from the secret at {mountPoint}/{path}");
            var kvVersion = await GetKVVersionAsync();

            List<string> result;
            try
            {
                if (kvVersion == 2)
                {
                    var res = await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretSubkeysAsync(path, mountPoint: _mountPoint);
                    result = res.Data?.Subkeys?.Keys?.ToList();
                }
                else // v1
                {
                    var res = await VaultClient.V1.Secrets.KeyValue.V1.ReadSecretAsync(path, _mountPoint);
                    result = res.Data?.Keys?.ToList();
                }

                return result;
            }
            catch (VaultApiException ex)
            {
                logger.LogError($"Status code: {ex.StatusCode}");
                logger.LogError($"Message: {ex.Message}");
                logger.LogError($"API Errors: {string.Join(", ", ex.ApiErrors ?? new List<string>())}");
                logger.LogError($"Help Link: {ex.HelpLink}");
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError($"There was an error attempting to read the sub-keys within the secret: {LogHandler.FlattenException(ex)}");
                throw;
            }
        }
    }
}