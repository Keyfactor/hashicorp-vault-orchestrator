using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Microsoft.Extensions.Logging;
using VaultSharp;
using VaultSharp.V1;
using VaultSharp.V1.AuthMethods;
using VaultSharp.V1.AuthMethods.Token;
using VaultSharp.V1.Commons;
using VaultSharp.V1.SecretsEngines.Consul;
using VaultSharp.V1.SecretsEngines.PKI;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault
{
    public class HcvClient
    {
        private IVaultClient _vaultClient { get; set; }

        protected IVaultClient VaultClient => _vaultClient;

        private ILogger logger = LogHandler.GetClassLogger<HcvClient>();

        private string _storePath { get; set; }

        private VaultClientSettings clientSettings { get; set; }


        public HcvClient(string vaultToken, string serverUrl, string storePath)
        {
            // Initialize one of the several auth methods.
            IAuthMethodInfo authMethod = new TokenAuthMethodInfo(vaultToken);

            // Initialize settings. You can also set proxies, custom delegates etc. here.
            clientSettings = new VaultClientSettings(serverUrl, authMethod);

            // Use client to read a key-value secret.

            // Very important to provide mountpath and secret name as two separate parameters. Don't provide a single combined string.
            // Please use named parameters for 100% clarity of code. (the method also takes version and wrapTimeToLive as params)

            // Generate a dynamic Consul credential
            // Secret<ConsulCredentials> consulCreds = await vaultClient.V1.Secrets.Consul.GetCredentialsAsync(consulRole, consulMount);

            // string consulToken = consulCredentials.Data.Token;
        }

        public async Task<CurrentInventoryItem> GetCertificate(string serial)
        {
            Secret<CertificateData> cert = null;

            try
            {
                _vaultClient = new VaultClient(clientSettings);

                cert = await VaultClient.V1.Secrets.PKI.ReadCertificateAsync(serial, _storePath);
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);
                throw;
            }

            return new CurrentInventoryItem()
            {
                Alias = cert.Data.SerialNumber,
                PrivateKeyEntry = true,
                ItemStatus = OrchestratorInventoryItemStatus.Unknown,
                UseChainLevel = true,
                Certificates = new string[] { cert.Data.CertificateContent }
            };
        }

        public async Task PutCertificate()
        {

        }

        public async Task CreateStore()
        {

        }

        public async Task<IEnumerable<CurrentInventoryItem>> GetCertificates()
        {
            var certs = new List<CurrentInventoryItem>();
            try
            {
                var certSerials = await VaultClient.V1.Secrets.PKI.ListCertificatesAsync(_storePath);

                certSerials.Data.Keys.ForEach(k =>
                {
                    var cert = GetCertificate(k).Result;
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
