
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault
{
    public class JobProperties
    {
        public string StorePath { get; set; }
        public string CertSecretPath => StorePath.Split('?')[0]; // everything before the optional ? is the path to the cert secret
        public string CertSecretPropName => StorePath.Split('?').Length > 1 ? StorePath.Split('?')[1] : string.Empty; // anything after the ? is the optional property name within the secret for the certificate
        public string VaultToken { get; set; }
        public string ClientMachine { get; set; }
        public string VaultServerUrl { get; set; }
        public string PassphrasePath { get; set; }
        public string PassphraseSecretPath => PassphrasePath?.Split('?')[0] ?? string.Empty; // everything before the optional ? is the path to the cert store password secret. null when not configured (e.g. HCVKVPEM with no private key).
        public string PassphraseSecretPropName => PassphrasePath != null && PassphrasePath.Split('?').Length > 1 ? PassphrasePath.Split('?')[1] : string.Empty; // anything after the ? is the optional property name within the secret for the password
        public bool IncludeCertChain { get; set; }
        public string MountPoint { get; set; } // the mount point of the KV secrets engine.  defaults to kv-v2 if not provided.
        public string Namespace { get; set; } // for enterprise editions of vault that utilize namespaces; split from the passed in mount point if needed.
        public string DiscoverySuffix { get; set; } // overrides the default secret-key-name suffix used to identify candidate secrets during a Discovery job.
    }
}
