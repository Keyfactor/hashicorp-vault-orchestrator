using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Extensions;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Pkcs;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault.FileStores
{
    public class Pkcs12FileStore : IFileStore
    {
        internal protected ILogger logger { get; set; }

        public Pkcs12FileStore()
        {
            logger = LogHandler.GetClassLogger<Pkcs12FileStore>();
        }

        public string AddCertificate(string alias, string pfxPassword, string entryContents, bool includeChain, string storeFileContent, string passphrase)
        {
            throw new NotImplementedException();
        }

        public byte[] CreateFileStore(string password)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<CurrentInventoryItem> GetInventory(Dictionary<string, object> certFields)
        {
            logger = LogHandler.GetClassLogger<JksFileStore>();
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

                if (certFields.TryGetValue("passphrase", out object filePasswordObj))
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
                certs = CertUtility.CurrentInventoryFromPkcs12(pkcs12Store);
                logger.MethodExit();
                return certs;
            }
            catch (Exception ex)
            {
                logger.LogError("Unable to read PKCS12 file.", ex);
                throw;
            }
        }

        public string RemoveCertificate(string alias, string passphrase, string storeFileContent)
        {
            throw new NotImplementedException();
        }
    }
}
