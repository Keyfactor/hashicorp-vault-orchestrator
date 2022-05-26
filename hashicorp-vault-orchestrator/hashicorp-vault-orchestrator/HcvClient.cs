using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Microsoft.Extensions.Logging;
using VaultSharp;
using VaultSharp.V1.AuthMethods;
using VaultSharp.V1.AuthMethods.Token;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault
{
    public class HcvClient
    {
        private IVaultClient _vaultClient { get; set; }

        protected IVaultClient VaultClient => _vaultClient;

        private ILogger logger = LogHandler.GetClassLogger<HcvClient>();

        private string _storePath { get; set; }

        private VaultClientSettings clientSettings { get; set; }


        public HcvClient(string vaultToken, string serverUrl)
        {
            // Initialize one of the several auth methods.
            IAuthMethodInfo authMethod = new TokenAuthMethodInfo(vaultToken);

            // Initialize settings. You can also set proxies, custom delegates etc. here.
            clientSettings = new VaultClientSettings(serverUrl, authMethod);

            _vaultClient = new VaultClient(clientSettings);
        }

        public async Task<CurrentInventoryItem> GetCertificate(string key, string storePath, string mountPoint = null)
        {
            VaultClient.V1.Auth.ResetVaultToken();

            Dictionary<string, string> certData;
            try
            {
                var fullPath = storePath + "/" + key;

                if (mountPoint == null)
                {
                    certData = (Dictionary<string, string>)(await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretAsync(fullPath)).Data.Data;
                }
                else
                {
                    certData = (Dictionary<string, string>)(await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretAsync(fullPath, mountPoint: _storePath)).Data.Data;
                }
            }
            catch (Exception ex)
            {
                logger.LogError("Error getting certificate from Vault", ex);
                throw;
            }

            try
            {
                var certs = new List<string>() { certData["PUBLIC_KEY"] };

                var keys = certData.Keys.Where(k => k.StartsWith("PUBLIC_KEY_")).ToList();

                keys.ForEach(k => certs.Add(certData[k]));

                return new CurrentInventoryItem()
                {
                    Alias = key,
                    PrivateKeyEntry = certData.Keys.Contains("PRIVATE_KEY"),
                    ItemStatus = OrchestratorInventoryItemStatus.Unknown,
                    UseChainLevel = true,
                    Certificates = certs.ToArray()
                };
            }
            catch (Exception ex)
            {
                logger.LogError("Error parsing cert data", ex);
                throw;
            }
        }

        public async Task<IEnumerable<string>> GetVaults(string storePath, string mountPoint = null)
        {
            VaultClient.V1.Auth.ResetVaultToken();

            var vaults = new List<string>();

            try
            {
                if (mountPoint == null)
                {
                    vaults = (await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretPathsAsync(storePath)).Data.Keys.ToList();
                }
                else
                {
                    vaults = (await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretPathsAsync(storePath, mountPoint)).Data.Keys.ToList();
                }

            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);
            }

            return vaults;
        }

        public async Task PutCertificate(string certName, string contents, string pfxPassword, string storePath, string mountPoint = null)
        {
            VaultClient.V1.Auth.ResetVaultToken();

            var beginPrivateKey = "-----BEGIN PRIVATE KEY-----";
            var endPrivateKey = "-----END PRIVATE KEY-----";

            var beginCert = "-----BEGIN CERTIFICATE-----";
            var endCert = "-----END CERTIFICATE-----";

            string[] privateSplit = { beginPrivateKey, endPrivateKey };
            string[] publicSplit = { beginCert, endCert };

            var certDict = new Dictionary<string, string>();

            try
            {
                var privateKey = contents.Split(privateSplit, StringSplitOptions.RemoveEmptyEntries)[0];
                var publicKeysOnly = contents.Replace(beginCert, "").Replace(endCert, "").Replace(privateKey, "");
                var publicKeys = publicKeysOnly.Split(publicSplit, StringSplitOptions.RemoveEmptyEntries).ToList();

                certDict.Add("PRIVATE_KEY", privateKey);

                var index = 0;

                publicKeys.ForEach(pubk =>
                {
                    var fieldName = index == 0 ? "PUBLIC_KEY" : "PUBLIC_KEY_" + index;
                    certDict.Add(fieldName, pubk);
                });

                certDict.Add("KEY_SECRET", pfxPassword);
            }
            catch (Exception ex)
            {
                logger.LogError("Error parsing certificate content", ex);
                throw;
            }
            try
            {
                var fullPath = storePath + "/" + certName;

                if (mountPoint == null)
                {
                    await VaultClient.V1.Secrets.KeyValue.V2.WriteSecretAsync(fullPath, certDict);
                }
                else
                {
                    await VaultClient.V1.Secrets.KeyValue.V2.WriteSecretAsync(fullPath, certDict, mountPoint: mountPoint);
                }
            }
            catch (Exception ex)
            {
                logger.LogError("Error writing cert to Vault", ex);
                throw;
            }
        }

        public async Task<bool> DeleteCertificate(string certName, string storePath, string mountPoint = null)
        {
            VaultClient.V1.Auth.ResetVaultToken();

            try
            {
                var fullPath = storePath + "/" + certName;

                if (mountPoint == null)
                {
                    await VaultClient.V1.Secrets.KeyValue.V2.DeleteSecretAsync(fullPath);
                }
                else
                {
                    await VaultClient.V1.Secrets.KeyValue.V2.DeleteSecretAsync(fullPath, mountPoint);
                }

            }
            catch (Exception ex)
            {
                logger.LogError("Error removing cert from Vault", ex);
                throw;
            }
            return true;
        }

        public async Task<bool> CreateStore(string storePath, string mountPoint = null)
        {
            return true;
            //VaultClient.V1.Auth.ResetVaultToken();
        }

        public async Task<IEnumerable<CurrentInventoryItem>> GetCertificates(string storePath, string mountPoint = null)
        {
            VaultClient.V1.Auth.ResetVaultToken();

            var certs = new List<CurrentInventoryItem>();
            var certNames = new List<string>();
            try
            {
                if (mountPoint == null)
                {
                    certNames = (await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretPathsAsync(storePath)).Data.Keys.ToList();
                }
                else
                {
                    certNames = (await VaultClient.V1.Secrets.KeyValue.V2.ReadSecretPathsAsync(storePath, mountPoint)).Data.Keys.ToList();
                }

                certNames.ForEach(async k =>
                {
                    var cert = await GetCertificate(k, mountPoint);
                    certs.Add(cert);
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);
            }

            return certs;
        }
    }
}
