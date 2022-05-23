using System;
using System.Collections.Generic;
using System.Text;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault
{
    static class AzureKeyVaultConstants
    {
        public const string STORE_TYPE_NAME = "HCV";
    }

    static class JobTypes
    {
        public const string CREATE = "Create";
        public const string DISCOVERY = "Discovery";
        public const string INVENTORY = "Inventory";
        public const string MANAGEMENT = "Management";
        public const string REENROLLMENT = "Enrollment";
    }
}
