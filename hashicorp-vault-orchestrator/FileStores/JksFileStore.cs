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
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Extensions;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault.FileStores
{
    public class JksFileStore : IFileStore
    {
        internal protected ILogger logger { get; set; }

        private bool isPKCS12Format;

        public JksFileStore()
        {
            logger = LogHandler.GetClassLogger<JksFileStore>();
        }

        public byte[] CreateFileStore(string password)
        {
            var newStore = new JksStore();

            using (var outstream = new MemoryStream())
            {
                logger.LogDebug("Created new JKS store, saving it to outStream");
                newStore.Save(outstream, password.ToCharArray());
                return outstream.ToArray();
            }
        }

        public string AddCertificate(string alias, string pfxPassword, string entryContents, bool includeChain, string storeFileContent, string passphrase)
        {
            logger.MethodEntry();

            logger.LogTrace("converting base64 encoded jks store to binary.");
            var jksBytes = Convert.FromBase64String(storeFileContent);

            var newCertBytes = Convert.FromBase64String(entryContents);

            logger.LogTrace("adding the new certificate, and getting the new JKS store bytes.");
            var newJksBytes = AddOrRemoveCert(alias, pfxPassword, newCertBytes, jksBytes, passphrase);

            return Convert.ToBase64String(newJksBytes);
        }

        public string RemoveCertificate(string alias, string passphrase, string storeFileContent)
        {
            logger.MethodEntry();
            logger.LogTrace("converting base64 encoded jks store to binary.");
            var jksBytes = Convert.FromBase64String(storeFileContent);

            logger.LogTrace("removing the certificate, and getting the new JKS store bytes.");
            var newJksBytes = AddOrRemoveCert(alias, null, null, jksBytes, passphrase, true);

            return Convert.ToBase64String(newJksBytes);
        }

        public IEnumerable<CurrentInventoryItem> GetInventory(Dictionary<string, object> certFields)
        {
            logger.MethodEntry();
            // certFields should contain two entries.  The certificate with the "_jks" suffix, and "passphrase"

            string password;
            string base64EncodedJksStore;
            var certs = new List<CurrentInventoryItem>();
            string certKey = null;

            try
            {
                certKey = certFields.Keys.First(f => f.EndsWith(StoreFileExtensions.HCVKVJKS));

                if (certKey == null)
                {
                    throw new Exception($"No entry with extension '{StoreFileExtensions.HCVKVJKS}' found");
                }
                else
                {
                    base64EncodedJksStore = certFields[certKey].ToString();
                }

                if (certFields.TryGetValue("passphrase", out object filePasswordObj))
                {
                    password = filePasswordObj.ToString();
                }
                else
                {
                    throw new Exception($"No passphrase entry found for JKS store '{certKey}'.");
                }

                logger.LogTrace("converting base64 encoded cert to binary.");
                var jksBytes = Convert.FromBase64String(base64EncodedJksStore);
                var pkcs12Store = JksToPkcs12Store(jksBytes, password);
                certs = CertUtility.CurrentInventoryFromPkcs12(pkcs12Store);
                logger.MethodExit();
                return certs;

            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error reading entry for {certKey} in vault.");

                throw;
            }
        }

        private byte[] AddOrRemoveCert(string alias, string newCertPassword, byte[] newCertBytes, byte[] existingStore, string existingStorePassword, bool remove = false)
        {
            logger.MethodEntry();

            // If existingStore is null, create a new store
            var existingJksStore = new JksStore();
            Pkcs12Store existingPKCS12Store = null;

            var newJksStore = new JksStore();
            var createdNewStore = false;

            // If existingStore is not null, load it into jksStore
            if (existingStore != null)
            {
                logger.LogDebug("Loading existing JKS store");
                using (var ms = new MemoryStream(existingStore))
                {
                    try
                    {
                        existingJksStore.Load(ms, string.IsNullOrEmpty(existingStorePassword) ? Array.Empty<char>() : existingStorePassword.ToCharArray());
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "error loading store as JKS, attempting to load as PKCS12");
                        try
                        {

                            logger.LogTrace("creating pkcs12 store for working with the certificate.");
                            Pkcs12StoreBuilder pkcs12storeBuilder = new Pkcs12StoreBuilder();
                            existingPKCS12Store = pkcs12storeBuilder.Build();
                            existingPKCS12Store.Load(ms, existingStorePassword.ToCharArray());
                            isPKCS12Format = true;
                        }
                        catch (Exception innerEx)
                        {
                            logger.LogError(innerEx, $"Unable to load store as JKS or PKCS12: {innerEx.Message}");
                            isPKCS12Format = false;
                            throw;
                        }
                    }
                }
                if ((!isPKCS12Format && existingJksStore.ContainsAlias(alias)) || (isPKCS12Format && existingPKCS12Store.ContainsAlias(alias)))
                {
                    // If alias exists, delete it from existingJksStore
                    logger.LogDebug("Alias '{Alias}' exists in existing JKS store, deleting it", alias);

                    if (isPKCS12Format) existingPKCS12Store.DeleteEntry(alias); else existingJksStore.DeleteEntry(alias);

                    if (remove)
                    {
                        // If remove is true, save existingJksStore and return
                        logger.LogDebug("This is a removal operation, saving existing JKS store");
                        using (var mms = new MemoryStream())
                        {
                            if (isPKCS12Format)
                            {
                                existingPKCS12Store.Save(mms, string.IsNullOrEmpty(existingStorePassword) ? Array.Empty<char>() : existingStorePassword.ToCharArray(), new SecureRandom());
                            }
                            else
                            {
                                existingJksStore.Save(mms, string.IsNullOrEmpty(existingStorePassword) ? Array.Empty<char>() : existingStorePassword.ToCharArray());
                            }

                            logger.LogDebug("Returning existing JKS store");
                            return mms.ToArray();
                        }
                    }
                }
                else if (remove)
                {
                    // If alias does not exist and remove is true, return existingStore
                    logger.LogDebug("Alias '{Alias}' does not exist in existing JKS store and this is a removal operation, returning existing JKS store as-is", alias);
                    using (var mms = new MemoryStream())
                    {
                        if (isPKCS12Format)
                        {
                            existingPKCS12Store.Save(mms, string.IsNullOrEmpty(existingStorePassword) ? Array.Empty<char>() : existingStorePassword.ToCharArray(), new SecureRandom());
                        }
                        else
                        {
                            existingJksStore.Save(mms, string.IsNullOrEmpty(existingStorePassword) ? Array.Empty<char>() : existingStorePassword.ToCharArray());
                        }

                        return mms.ToArray();
                    }
                }
            }
            else
            {
                logger.LogDebug("Existing JKS store is null, creating new JKS store");
                createdNewStore = true;
            }

            // Create new Pkcs12Store from newPkcs12Bytes
            var storeBuilder = new Pkcs12StoreBuilder();
            var newCert = storeBuilder.Build();

            try
            {
                logger.LogDebug("Loading new Pkcs12Store from newPkcs12Bytes");
                using (var pkcs12Ms = new MemoryStream(newCertBytes))
                {
                    newCert.Load(pkcs12Ms, string.IsNullOrEmpty(newCertPassword) ? Array.Empty<char>() : newCertPassword.ToCharArray());
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
                newCert = storeBuilder.Build();
                logger.LogDebug("Setting certificate entry in new Pkcs12Store as alias '{Alias}'", alias);
                newCert.SetCertificateEntry(alias, new X509CertificateEntry(certificate));
            }


            // Iterate through newCert aliases.
            logger.LogDebug("Iterating through new Pkcs12Store aliases");
            foreach (var al in newCert.Aliases)
            {
                logger.LogTrace("Alias: {Alias}", al);
                if (newCert.IsKeyEntry(al))
                {
                    logger.LogDebug("Alias '{Alias}' is a key entry, getting key entry and certificate chain", al);
                    var keyEntry = newCert.GetKey(al);
                    logger.LogDebug("Getting certificate chain for alias '{Alias}'", al);
                    var certificateChain = newCert.GetCertificateChain(al);

                    logger.LogDebug("Creating certificate list from certificate chain");
                    var certificates = certificateChain.Select(certificateEntry => certificateEntry.Certificate).ToList();

                    if (createdNewStore)
                    {
                        // If createdNewStore is true, create a new store
                        logger.LogDebug("Created new JKS store, setting key entry for alias '{Alias}'", al);
                        newJksStore.SetKeyEntry(alias,
                            keyEntry.Key,
                            string.IsNullOrEmpty(existingStorePassword) ? Array.Empty<char>() : existingStorePassword.ToCharArray(),
                            certificates.ToArray());
                    }
                    else
                    {
                        // If createdNewStore is false, add to existingJksStore
                        // check if alias exists in existingJksStore
                        if ((isPKCS12Format && existingPKCS12Store.ContainsAlias(alias)) || (!isPKCS12Format && existingJksStore.ContainsAlias(alias)))
                        {
                            // If alias exists, delete it from existingJksStore
                            logger.LogDebug("Alias '{Alias}' exists in existing JKS store, deleting it", alias);
                            if (isPKCS12Format) existingPKCS12Store.DeleteEntry(alias); else existingJksStore.DeleteEntry(alias);
                        }

                        logger.LogDebug("Setting key entry for alias '{Alias}'", alias);

                        if (!isPKCS12Format)
                        {
                            logger.LogTrace("using JKS format");
                            existingJksStore.SetKeyEntry(alias,
                                keyEntry.Key,
                                string.IsNullOrEmpty(existingStorePassword) ? Array.Empty<char>() : existingStorePassword.ToCharArray(),
                                certificates.ToArray());
                        }
                        else
                        {
                            logger.LogTrace("using PKCS12 format");
                            existingPKCS12Store.SetKeyEntry(alias,
                                    keyEntry,
                                    certificateChain);
                        }
                    }
                }
                else
                {
                    logger.LogDebug("Setting certificate entry for existing JKS store, alias '{Alias}'", alias);
                    if (!isPKCS12Format) existingJksStore.SetCertificateEntry(alias, newCert.GetCertificate(alias).Certificate);
                    else existingPKCS12Store.SetCertificateEntry(alias, newCert.GetCertificate(alias));
                }
            }

            using (var outStream = new MemoryStream())
            {
                logger.LogDebug("Saving existing JKS store to outStream");
                if (isPKCS12Format) existingPKCS12Store.Save(outStream, string.IsNullOrEmpty(existingStorePassword) ? Array.Empty<char>() : existingStorePassword.ToCharArray(), new SecureRandom());
                else existingJksStore.Save(outStream, string.IsNullOrEmpty(existingStorePassword) ? Array.Empty<char>() : existingStorePassword.ToCharArray());

                logger.LogDebug("Returning updated JKS store as byte[]");
                return outStream.ToArray();
            }
        }

        private Pkcs12Store JksToPkcs12Store(byte[] storeContents, string storePassword)
        {
            logger.LogTrace("Entering method to convert JKS store to PKCS12 to work with the contents.");

            Pkcs12StoreBuilder storeBuilder = new Pkcs12StoreBuilder();
            Pkcs12Store pkcs12Store = storeBuilder.Build();
            Pkcs12Store pkcs12StoreNew = storeBuilder.Build();
            Pkcs12Store existingPKCS12Store = null;
            JksStore jksStore = new JksStore();


            logger.LogTrace("loading the contents into a jks store");
            try
            {
                using (var ms = new MemoryStream(storeContents))
                {
                    jksStore.Load(ms, string.IsNullOrEmpty(storePassword) ? new char[0] : storePassword.ToCharArray());
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "error loading store as JKS, attempting to load as PKCS12");
                try
                {
                    using (var ms = new MemoryStream(storeContents))
                    {
                        logger.LogTrace("creating pkcs12 store for working with the certificate.");
                        Pkcs12StoreBuilder pkcs12storeBuilder = new Pkcs12StoreBuilder();
                        existingPKCS12Store = pkcs12storeBuilder.Build();
                        existingPKCS12Store.Load(ms, storePassword.ToCharArray());
                        isPKCS12Format = true;
                    }
                }
                catch (Exception innerEx)
                {
                    logger.LogError(innerEx, $"Unable to load store as JKS or PKCS12: {innerEx.Message}");
                    isPKCS12Format = false;
                    throw;
                }
            }


            if (existingPKCS12Store != null) return existingPKCS12Store; //if it was already in PKCS12 format, just return it.

            foreach (string alias in jksStore.Aliases)
            {
                if (jksStore.IsKeyEntry(alias))
                {
                    logger.LogTrace("extracting key pair");
                    AsymmetricKeyParameter keyParam = jksStore.GetKey(alias, string.IsNullOrEmpty(storePassword) ? new char[0] : storePassword.ToCharArray());
                    AsymmetricKeyEntry keyEntry = new AsymmetricKeyEntry(keyParam);

                    logger.LogTrace("extracting certificate chain");

                    X509Certificate[] certificateChain = jksStore.GetCertificateChain(alias);
                    List<X509CertificateEntry> certificateChainEntries = new List<X509CertificateEntry>();
                    foreach (X509Certificate certificate in certificateChain)
                    {
                        certificateChainEntries.Add(new X509CertificateEntry(certificate));
                    }
                    logger.LogTrace("setting keys on the pkcs12 store.");
                    pkcs12Store.SetKeyEntry(alias, keyEntry, certificateChainEntries.ToArray());
                }
                else
                {
                    logger.LogTrace("setting certificates on the pkcs12 store");
                    pkcs12Store.SetCertificateEntry(alias, new X509CertificateEntry(jksStore.GetCertificate(alias)));
                }
            }

            // Second Pkcs12Store necessary because of an obscure BC bug where creating a Pkcs12Store without .Load (code above using "Set" methods only) does not set all internal hashtables necessary to avoid an error later
            //  when processing store.
            MemoryStream ms2 = new MemoryStream();
            pkcs12Store.Save(ms2, string.IsNullOrEmpty(storePassword) ? new char[0] : storePassword.ToCharArray(), new Org.BouncyCastle.Security.SecureRandom());
            ms2.Position = 0;

            pkcs12StoreNew.Load(ms2, string.IsNullOrEmpty(storePassword) ? new char[0] : storePassword.ToCharArray());
            return pkcs12StoreNew;
        }
    }
}
