// Copyright 2023 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.

using System.Collections.Generic;
using System.Threading.Tasks;
using Keyfactor.Orchestrators.Extensions;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault
{
    public interface IHashiClient
    {
        Task<(List<CurrentInventoryItem>, List<string>)> GetCertificates();
        Task<CurrentInventoryItem> GetCertificateFromPemStore(string key);
        Task<(List<string>, List<string>)> GetVaults(string storePath);
        Task PutCertificate(string certName, string contents, string pfxPassword, bool includeChain);
        Task<bool> RemoveCertificate(string certName);
        Task CreateCertStore();
    }
}
