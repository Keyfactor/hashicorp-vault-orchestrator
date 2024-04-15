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
using System.Security.Cryptography;
using System.Text;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Extensions;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault
{
    public static class CertUtility
    {
        public static List<CurrentInventoryItem> CurrentInventoryFromPkcs12(Pkcs12Store store)
        {
            var logger = LogHandler.GetClassLogger<HcvKeyValueClient>();

            logger.MethodEntry();
            var certs = new List<CurrentInventoryItem>();

            try
            {
                using (var memoryStream = new MemoryStream())
                {
                    using (TextWriter streamWriter = new StreamWriter(memoryStream))
                    {
                        logger.LogTrace("Extracting Private Key...");
                        var pemWriter = new PemWriter(streamWriter);
                        logger.LogTrace("Created pemWriter...");
                        var aliases = store.Aliases.Cast<string>().Where(a => store.IsKeyEntry(a));
                        //logger.LogTrace($"Alias = {alias}");
                        foreach (var alias in aliases)
                        {
                            var certInventoryItem = new CurrentInventoryItem { Alias = alias };

                            var entryCerts = new List<string>();
                            logger.LogTrace("extracting public key");
                            var publicKey = store.GetCertificate(alias).Certificate.GetPublicKey();
                            var privateKeyEntry = store.GetKey(alias);
                            if (privateKeyEntry != null) certInventoryItem.PrivateKeyEntry = true;
                            pemWriter.WriteObject(publicKey);
                            streamWriter.Flush();
                            var publicKeyString = Encoding.ASCII.GetString(memoryStream.GetBuffer()).Trim()
                                .Replace("\r", "").Replace("\0", "");
                            entryCerts.Add(publicKeyString);

                            var pemChain = new List<string>();

                            logger.LogTrace("getting chain certs");

                            var chain = store.GetCertificateChain(alias).ToList();

                            chain.ForEach(c =>
                            {
                                var cert = c.Certificate.GetEncoded();
                                var encoded = Pemify(Convert.ToBase64String(cert));
                                pemChain.Add(encoded);
                            });

                            if (chain.Count() > 0)
                            {
                                certInventoryItem.UseChainLevel = true;
                                entryCerts.AddRange(pemChain);
                            }
                            certInventoryItem.Certificates = pemChain;
                            certs.Add(certInventoryItem);
                        }
                        memoryStream.Close();
                        streamWriter.Close();
                    }
                    logger.MethodExit();
                    return certs;
                }
            }
            catch (Exception ex)
            {
                logger.LogError("error extracting certs from pkcs12", ex);
                throw;
            }
        }

        public static Func<string, string> Pemify = base64Cert =>
        {
            string FormatBase64(string ss) =>
                ss.Length <= 64 ? ss : ss.Substring(0, 64) + "\n" + FormatBase64(ss.Substring(64));

            return CertificateHeaders.PEM_HEADER + FormatBase64(base64Cert) + CertificateHeaders.PEM_FOOTER;
        };

        public static string GenerateRandomString(int length)
        {
            using (Aes crypto = Aes.Create())
            {
                crypto.GenerateKey();
                return Convert.ToBase64String(crypto.Key).Substring(0, length);
            }
        }
    }
}
