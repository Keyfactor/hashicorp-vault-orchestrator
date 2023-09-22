using System.Collections.Generic;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.X509;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault
{
    public class JksUtility
    {
        public Pkcs12Store JksToPkcs12Store(byte[] storeContents, string storePassword, ILogger logger)
        {
            logger.LogTrace("Entering method to convert JKS store to PKCS12 to work with the contents.");

            Pkcs12StoreBuilder storeBuilder = new Pkcs12StoreBuilder();
            Pkcs12Store pkcs12Store = storeBuilder.Build();
            Pkcs12Store pkcs12StoreNew = storeBuilder.Build();

            JksStore jksStore = new JksStore();

            using (MemoryStream ms = new MemoryStream(storeContents))
            {
                logger.LogTrace("loading the contents into a jks store");
                jksStore.Load(ms, string.IsNullOrEmpty(storePassword) ? new char[0] : storePassword.ToCharArray());
            }

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
