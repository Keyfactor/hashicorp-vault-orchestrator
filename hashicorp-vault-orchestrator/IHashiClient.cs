using System.Collections.Generic;
using System.Threading.Tasks;
using Keyfactor.Orchestrators.Extensions;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault
{
    public interface IHashiClient
    {
        Task<IEnumerable<CurrentInventoryItem>> GetCertificates(string storePath, string mountPoint = null);
        Task<CurrentInventoryItem> GetCertificate(string key, string storePath, string mountPoint = null);
        Task<IEnumerable<string>> GetVaults(string storePath, string mountPoint = null);
        Task PutCertificate(string certName, string contents, string pfxPassword, string storePath, string mountPoint = null);
        Task<bool> DeleteCertificate(string certName, string storePath, string mountPoint = null);
    }
}
