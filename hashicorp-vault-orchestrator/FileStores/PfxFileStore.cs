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
    public class PfxFileStore : IFileStore
    {
        internal protected ILogger logger { get; set; }

        public PfxFileStore()
        {
            logger = LogHandler.GetClassLogger<PfxFileStore>();
        }

        public string AddCertificate(string alias, string pfxPassword, string entryContents, bool includeChain, string storeFileContent, string passphrase)
        {
            throw new NotImplementedException();
        }

        public byte[] CreateFileStore(string name, string password)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<CurrentInventoryItem> GetInventory(Dictionary<string, object> certFields)
        {
            logger = LogHandler.GetClassLogger<PfxFileStore>();
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

        public string RemoveCertificate(string alias, string passphrase, string storeFileContent)
        {
            throw new NotImplementedException();
        }
    }
}
