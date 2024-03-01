// Copyright 2023 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault
{
    static class AzureKeyVaultConstants
    {
        public const string KEY_VALUE_STORE_TYPE = "HCVKV";
        public const string PKI_STORE_TYPE = "HCV"; //same for Keyfactor plugin store type
    }

    static class JobType
    {
        public const string CREATE = "Create";
        public const string DISCOVERY = "Discovery";
        public const string INVENTORY = "Inventory";
        public const string MANAGEMENT = "Management";
        public const string REENROLLMENT = "Enrollment";
    }

    static class StoreType
    {
        public const string HCVKVPEM = "HCVKVPEM";
        public const string KCVKVJKS = "HCVKVJKS";
        public const string HCVKVPKCS12 = "HCVKVP12";
        public const string HCVKVPFX = "HCVKVPFX";
        public const string HCVPKI = "HCVPKI";
    }

    static class StoreFileExtensions
    {
        public const string HCVKVJKS = "_jks";
        public const string HCVKVPKCS12 = "_p12";
        public const string HCVKVPFX = "_pfx";
        public const string HCVKVPEM = "certificate";
        public static string ForStoreType(string type)
        {
            switch (type)
            {
                case StoreType.HCVKVPEM:
                    return HCVKVPEM;
                case StoreType.HCVKVPFX:
                    return HCVKVPFX;
                case StoreType.HCVKVPKCS12:
                    return HCVKVPKCS12;
                case StoreType.KCVKVJKS:
                    return HCVKVJKS;
                default:
                    return string.Empty;
            }
        }
    }

    static class CertificateHeaders {
        public const string PEM_HEADER = "-----BEGIN CERTIFICATE-----\n";
        public const string PEM_FOOTER = "\n-----END CERTIFICATE-----";    
    }
}
