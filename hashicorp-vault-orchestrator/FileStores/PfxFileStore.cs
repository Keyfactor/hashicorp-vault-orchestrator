
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
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Extensions;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault.FileStores
{
    public class PfxFileStore : FileStoreBase, IFileStore
    {
        public PfxFileStore()
        {
            logger = LogHandler.GetClassLogger<PfxFileStore>();
        }

        public byte[] CreateFileStore(string password)
        {
            Pkcs12StoreBuilder storeBuilder = new Pkcs12StoreBuilder();
            Pkcs12Store newStore = storeBuilder.Build();
            using var outstream = new MemoryStream();
            logger.LogDebug("Created new PFX store, saving it to outStream");
            newStore.Save(outstream, password.ToCharArray(), new SecureRandom());
            return outstream.ToArray();
        }

        public string AddCertificate(string alias, string pfxPassword, string entryContents, bool includeChain, string storeFileContent, string passphrase)
        {
            logger.MethodEntry();

            logger.LogTrace("converting base64 encoded PFX store to binary.");
            var pfxBytes = Convert.FromBase64String(storeFileContent);
            var newCertBytes = Convert.FromBase64String(entryContents);

            logger.LogTrace("adding the new certificate, and getting the new PFX store bytes.");
            var newPFXbytes = AddOrRemoveCert(alias, pfxPassword, newCertBytes, pfxBytes, passphrase);

            return Convert.ToBase64String(newPFXbytes);
        }
        public string RemoveCertificate(string alias, string passphrase, string storeFileContent)
        {
            logger.MethodEntry();
            logger.LogTrace("converting base64 encoded PFX store to binary.");
            var pfxStoreBytes = Convert.FromBase64String(storeFileContent);

            logger.LogTrace("removing the certificate, and getting the new PFX store bytes.");
            var newPfxStoreBytes = AddOrRemoveCert(alias, null, null, pfxStoreBytes, passphrase, true);

            return Convert.ToBase64String(newPfxStoreBytes);
        }

        public IEnumerable<CurrentInventoryItem> GetInventory(string base64encodedCert, string passphrase)
        {
            logger.MethodEntry();

            var certs = new List<CurrentInventoryItem>();

            var pfxBytes = Convert.FromBase64String(base64encodedCert);

            Pkcs12Store p;

            using (var pfxBytesMemoryStream = new MemoryStream(pfxBytes))
            {
                logger.LogTrace("creating pkcs12 store for working with the certificate.");
                Pkcs12StoreBuilder storeBuilder = new Pkcs12StoreBuilder();
                p = storeBuilder.Build();
                p.Load(pfxBytesMemoryStream, passphrase.ToCharArray());
            }

            certs = CertUtility.CurrentInventoryFromPkcs12(p);
            logger.MethodExit();
            return certs;
        }

        private byte[] AddOrRemoveCert(string alias, string newCertPassword, byte[] newCertBytes, byte[] existingStore, string existingStorePassword, bool remove = false)
        {
            logger.MethodEntry();

            Pkcs12Store existingPfxStore = null;

            if (existingStore == null)
            {
                throw new DirectoryNotFoundException("An existing PFX certificate store was not found.");
            }

            logger.LogDebug("Loading existing PFX store from binary data.");

            try
            {
                using var pfxBytesMemoryStream = new MemoryStream(existingStore);
                logger.LogTrace("creating pkcs12 store for working with the certificate.");
                var sb = new Pkcs12StoreBuilder();
                existingPfxStore = sb.Build();
                existingPfxStore.Load(pfxBytesMemoryStream, existingStorePassword.ToCharArray());
            }
            catch (Exception ex)
            {
                logger.LogError($"error loading existing PFX store: {ex.Message}");
            }

            if (existingPfxStore.ContainsAlias(alias))
            {
                // If alias exists, delete it from existingJksStore
                logger.LogDebug($"alias '{alias}' exists in existing PFX store, deleting it");
                existingPfxStore.DeleteEntry(alias);
                if (remove)
                {
                    // If remove is true, save existingJksStore and return
                    logger.LogDebug("this is a removal operation, saving existing PFX store");
                    using var mms = new MemoryStream();
                    existingPfxStore.Save(mms,
                                          string.IsNullOrEmpty(existingStorePassword) ? Array.Empty<char>() : existingStorePassword.ToCharArray(), new SecureRandom());
                    logger.LogDebug("returning existing PFX store");
                    return mms.ToArray();
                }
            }
            else if (remove)
            {
                // If alias does not exist and remove is true, return existingStore
                logger.LogDebug($"alias '{alias}' does not exist in existing PFX store and this is a removal operation, returning existing PFX store as-is");
                using var mms = new MemoryStream();
                existingPfxStore.Save(mms, string.IsNullOrEmpty(existingStorePassword) ? Array.Empty<char>() : existingStorePassword.ToCharArray(), new SecureRandom());
                return mms.ToArray();
            }

            // adding the new certificate

            // Create new Pkcs12Store from newPkcs12Bytes
            var storeBuilder = new Pkcs12StoreBuilder();
            var newCert = storeBuilder.Build();

            try
            {
                logger.LogDebug("Loading new certificate as pfx/pkcs12 from newPkcs12Bytes");
                using var pkcs12Ms = new MemoryStream(newCertBytes);
                newCert.Load(pkcs12Ms, string.IsNullOrEmpty(newCertPassword) ? Array.Empty<char>() : newCertPassword.ToCharArray());
            }
            catch (Exception)
            {
                logger.LogDebug("Loading new Pkcs12Store from newPkcs12Bytes failed, trying to load as X509Certificate");
                var certificateParser = new X509CertificateParser();
                var certificate = certificateParser.ReadCertificate(newCertBytes);

                logger.LogDebug("Creating new Pkcs12Store from certificate");
                // create new Pkcs12Store from certificate
                storeBuilder = new Pkcs12StoreBuilder();
                newCert = storeBuilder.Build();
                logger.LogDebug($"Setting certificate entry in new Pkcs12Store as alias '{alias}'");
                newCert.SetCertificateEntry(alias, new X509CertificateEntry(certificate));
            }

            // Iterate through newCert aliases.
            logger.LogDebug("Iterating through new Pkcs12Store aliases");
            foreach (var al in newCert.Aliases)
            {
                logger.LogTrace($"Alias: {al}");
                if (newCert.IsKeyEntry(al))
                {
                    logger.LogDebug($"alias '{al}' is a key entry, getting key entry and certificate chain");
                    var keyEntry = newCert.GetKey(al);
                    logger.LogDebug($"getting certificate chain for alias '{al}'");
                    var certificateChain = newCert.GetCertificateChain(al);

                    logger.LogDebug("creating certificate list from certificate chain");
                    var certificates = certificateChain.ToList();

                    // If createdNewStore is false, add to existingJksStore
                    // check if alias exists in existingJksStore
                    if (existingPfxStore.ContainsAlias(alias))
                    {
                        // If alias exists, delete it from existingJksStore
                        logger.LogDebug($"alias '{al}' exists in existing PFX store, deleting it");
                        existingPfxStore.DeleteEntry(al);
                    }

                    logger.LogDebug($"setting key entry for alias '{alias}'");
                    existingPfxStore.SetKeyEntry(alias,
                        keyEntry,
                        certificates.ToArray());
                }
                else
                {
                    logger.LogDebug($"setting certificate with alias '{al}' for existing PFX store");
                    existingPfxStore.SetCertificateEntry(al, newCert.GetCertificate(al));
                }
            }

            using var outStream = new MemoryStream();
            logger.LogDebug("Saving existing PFX store to outStream");
            existingPfxStore.Save(outStream, string.IsNullOrEmpty(existingStorePassword) ? Array.Empty<char>() : existingStorePassword.ToCharArray(), new SecureRandom());

            logger.LogDebug("Returning updated PFX store as byte[]");
            return outStream.ToArray();
        }
    }
}
