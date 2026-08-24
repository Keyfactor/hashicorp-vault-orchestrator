
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
using System.Text.Json;
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
        protected string _storeType { get; set; }
        private string _namespace { get; set; }
        private string _discoverySuffix { get; set; }
        private int _kvVersionCache { get; set; }

        public HcvKeyValueClient(string vaultToken, string serverUrl, string mountPoint, string ns, string storeType, string certPath, string certPropName, string passphrasePath, string passphrasePropName, string discoverySuffix = null)
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
            _storeType = storeType?.Split('.')[1];
            _discoverySuffix = !string.IsNullOrEmpty(discoverySuffix) ? discoverySuffix : StoreFileExtensions.ForStoreType(_storeType);
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

            (var certParentPath, var certSecretName, var passphraseParentPath, var passphraseSecretName) = ParsedSecretPaths();

            var certSecretIsJSON = !string.IsNullOrEmpty(_certPropName);
            var pathToWriteCert = $"{certParentPath}/{certSecretName}";
            var certSecretContent = certSecretIsJSON
                ? new Dictionary<string, object> { { _certPropName, string.Empty } }
                : new Dictionary<string, object> { { certSecretName, string.Empty } };

            try
            {
                logger.LogTrace($"seeding an empty certificate secret at {pathToWriteCert}");
                await WriteSecretAutoAsync(pathToWriteCert, certSecretContent, _mountPoint);

                // only seed a private key secret if PassphrasePath was configured — omitted means
                // this PEM store has no private key (e.g. a CA trust chain), not "use a default path".
                if (passphraseParentPath != null)
                {
                    var passphraseSecretIsJSON = !string.IsNullOrEmpty(_passphrasePropName);
                    var pathToWriteKey = $"{passphraseParentPath}/{passphraseSecretName}";
                    var keySecretContent = passphraseSecretIsJSON
                        ? new Dictionary<string, object> { { _passphrasePropName, string.Empty } }
                        : new Dictionary<string, object> { { passphraseSecretName, string.Empty } };

                    logger.LogTrace($"seeding an empty private key secret at {pathToWriteKey}");
                    await WriteSecretAutoAsync(pathToWriteKey, keySecretContent, _mountPoint);
                }
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

            string certificate;
            string privateKey;

            try
            {
                (certificate, privateKey) = await GetCertificateAndPassphrase();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error reading PEM store certificate at {_certPath}.  Exception message: `{ex.Message}`");
                throw new PemException($"Error reading PEM store certificate at {_certPath}.  Exception message: `{ex.Message}`", ex);
            }

            try
            {
                // a missing certificate means there's nothing to inventory for this store.
                // a missing private key is a normal, expected case now (e.g. a CA trust
                // chain with no key) — it is no longer treated as an error condition.
                if (string.IsNullOrEmpty(certificate))
                {
                    logger.LogTrace($"No certificate found at {_certPath}.");
                    return null;
                }

                //split the chain entries (if chain is included)
                logger.LogTrace("splitting the entries in the PEM certificate file.");

                var certs = certificate.Split(new string[] { CertificateHeaders.PEM_FOOTER }, StringSplitOptions.RemoveEmptyEntries).ToList();

                for (int i = 0; i < certs.Count(); i++)
                {
                    certs[i] = certs[i].Trim() + CertificateHeaders.PEM_FOOTER;
                }

                logger.LogTrace($"Found {certs.Count()} certificates in the entry.");

                if (certs.Count() > 0)
                {
                    var alias = !string.IsNullOrEmpty(key) ? key : _certPath.Split('?')[0].TrimEnd('/').Split('/').Last();

                    return new CurrentInventoryItem()
                    {
                        Alias = alias,
                        PrivateKeyEntry = !string.IsNullOrEmpty(privateKey),
                        ItemStatus = OrchestratorInventoryItemStatus.Unknown,
                        UseChainLevel = certs.Count() > 1,
                        Certificates = certs
                    };
                }
                else
                {
                    logger.LogTrace($"No valid certificate data found in {_certPath}.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error parsing certificate data for PEM store certificate located at {_certPath}.  Exception message: `{ex.Message}`");
                throw;
            }
        }

        public async Task<(List<string>, List<string>)> GetVaults(string storePath)
        {
            logger.MethodEntry();

            // there are 4 store types that use the KV secrets engine (HCVKVPEM, HCVKVJKS, HCVKVPKCS12,
            // HCVKVPFX) — all of them now identify a store by the full path to its secret, discovered by
            // matching a secret-key-name suffix (see _discoverySuffix, overridable via the DiscoverySuffix
            // job property; defaults per StoreFileExtensions.ForStoreType).

            storePath = storePath ?? _certPath;

            if (!storePath.StartsWith("/")) storePath = "/" + storePath;
            if (!storePath.EndsWith("/")) storePath = storePath + "/";

            string suffix = _discoverySuffix;
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

                    var keys = await ReadSecretSubKeysAutoAsync(fullPath, _mountPoint);


                    // does it have an entry with the suffix we are looking for?
                    var key = keys.FirstOrDefault(k => k.EndsWith(suffix));
                    if (key != null)
                    {
                        // `key` is a JSON property name inside the secret, not a further path
                        // segment. When it matches the secret's own name (the whole-secret
                        // convention, e.g. secret `mystore_pfx` whose only property is also named
                        // `mystore_pfx`), the discovered store path is just the secret itself.
                        // Otherwise `key` names a true JSON sub-property within a secret that may
                        // hold other data too, so the discovered path needs the `?propName` suffix
                        // to address it correctly.
                        vaultPaths.Add(string.Equals(key, path, StringComparison.Ordinal) ? fullPath : $"{fullPath}?{key}");
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

            var pfxBytes = Convert.FromBase64String(contents);
            Pkcs12Store p;

            using (var pfxBytesMemoryStream = new MemoryStream(pfxBytes))
            {
                Pkcs12StoreBuilder storeBuilder = new Pkcs12StoreBuilder();
                p = storeBuilder.Build();
                p.Load(pfxBytesMemoryStream, pfxPassword.ToCharArray());
            }

            string alias;
            string privateKeyString = null;

            var keyAlias = p.Aliases.Cast<string>().SingleOrDefault(a => p.IsKeyEntry(a));

            if (keyAlias != null)
            {
                alias = keyAlias;
                logger.LogTrace("Extracting Private Key...");
                using (var memoryStream = new MemoryStream())
                {
                    using (TextWriter streamWriter = new StreamWriter(memoryStream))
                    {
                        var pemWriter = new PemWriter(streamWriter);
                        var publicKey = p.GetCertificate(alias).Certificate.GetPublicKey();
                        var KeyEntry = p.GetKey(alias);
                        if (KeyEntry == null) throw new Exception("Unable to retrieve private secretName");

                        var privateKey = KeyEntry.Key;
                        var keyPair = new AsymmetricCipherKeyPair(publicKey, privateKey);

                        pemWriter.WriteObject(keyPair.Private);
                        streamWriter.Flush();
                        privateKeyString = Encoding.ASCII.GetString(memoryStream.GetBuffer()).Trim()
                            .Replace("\r", "").Replace("\0", "");

                        memoryStream.Close();
                        streamWriter.Close();
                        logger.LogTrace("Finished Extracting Private Key...");
                    }
                }
            }
            else
            {
                // no private key entry in the incoming content (e.g. a CA/chain-only certificate) —
                // fall back to a plain certificate alias so the cert can still be written.
                alias = p.Aliases.Cast<string>().SingleOrDefault(a => p.IsCertificateEntry(a));
                if (alias == null) throw new Exception("Unable to find a certificate entry in the supplied content.");
                logger.LogTrace("No private key entry found in the supplied content — adding a certificate-only PEM entry.");
            }

            if (!string.IsNullOrEmpty(privateKeyString) && string.IsNullOrEmpty(_passphrasePath))
            {
                throw new InvalidOperationException("The certificate being added includes a private key, but no PassphrasePath is configured on this PEM store to hold it. Configure PassphrasePath on the certificate store, or add a certificate-only entry.");
            }

            var pubCert = p.GetCertificate(alias).Certificate.GetEncoded();

            logger.LogTrace("converting to PEM format.");

            var pubCertPem = CertUtility.Pemify(Convert.ToBase64String(pubCert));

            string certPem;

            if (includeChain)
            {
                logger.LogTrace("adding the chain certs");

                var pemChain = new List<string>();
                var chain = p.GetCertificateChain(alias).ToList();

                chain.ForEach(c =>
                {
                    var cert = c.Certificate.GetEncoded();
                    var encoded = CertUtility.Pemify(Convert.ToBase64String(cert));
                    pemChain.Add(encoded);
                });
                certPem = string.Join("\n", pemChain);
            }
            else
            {
                certPem = pubCertPem;
            }

            (var certParentPath, var certSecretName, var passphraseParentPath, var passphraseSecretName) = ParsedSecretPaths();

            try
            {
                logger.LogTrace("writing certificate secret to vault.");

                var certSecretIsJSON = !string.IsNullOrEmpty(_certPropName);
                var certPathToWrite = $"{certParentPath}/{certSecretName}";
                var certSecretData = certSecretIsJSON
                    ? new Dictionary<string, object> { { _certPropName, certPem } }
                    : new Dictionary<string, object> { { certSecretName, certPem } };

                if (certSecretIsJSON)
                {
                    await PatchSecretAutoAsync(certPathToWrite, certSecretData, _mountPoint);
                }
                else
                {
                    await WriteSecretAutoAsync(certPathToWrite, certSecretData, _mountPoint);
                }

                if (!string.IsNullOrEmpty(privateKeyString))
                {
                    logger.LogTrace("writing private key secret to vault.");

                    var passphraseSecretIsJSON = !string.IsNullOrEmpty(_passphrasePropName);
                    var keyPathToWrite = $"{passphraseParentPath}/{passphraseSecretName}";
                    var keySecretData = passphraseSecretIsJSON
                        ? new Dictionary<string, object> { { _passphrasePropName, privateKeyString } }
                        : new Dictionary<string, object> { { passphraseSecretName, privateKeyString } };

                    if (passphraseSecretIsJSON)
                    {
                        await PatchSecretAutoAsync(keyPathToWrite, keySecretData, _mountPoint);
                    }
                    else
                    {
                        await WriteSecretAutoAsync(keyPathToWrite, keySecretData, _mountPoint);
                    }
                }
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
            (var certParentPath, var certSecretName, var passphraseParentPath, var passphraseSecretName) = ParsedSecretPaths();

            var certPath = $"{certParentPath}/{certSecretName}";

            try
            {
                logger.LogTrace($"deleting the certificate secret at {certPath}");
                await DeleteSecretAutoAsync(certPath, _mountPoint);

                if (passphraseParentPath != null)
                {
                    var keyPath = $"{passphraseParentPath}/{passphraseSecretName}";
                    if (!string.Equals(keyPath, certPath, StringComparison.OrdinalIgnoreCase))
                    {
                        logger.LogTrace($"deleting the private key secret at {keyPath}");
                        await DeleteSecretAutoAsync(keyPath, _mountPoint);
                    }
                }
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

            var certs = new List<CurrentInventoryItem>();
            var inventoryExceptions = new List<string>();

            try
            {
                var cert = await GetCertificateFromPemStore(null);
                if (cert != null) certs.Add(cert);
            }
            catch (PemException ex)
            {
                inventoryExceptions.Add(ex.Message);
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

        private (string, string, string, string) ParsedSecretPaths()
        {
            logger.MethodEntry();
            logger.LogTrace("extracting the JSON property names from the secret paths..");
            var certParentPath = _certPath.Substring(0, _certPath.LastIndexOf("/"));

            // Only HCVKVPFX/JKS/P12 fall back to a sibling "passphrase" secret when PassphrasePath
            // is omitted. HCVKVPEM/HCVPKI never populate a default here (see JobBase.InitProps) —
            // an omitted PassphrasePath for those means "no private key secret configured", not
            // "assume a sibling secret named 'passphrase'". Reflect that distinction here: a
            // genuinely-unset _passphrasePath (for those store types) must produce a null parent
            // path/secret name, not a phantom default.
            string passphraseParentPath = null;
            string passphraseSecretName = null;

            if (!string.IsNullOrEmpty(_passphrasePath))
            {
                passphraseParentPath = _passphrasePath[.._passphrasePath.LastIndexOf('/')];
                passphraseSecretName = _passphrasePath[_passphrasePath.LastIndexOf('/')..].Split('?')[0].TrimStart('/');
            }
            else if (_storeType != StoreType.HCVKVPEM && _storeType != StoreType.HCVPKI)
            {
                passphraseParentPath = certParentPath;
                passphraseSecretName = StoreFileExtensions.PASSPHRASE;
            }

            logger.LogTrace($"cert parent path = {certParentPath}");
            logger.LogTrace($"passphrase parent path = {passphraseParentPath}");

            var certSecretName = _certPath.Substring(_certPath.LastIndexOf('/')).TrimStart('/');
            certSecretName = certSecretName.Split('?')[0]; // we want the name of the secret without the optional property name parameter
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

            // No PassphrasePath configured for a store type that doesn't default one (HCVKVPEM/
            // HCVPKI, per ParsedSecretPaths) — there is no second secret to read. Return null
            // (not "") so callers can distinguish "not configured" from "configured but empty".
            if (passphraseParentPath == null)
            {
                logger.LogTrace("No PassphrasePath configured — skipping the private key/passphrase read.");
                return (certContent, null);
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
                    logger.LogTrace($"serialized values: {JsonSerializer.Serialize(mountConfig)}");
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
                    logger.LogTrace($"response: {JsonSerializer.Serialize(secretv1)}");
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
                logger.LogTrace($"full exception: {LogHandler.FlattenException(ex)}");
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
        public virtual async Task DeleteSecretAutoAsync(
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
        public virtual async Task<List<string>> ReadSecretPathsAutoAsync(
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
        public virtual async Task<List<string>> ReadSecretSubKeysAutoAsync(
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