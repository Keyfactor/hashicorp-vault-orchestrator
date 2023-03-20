using System.Collections.Generic;
using System.Threading.Tasks;
using Keyfactor.Orchestrators.Extensions;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault
{
    public interface IHashiClient
    {
        Task<IEnumerable<CurrentInventoryItem>> GetCertificates();
        Task<CurrentInventoryItem> GetCertificate(string key);
        Task<IEnumerable<string>> GetVaults();
        Task PutCertificate(string certName, string contents, string pfxPassword);
        Task<bool> DeleteCertificate(string certName);
    }
}
