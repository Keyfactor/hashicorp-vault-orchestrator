namespace Keyfactor.Extensions.Orchestrator.HashicorpVault
{
    public class JobProperties
    {
        public string StorePath { get; set; }
        public string CertSecretPath => StorePath.Split('?')[0]; // everything before the optional ? is the path to the cert secret
        public string CertSecretPropName => StorePath.Split('?')[1] ?? string.Empty; // anything after the ? is the optional property name within the secret for the certificate
        public string VaultToken { get; set; }
        public string ClientMachine { get; set; }
        public string VaultServerUrl { get; set; }
        public string PassphrasePath { get; set; }
        public string PassphraseSecretPath => PassphrasePath.Split('?')[0] ?? string.Empty; // everything before the optional ? is the path to the cert store password secret
        public string PassphraseSecretPropName => PassphraseSecretPath.Split('?')[1] ?? string.Empty; // anything after the ? is the optional property name within the secret for the password
        public bool SubfolderInventory { get; set; }
        public bool IncludeCertChain { get; set; }
        public string MountPoint { get; set; } // the mount point of the KV secrets engine.  defaults to kv-v2 if not provided.
        public string Namespace { get; set; } // for enterprise editions of vault that utilize namespaces; split from the passed in mount point if needed. 
    }
}
