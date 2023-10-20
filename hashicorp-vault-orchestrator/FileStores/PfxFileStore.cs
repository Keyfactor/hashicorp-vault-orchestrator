using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Extensions;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault.FileStores
{
    public class PfxFileStore : IFileStore
    {
        internal protected ILogger logger { get; set; }

        public PfxFileStore()
        {
            logger = LogHandler.GetClassLogger<PfxFileStore>();
        }

        public byte[] CreateFileStore(string password)
        {
            Pkcs12Store newStore = null;
            using (var outstream = new MemoryStream())
            {
                logger.LogDebug("Created new PFX store, saving it to outStream");
                newStore.Save(outstream, password.ToCharArray(), new SecureRandom());
                return outstream.ToArray();
            }
        }

        public string AddCertificate(string alias, string pfxPassword, string entryContents, bool includeChain, string storeFileContent, string passphrase)
        {
            logger.MethodEntry();

            logger.LogTrace("converting base64 encoded PFX store to binary.");
            var pfxBytes = Convert.FromBase64String(storeFileContent);


            var newCertBytes = Convert.FromBase64String(entryContents);

            logger.LogTrace("adding the new certificate, and getting the new PFX store bytes.");
            var newJksBytes = AddOrRemoveCert(alias, pfxPassword, newCertBytes, pfxBytes, passphrase);

            return Convert.ToBase64String(newJksBytes);
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

        public IEnumerable<CurrentInventoryItem> GetInventory(Dictionary<string, object> certFields)
        {         
            logger.MethodEntry();
            // certFields should contain two entries.  The certificate with the "_pfx" suffix, and "passphrase"
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

            if (certFields.TryGetValue("passphrase", out object filePasswordObj))
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
                using (var pfxBytesMemoryStream = new MemoryStream(existingStore))
                {
                    logger.LogTrace("creating pkcs12 store for working with the certificate.");
                    Pkcs12StoreBuilder sb = new Pkcs12StoreBuilder();
                    existingPfxStore = sb.Build();
                    existingPfxStore.Load(pfxBytesMemoryStream, existingStorePassword.ToCharArray());
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error loading existing PFX store: {ex.Message}");
            }

            if (existingPfxStore.ContainsAlias(alias))
            {
                // If alias exists, delete it from existingJksStore
                logger.LogDebug($"Alias '{alias}' exists in existing PFX store, deleting it");
                existingPfxStore.DeleteEntry(alias);
                if (remove)
                {
                    // If remove is true, save existingJksStore and return
                    logger.LogDebug("This is a removal operation, saving existing PFX store");
                    using (var mms = new MemoryStream())
                    {
                        existingPfxStore.Save(mms,
                                              string.IsNullOrEmpty(existingStorePassword) ? Array.Empty<char>() : existingStorePassword.ToCharArray(), new SecureRandom());
                        logger.LogDebug("Returning existing PFX store");
                        return mms.ToArray();
                    }
                }
            }
            else if (remove)
            {
                // If alias does not exist and remove is true, return existingStore
                logger.LogDebug($"Alias '{alias}' does not exist in existing PFX store and this is a removal operation, returning existing PFX store as-is");
                using (var mms = new MemoryStream())
                {
                    existingPfxStore.Save(mms, string.IsNullOrEmpty(existingStorePassword) ? Array.Empty<char>() : existingStorePassword.ToCharArray(), new SecureRandom());
                    return mms.ToArray();
                }
            }

            // adding the new certificate

            // Create new Pkcs12Store from newPkcs12Bytes
            var storeBuilder = new Pkcs12StoreBuilder();
            var newCert = storeBuilder.Build();

            try
            {
                logger.LogDebug("Loading new certificate as pfx/pkcs12 from newPkcs12Bytes");
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
                    logger.LogDebug($"Alias '{al}' is a key entry, getting key entry and certificate chain");
                    var keyEntry = newCert.GetKey(al);
                    logger.LogDebug($"Getting certificate chain for alias '{al}'");
                    var certificateChain = newCert.GetCertificateChain(al);

                    logger.LogDebug("Creating certificate list from certificate chain");
                    var certificates = certificateChain.ToList();

                    // If createdNewStore is false, add to existingJksStore
                    // check if alias exists in existingJksStore
                    if (existingPfxStore.ContainsAlias(alias))
                    {
                        // If alias exists, delete it from existingJksStore
                        logger.LogDebug($"Alias '{alias}' exists in existing PFX store, deleting it");
                        existingPfxStore.DeleteEntry(alias);
                    }

                    logger.LogDebug($"Setting key entry for alias '{alias}'");
                    existingPfxStore.SetKeyEntry(alias,
                        keyEntry,
                        certificates.ToArray());
                }
                else
                {
                    logger.LogDebug($"Setting certificate with alias '{alias}' for existing PFX store");
                    existingPfxStore.SetCertificateEntry(alias, newCert.GetCertificate(alias));
                }
            }

            using (var outStream = new MemoryStream())
            {
                logger.LogDebug("Saving existing PFX store to outStream");
                existingPfxStore.Save(outStream, string.IsNullOrEmpty(existingStorePassword) ? Array.Empty<char>() : existingStorePassword.ToCharArray(), new SecureRandom());

                logger.LogDebug("Returning updated PFX store as byte[]");
                return outStream.ToArray();
            }
        }
    }
}
