
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
    public class Pkcs12FileStore : IFileStore
    {
        internal protected ILogger logger { get; set; }

        public Pkcs12FileStore()
        {
            logger = LogHandler.GetClassLogger<Pkcs12FileStore>();
        }

        public byte[] CreateFileStore(string password)
        {
            Pkcs12StoreBuilder storeBuilder = new Pkcs12StoreBuilder();
            Pkcs12Store newStore = storeBuilder.Build();
            using (var outstream = new MemoryStream())
            {
                logger.LogDebug("Created new PKCS12 store, saving it to outStream");
                newStore.Save(outstream, password.ToCharArray(), new SecureRandom());
                return outstream.ToArray();
            }
        }

        public string AddCertificate(string alias, string pfxPassword, string entryContents, bool includeChain, string storeFileContent, string passphrase)
        {
            logger.MethodEntry();

            logger.LogTrace("converting base64 encoded PKCS12 store to binary.");
            var pkcs12bytes = Convert.FromBase64String(storeFileContent);


            var newCertBytes = Convert.FromBase64String(entryContents);

            logger.LogTrace("adding the new certificate, and getting the new PKCS12 store bytes.");
            var newPkcs12Bytes = AddOrRemoveCert(alias, pfxPassword, newCertBytes, pkcs12bytes, passphrase);

            return Convert.ToBase64String(newPkcs12Bytes);
        }

        public string RemoveCertificate(string alias, string passphrase, string storeFileContent)
        {
            logger.MethodEntry();
            logger.LogTrace("converting base64 encoded PKCS12 store to binary.");
            var pkcs12StoreBytes = Convert.FromBase64String(storeFileContent);

            logger.LogTrace("removing the certificate, and getting the new PKCS12 store bytes.");
            var newPkcs12StoreBytes = AddOrRemoveCert(alias, null, null, pkcs12StoreBytes, passphrase, true);

            return Convert.ToBase64String(newPkcs12StoreBytes);
        }

        public IEnumerable<CurrentInventoryItem> GetInventory(string base64encodedCert, string passphrase)
        {
            logger.MethodEntry();
            // certFields should contain two entries.  The certificate with the "_pfx" suffix, and "passphrase"

            var certs = new List<CurrentInventoryItem>();

            try
            {
                // certFields should contain two entries.  The certificate with the "p12-contents" suffix, and "password"
                logger.LogTrace("converting base64 encoded cert to binary.");
                var bytes = Convert.FromBase64String(base64encodedCert);

                Pkcs12Store pkcs12Store;

                using (var stream = new MemoryStream(bytes))
                {
                    logger.LogTrace("creating pkcs12 store for working with the certificate.");
                    Pkcs12StoreBuilder storeBuilder = new Pkcs12StoreBuilder();
                    pkcs12Store = storeBuilder.Build();
                    pkcs12Store.Load(stream, passphrase.ToCharArray());
                }
                certs = CertUtility.CurrentInventoryFromPkcs12(pkcs12Store);
                logger.MethodExit();
                return certs;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unable to read PKCS12 file.");
                throw;
            }
        }

        private byte[] AddOrRemoveCert(string alias, string newCertPassword, byte[] newCertBytes, byte[] existingStore, string existingStorePassword, bool remove = false)
        {
            logger.MethodEntry();

            Pkcs12Store existingPkcs12Store = null;

            // If existingStore is not null, load it into existingPkcs12Store

            if (existingStore == null)
            {
                throw new DirectoryNotFoundException("An existing PKCS12 certificate store was not found.");
            }

            logger.LogDebug("Loading existing PKCS12 store from binary data.");

            try
            {
                using (var pfxBytesMemoryStream = new MemoryStream(existingStore))
                {
                    logger.LogTrace("creating pkcs12 store for working with the certificate.");
                    Pkcs12StoreBuilder sb = new Pkcs12StoreBuilder();
                    existingPkcs12Store = sb.Build();
                    existingPkcs12Store.Load(pfxBytesMemoryStream, existingStorePassword.ToCharArray());
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error loading existing PKCS12 store: {ex.Message}");
            }

            if (existingPkcs12Store.ContainsAlias(alias))
            {
                // If alias exists, delete it from existing PKCS12 store
                logger.LogDebug($"Alias '{alias}' exists in existing PKCS12 store, deleting it");
                existingPkcs12Store.DeleteEntry(alias);
                if (remove)
                {
                    // If remove is true, save existing P12 store and return
                    logger.LogDebug("This is a removal operation, saving existing PKCS12 store");
                    using (var mms = new MemoryStream())
                    {
                        existingPkcs12Store.Save(mms,
                                              string.IsNullOrEmpty(existingStorePassword) ? Array.Empty<char>() : existingStorePassword.ToCharArray(), new SecureRandom());
                        logger.LogDebug("Returning existing PKCS12 store");
                        return mms.ToArray();
                    }
                }
            }
            else if (remove)
            {
                // If alias does not exist and remove is true, return existingStore
                logger.LogDebug($"Alias '{alias}' does not exist in existing PKCS12 store and this is a removal operation, returning existing PKCS12 store as-is");
                using var mms = new MemoryStream();
                existingPkcs12Store.Save(mms, string.IsNullOrEmpty(existingStorePassword) ? Array.Empty<char>() : existingStorePassword.ToCharArray(), new SecureRandom());
                return mms.ToArray();
            }

            // adding the new certificate

            // Create new Pkcs12Store from newPkcs12Bytes
            var storeBuilder = new Pkcs12StoreBuilder();
            var newCertStore = storeBuilder.Build();

            try
            {
                logger.LogDebug("Loading new certificate as pfx/pkcs12 from newPkcs12Bytes");
                using (var pkcs12Ms = new MemoryStream(newCertBytes))
                {
                    newCertStore.Load(pkcs12Ms, string.IsNullOrEmpty(newCertPassword) ? Array.Empty<char>() : newCertPassword.ToCharArray());
                }
            }
            catch (Exception)
            {
                logger.LogDebug("Loading new Pkcs12Store from newPkcs12Bytes failed, trying to load as X509Certificate");
                var certificateParser = new X509CertificateParser();
                var certificate = certificateParser.ReadCertificate(newCertBytes);

                logger.LogDebug("Creating new Pkcs12Store from certificate");
                // create new Pkcs12Store from certificate
                storeBuilder = new Pkcs12StoreBuilder();
                newCertStore = storeBuilder.Build();
                logger.LogDebug($"Setting certificate entry in new Pkcs12Store as alias '{alias}'");
                newCertStore.SetCertificateEntry(alias, new X509CertificateEntry(certificate));
            }


            // Iterate through newCertStore aliases.
            logger.LogDebug("Iterating through new Pkcs12Store aliases");
            foreach (var al in newCertStore.Aliases)
            {
                logger.LogTrace($"alias: {al}");

                if (newCertStore.IsCertificateEntry(al)) 
                {
                    logger.LogTrace("is a certificate entry..");
                                 
                    if (existingPkcs12Store.ContainsAlias(al)) 
                    {
                        // it does.. so we remove it
                        logger.LogTrace("that already exists in the cert store, removing..");
                                                
                        existingPkcs12Store.DeleteEntry(al);

                        logger.LogTrace("and now replacing it with the new one..");
                        existingPkcs12Store.SetCertificateEntry(al, newCertStore.GetCertificate(al));
                    }
                }
                else if (newCertStore.IsKeyEntry(al))
                {
                    // it's the private key; get the chain and set it as the key entry for the new cert
                    logger.LogDebug($"Alias '{al}' is a key entry, getting key entry and certificate chain");
                    var keyEntry = newCertStore.GetKey(al);
                    logger.LogDebug($"Getting certificate chain for alias '{al}'");
                    var certificateChain = newCertStore.GetCertificateChain(al);

                    logger.LogDebug("Creating certificate list from certificate chain");
                    var certificates = certificateChain.ToList();

                    // check if alias exists in existingJksStore
                    if (existingPkcs12Store.ContainsAlias(al))
                    {
                        // If alias al exists, delete it from existingJksStore
                        logger.LogDebug($"Alias '{al}' exists in existing PKCS12 store, deleting it");
                        existingPkcs12Store.DeleteEntry(al);
                    }

                    // we found the key, setting the key for the new cert alias in the existing cert store 
                    logger.LogDebug($"Setting key entry with alias '{al}' for cert with alias '{alias}' in the existing store..");
                    existingPkcs12Store.SetKeyEntry(alias,
                        keyEntry,
                        certificates.ToArray());
                }
            }

            using var outStream = new MemoryStream();
            logger.LogDebug("Saving existing PKCS12 store to outStream");
            existingPkcs12Store.Save(outStream, string.IsNullOrEmpty(existingStorePassword) ? Array.Empty<char>() : existingStorePassword.ToCharArray(), new SecureRandom());

            logger.LogDebug("Returning updated PKCS12 store as byte[]");
            return outStream.ToArray();
        }
    }
}
