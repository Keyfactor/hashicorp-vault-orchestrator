namespace Keyfactor.Extensions.Orchestrator.HashicorpVault.Jobs
{
    public abstract class JobBase
    {
        public string ExtensionName => "HCV";

        internal protected string StorePath { get; set; }

        internal protected string VaultToken { get; set; }

        internal protected string VaultServerUrl { get; set; }

        internal protected string MountPoint { get; set; } // the mount point of the KV secrets engine.  defaults to KV

        internal protected HcvClient VaultClient { get; set; }


        public void InitializeStore(dynamic config)
        {
            VaultToken = config.VaultToken;
            VaultServerUrl = config.VaultServerUrl;
            MountPoint = config.MountPoint;

            if (config.GetType().GetProperty("CertificateStoreDetails") != null)
            {
                StorePath = config.CertificateStoreDetails.StorePath;
            }

        }
    }
}
