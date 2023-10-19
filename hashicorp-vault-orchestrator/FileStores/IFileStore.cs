using System.Collections.Generic;
using Keyfactor.Orchestrators.Extensions;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault
{
    public interface IFileStore
    {
        string AddCertificate(string alias, string pfxPassword, string entryContents, bool includeChain, string certContent, string passphrase);
        string RemoveCertificate(string alias, string passphrase, string storeFileContent);
        byte[] CreateFileStore(string name, string password);
        IEnumerable<CurrentInventoryItem> GetInventory(Dictionary<string, object> certFields);
    }
}
