// Copyright 2022 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.

using Keyfactor.Orchestrators.Extensions;
using Newtonsoft.Json;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault.Jobs
{
    public abstract class JobBase
    {
        public string ExtensionName => "HCV";

        public string StorePath { get; set; }

        public string VaultToken { get; set; }

        public string VaultServerUrl { get; set; }

        public string MountPoint { get; set; } // the mount point of the KV secrets engine.  defaults to KV

        internal protected HcvClient VaultClient { get; set; }


        public void InitializeStore(InventoryJobConfiguration config)
        {
            var props = JsonConvert.DeserializeObject(config.CertificateStoreDetails.Properties);
            //var props = Jsonconfig.CertificateStoreDetails.Properties;

            InitProps(props);
            StorePath = config.CertificateStoreDetails?.StorePath ?? null;
        }

        public void InitializeStore(DiscoveryJobConfiguration config)
        {
            var props = config.JobProperties;
            InitProps(props);
        }
        public void InitializeStore(ManagementJobConfiguration config)
        {
            var props = JsonConvert.DeserializeObject(config.CertificateStoreDetails.Properties);
            InitProps(props);
            StorePath = config.CertificateStoreDetails?.StorePath ?? null;
            StorePath = StorePath.Replace("/", string.Empty);

        }

        private void InitProps(dynamic props) {
            if (props == null) throw new System.Exception("Properties is null", props);

            VaultToken = props["VaultToken"];
            VaultServerUrl = props["VaultServerUrl"];
            MountPoint = props["MountPoint"] ?? null;

            VaultClient = new HcvClient(VaultToken, VaultServerUrl);
        }
    }
}
