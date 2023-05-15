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

        public string SecretsEngine { get; set; } // "PKI", "Keyfactor", "Key Value"

        public string VaultServerUrl { get; set; }
        
        public bool SubfolderInventory { get; set; }

        public string MountPoint { get; set; } // the mount point of the KV secrets engine.  defaults to KV

        public string RoleName { get; set; }

        internal protected IHashiClient VaultClient { get; set; }

        const string KEY_VALUE_ENGINE = "KV";
        const string KEYFACTOR_ENGINE = "Keyfactor";
        const string PKI_ENGINE = "Hashicorp PKI";

        public void InitializeStore(InventoryJobConfiguration config)
        {
            var props = JsonConvert.DeserializeObject(config.CertificateStoreDetails.Properties);
            //var props = Jsonconfig.CertificateStoreDetails.Properties;

            StorePath = config.CertificateStoreDetails?.StorePath ?? null;
            StorePath = StorePath.TrimStart('/');
            StorePath = StorePath.TrimEnd('/');
            StorePath = StorePath == null ? null : StorePath + "/"; //enforce single trailing slash for path

            InitProps(props, config.Capability);
        }

        public void InitializeStore(DiscoveryJobConfiguration config)
        {
            var props = config.JobProperties;
            InitProps(props, config.Capability);
        }
        public void InitializeStore(ManagementJobConfiguration config)
        {
            var props = JsonConvert.DeserializeObject(config.CertificateStoreDetails.Properties);
            StorePath = config.CertificateStoreDetails?.StorePath ?? null;
            StorePath = StorePath.TrimStart('/');
            StorePath = StorePath.TrimEnd('/');
            StorePath = StorePath == null ? null : StorePath + "/"; //enforce single trailing slash for path

            InitProps(props, config.Capability);
        }

        private void InitProps(dynamic props, string capability)
        {
            if (props == null) throw new System.Exception("Properties is null", props);

            VaultToken = props["VaultToken"];
            VaultServerUrl = props["VaultServerUrl"];
            SecretsEngine = props["SecretsEngine"];
            MountPoint = props["MountPoint"] ?? null;
            if (props["SubfolderInventory"] == null)
            {
                SubfolderInventory = false;
            }
            else
            {
                SubfolderInventory = props["SubfolderInventory"];
            }

            var isPki = capability.Contains("HCVPKI");

            if (!isPki)
            {
                VaultClient = new HcvKeyValueClient(VaultToken, VaultServerUrl, MountPoint, StorePath, SubfolderInventory);
            }
            else
            {
                VaultClient = new HcvKeyfactorClient(VaultToken, VaultServerUrl, MountPoint, StorePath);
            }

        }
    }
}