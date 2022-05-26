namespace Keyfactor.Extensions.Orchestrator.HashicorpVault
{
    public interface ISecretCert
    {
        public string PUBLIC_KEY { get; set; }

        public string PRIVATE_KEY { get; set; }

        public string KEY_SECRET { get; set; }
    }
}
