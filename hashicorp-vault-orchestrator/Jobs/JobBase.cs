// Copyright 2023 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.

using System;
using System.Collections.Generic;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault.Jobs
{
    public abstract class JobBase
    {
        public string ExtensionName => "HCV";
        
        public string StorePath { get; set; }

        public string VaultToken { get; set; }

        public string ClientMachine { get; set; }

        public string VaultServerUrl { get; set; }
        
        public bool SubfolderInventory { get; set; }

        public bool IncludeCertChain { get; set; }

        public string MountPoint { get; set; } // the mount point of the KV secrets engine.  defaults to KV by Vault if not provided.

        internal protected IHashiClient VaultClient { get; set; }        
        internal protected string _storeType { get; set; }
        internal protected ILogger logger { get; set; }
        internal protected IPAMSecretResolver PamSecretResolver { get; set; }


        public void InitializeStore(InventoryJobConfiguration config)
        {            
            ClientMachine = config.CertificateStoreDetails.ClientMachine;

            // ClientId can be omitted for system assigned managed identities, required for user assigned or service principal auth
            VaultServerUrl = PAMUtilities.ResolvePAMField(PamSecretResolver, logger, "Server UserName", config.ServerUsername);

            // ClientSecret can be omitted for managed identities, required for service principal auth
            VaultToken = PAMUtilities.ResolvePAMField(PamSecretResolver, logger, "Server Password", config.ServerPassword);

            StorePath = config.CertificateStoreDetails.StorePath;
            ClientMachine = config.CertificateStoreDetails.ClientMachine;

            var props = JsonConvert.DeserializeObject<Dictionary<string, object>>(config.CertificateStoreDetails.Properties);
            
            InitProps(props, config.Capability);
        }

        public void InitializeStore(DiscoveryJobConfiguration config)
        {
            ClientMachine = config.ClientMachine;

            // ClientId can be omitted for system assigned managed identities, required for user assigned or service principal auth
            VaultServerUrl = PAMUtilities.ResolvePAMField(PamSecretResolver, logger, "Server UserName", config.ServerUsername);

            // ClientSecret can be omitted for managed identities, required for service principal auth
            VaultToken = PAMUtilities.ResolvePAMField(PamSecretResolver, logger, "Server Password", config.ServerPassword);

            var dirs = config.JobProperties?["dirs"] as string;
            // Discovery jobs need to pass the mount-point/namespace in this field.
            // if nothing is provided, we default to mount point: "kv-v2" and no namespace.
            if (!string.IsNullOrEmpty(dirs) && dirs.Trim() != "/" && dirs.Trim() != "\\") {
                MountPoint = dirs.Trim();
            }

            logger.LogTrace($"Directories to search: {dirs}");

            InitProps(config.JobProperties, config.Capability);
        }
        public void InitializeStore(ManagementJobConfiguration config)
        {
            ClientMachine = config.CertificateStoreDetails.ClientMachine;

            // ClientId can be omitted for system assigned managed identities, required for user assigned or service principal auth
            VaultServerUrl = PAMUtilities.ResolvePAMField(PamSecretResolver, logger, "Server UserName", config.ServerUsername);

            // ClientSecret can be omitted for managed identities, required for service principal auth
            VaultToken = PAMUtilities.ResolvePAMField(PamSecretResolver, logger, "Server Password", config.ServerPassword);

            StorePath = config.CertificateStoreDetails.StorePath;
            ClientMachine = config.CertificateStoreDetails.ClientMachine;
            dynamic props = JsonConvert.DeserializeObject(config.CertificateStoreDetails.Properties.ToString());            
            InitProps(props, config.Capability);
        }

        private void InitProps(dynamic props, string capability)
        {
            _storeType = capability;

            if (props == null) throw new Exception("Properties is null");

            if (props.ContainsKey("StorePath")) {
                StorePath = props["StorePath"].ToString();
                StorePath = StorePath.TrimStart('/');
                StorePath = StorePath.TrimEnd('/');
                if (_storeType.Contains(StoreType.HCVKVPEM) || _storeType.Contains(StoreType.HCVPKI)) {
                    StorePath += "/"; //ensure single trailing slash for path for PKI or PEM stores.  Others use the entry value instead of the container.
                }                
            }

            MountPoint = props.ContainsKey("MountPoint") ? props["MountPoint"].ToString() : MountPoint;
            SubfolderInventory = props.ContainsKey("SubfolderInventory") ? bool.Parse(props["SubfolderInventory"].ToString()) : false;
            IncludeCertChain = props.ContainsKey("IncludeCertChain") ? bool.Parse(props["IncludeCertChain"].ToString()) : false;
            
            var isPki = _storeType.Contains("HCVPKI");

            if (!isPki)
            {
                VaultClient = new HcvKeyValueClient(VaultToken, VaultServerUrl, MountPoint, StorePath, _storeType, SubfolderInventory);
            }
            else
            {
                VaultClient = new HcvKeyfactorClient(VaultToken, VaultServerUrl, MountPoint, StorePath);
            }

        }
    }
}