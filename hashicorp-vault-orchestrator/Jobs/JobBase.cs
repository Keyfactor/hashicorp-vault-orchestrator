
//  Copyright 2025 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

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
        public JobProperties JobParameters { get; set; }
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
            JobParameters = new JobProperties();

            JobParameters.ClientMachine = config.CertificateStoreDetails.ClientMachine;
            JobParameters.MountPoint = "kv-v2"; // default

            JobParameters.VaultServerUrl = PAMUtilities.ResolvePAMField(PamSecretResolver, logger, "Server UserName", config.ServerUsername);

            JobParameters.VaultToken = PAMUtilities.ResolvePAMField(PamSecretResolver, logger, "Server Password", config.ServerPassword);

            JobParameters.StorePath = config.CertificateStoreDetails.StorePath;
            JobParameters.ClientMachine = config.CertificateStoreDetails.ClientMachine;

            var props = JsonConvert.DeserializeObject<Dictionary<string, object>>(config.CertificateStoreDetails.Properties);

            InitProps(props, config.Capability);
        }

        public void Initialize(DiscoveryJobConfiguration config)
        {
            logger = LogHandler.GetClassLogger(GetType());
            JobParameters = new JobProperties();

            JobParameters.ClientMachine = config.ClientMachine;

            JobParameters.VaultServerUrl = PAMUtilities.ResolvePAMField(PamSecretResolver, logger, "Server UserName", config.ServerUsername);

            JobParameters.VaultToken = PAMUtilities.ResolvePAMField(PamSecretResolver, logger, "Server Password", config.ServerPassword);

            var subPath = config.JobProperties?["dirs"] as string;
            var mp = config.JobProperties?["extensions"] as string;

            // Discovery jobs need to pass the sub-paths in the "directories to search" field.
            // The mount point and namespace should be passed in the "Extensions" field.
            // if nothing is provided, we default to mount point: "kv-v2" and no namespace.

            JobParameters.StorePath = "/";
            logger.LogTrace($"parsing the passed in mountpoint value: {mp}");

            if (!string.IsNullOrEmpty(mp) && mp.Trim() != "/" && mp.Trim() != "\\")
            {
                var splitmp = mp.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (splitmp.Length > 1)
                {
                    logger.LogTrace($"detected an included namespace {splitmp[0]}, storing for authentication.");
                    JobParameters.Namespace = splitmp[0].Trim();
                    JobParameters.MountPoint = splitmp[1].Trim();
                }
                else
                {
                    JobParameters.MountPoint = mp.TrimStart(new[] { '/' });
                }
            }
            if (!string.IsNullOrEmpty(subPath))
            {
                JobParameters.StorePath = subPath.Trim();
            }

            logger.LogTrace($"Directories to search (mount point): {JobParameters.MountPoint}");
            logger.LogTrace($"Enterprise Namespace: {JobParameters.Namespace}");
            logger.LogTrace($"Directories to ignore (subpath to search): {subPath}");
            InitProps(config.JobProperties, config.Capability);
        }
        public void Initialize(ManagementJobConfiguration config)
        {
            logger = LogHandler.GetClassLogger(GetType());

            JobParameters.ClientMachine = config.CertificateStoreDetails.ClientMachine;
            JobParameters.VaultServerUrl = PAMUtilities.ResolvePAMField(PamSecretResolver, logger, "Server UserName", config.ServerUsername);
            JobParameters.VaultToken = PAMUtilities.ResolvePAMField(PamSecretResolver, logger, "Server Password", config.ServerPassword);
            JobParameters.StorePath = config.CertificateStoreDetails.StorePath;
            dynamic props = JsonConvert.DeserializeObject(config.CertificateStoreDetails.Properties.ToString());
            InitProps(props, config.Capability);
        }

        private void InitProps(dynamic props, string capability)
        {
            _storeType = capability;

            if (props == null) throw new Exception("Properties is null");

            if (props.ContainsKey("StorePath"))
            {
                JobParameters.StorePath = props["StorePath"].ToString();
                JobParameters.StorePath = JobParameters.StorePath.TrimStart('/');
                JobParameters.StorePath = JobParameters.StorePath.TrimEnd('/');
                if (_storeType.Contains(StoreType.HCVKVPEM) || _storeType.Contains(StoreType.HCVPKI))
                {
                    JobParameters.StorePath += "/"; //ensure single trailing slash for path for PKI or PEM stores.  Others use the entry value instead of the container.
                }
            }

            var mp = props.ContainsKey("MountPoint") ? props["MountPoint"].ToString() : null;
            JobParameters.MountPoint = !string.IsNullOrEmpty(mp) ? mp : JobParameters.MountPoint;

            JobParameters.SubfolderInventory = props.ContainsKey("SubfolderInventory") ? bool.Parse(props["SubfolderInventory"].ToString()) : false;
            JobParameters.IncludeCertChain = props.ContainsKey("IncludeCertChain") ? bool.Parse(props["IncludeCertChain"].ToString()) : false;

            var isPki = _storeType.Contains("HCVPKI");

            if (!isPki)
            {
                VaultClient = new HcvKeyValueClient(JobParameters.VaultToken, JobParameters.VaultServerUrl, JobParameters.MountPoint, JobParameters.Namespace, _storeType, JobParameters.StorePath, JobParameters.CertSecretPropName, JobParameters.PassphrasePath, JobParameters.PassphraseSecretPropName, JobParameters.SubfolderInventory);
            }
            else
            {
                VaultClient = new HcvKeyfactorClient(JobParameters.VaultToken, JobParameters.VaultServerUrl, JobParameters.MountPoint, JobParameters.StorePath);
            }
        }
    }
}