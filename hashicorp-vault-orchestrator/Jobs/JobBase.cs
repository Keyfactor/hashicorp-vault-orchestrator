// Copyright 2023 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.

using System;
using System.Collections.Generic;
using Keyfactor.Logging;
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
        public string MountPoint { get; set; } // the mount point of the KV secrets engine.  defaults to kv-v2 if not provided.
        public string Namespace { get; set; } // for enterprise editions of vault that utilize namespaces; split from the passed in mount point if needed. 
        internal protected IHashiClient VaultClient { get; set; }
        internal protected string _storeType { get; set; }
        internal protected ILogger logger { get; set; }
        internal protected IPAMSecretResolver PamSecretResolver { get; set; }

        public JobBase(IPAMSecretResolver resolver) {
            PamSecretResolver = resolver;
        }

        public void Initialize(InventoryJobConfiguration config)
        {
            logger = LogHandler.GetClassLogger(GetType());

            ClientMachine = config.CertificateStoreDetails.ClientMachine;
            MountPoint = "kv-v2"; // default
                       
            VaultServerUrl = PAMUtilities.ResolvePAMField(PamSecretResolver, logger, "Server UserName", config.ServerUsername);

            VaultToken = PAMUtilities.ResolvePAMField(PamSecretResolver, logger, "Server Password", config.ServerPassword);

            StorePath = config.CertificateStoreDetails.StorePath;
            ClientMachine = config.CertificateStoreDetails.ClientMachine;

            var props = JsonConvert.DeserializeObject<Dictionary<string, object>>(config.CertificateStoreDetails.Properties);

            InitProps(props, config.Capability);
        }

        public void Initialize(DiscoveryJobConfiguration config)
        {
            logger = LogHandler.GetClassLogger(GetType());
            ClientMachine = config.ClientMachine;

            VaultServerUrl = PAMUtilities.ResolvePAMField(PamSecretResolver, logger, "Server UserName", config.ServerUsername);

            VaultToken = PAMUtilities.ResolvePAMField(PamSecretResolver, logger, "Server Password", config.ServerPassword);

            var subPath = config.JobProperties?["dirs"] as string;
            var mp = config.JobProperties?["extensions"] as string;

            // Discovery jobs need to pass the sub-paths in the "directories to search" field.
            // The mount point and namespace should be passed in the "Extensions" field.
            // if nothing is provided, we default to mount point: "kv-v2" and no namespace.

            StorePath = "/";
            logger.LogTrace($"parsing the passed in mountpoint value: {mp}");

            if (!string.IsNullOrEmpty(mp) && mp.Trim() != "/" && mp.Trim() != "\\")
            {
                var splitmp = mp.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (splitmp.Length > 1)
                {
                    logger.LogTrace($"detected an included namespace {splitmp[0]}, storing for authentication.");
                    Namespace = splitmp[0].Trim();
                    MountPoint = splitmp[1].Trim();
                }
                else
                {
                    MountPoint = mp.TrimStart(new[] { '/' });
                }
            }
            if (!string.IsNullOrEmpty(subPath))
            {
                StorePath = subPath.Trim();
            }

            logger.LogTrace($"Directories to search (mount point): {MountPoint}");
            logger.LogTrace($"Enterprise Namespace: {Namespace}");
            logger.LogTrace($"Directories to ignore (subpath to search): {subPath}");
            InitProps(config.JobProperties, config.Capability);
        }
        public void Initialize(ManagementJobConfiguration config)
        {
            logger = LogHandler.GetClassLogger(GetType());

            ClientMachine = config.CertificateStoreDetails.ClientMachine;
            VaultServerUrl = PAMUtilities.ResolvePAMField(PamSecretResolver, logger, "Server UserName", config.ServerUsername);
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

            if (props.ContainsKey("StorePath"))
            {
                StorePath = props["StorePath"].ToString();
                StorePath = StorePath.TrimStart('/');
                StorePath = StorePath.TrimEnd('/');
                if (_storeType.Contains(StoreType.HCVKVPEM) || _storeType.Contains(StoreType.HCVPKI))
                {
                    StorePath += "/"; //ensure single trailing slash for path for PKI or PEM stores.  Others use the entry value instead of the container.
                }
            }

            var mp = props.ContainsKey("MountPoint") ? props["MountPoint"].ToString() : null;
            MountPoint = !string.IsNullOrEmpty(mp) ? mp : MountPoint;

            SubfolderInventory = props.ContainsKey("SubfolderInventory") ? bool.Parse(props["SubfolderInventory"].ToString()) : false;
            IncludeCertChain = props.ContainsKey("IncludeCertChain") ? bool.Parse(props["IncludeCertChain"].ToString()) : false;

            var isPki = _storeType.Contains("HCVPKI");

            if (!isPki)
            {
                VaultClient = new HcvKeyValueClient(VaultToken, VaultServerUrl, MountPoint, Namespace, StorePath, _storeType, SubfolderInventory);
            }
            else
            {
                VaultClient = new HcvKeyfactorClient(VaultToken, VaultServerUrl, MountPoint, StorePath);
            }
        }
    }
}