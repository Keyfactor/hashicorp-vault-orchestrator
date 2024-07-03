<h1 align="center" style="border-bottom: none">
    for Hashicorp Vault Universal Orchestrator Extension
</h1>

<p align="center">
  <!-- Badges -->
<img src="https://img.shields.io/badge/integration_status-production-3D1973?style=flat-square" alt="Integration Status: production" />
<a href="https://github.com/Keyfactor/hashicorp-vault-orchestrator/releases"><img src="https://img.shields.io/github/v/release/Keyfactor/hashicorp-vault-orchestrator?style=flat-square" alt="Release" /></a>
<img src="https://img.shields.io/github/issues/Keyfactor/hashicorp-vault-orchestrator?style=flat-square" alt="Issues" />
<img src="https://img.shields.io/github/downloads/Keyfactor/hashicorp-vault-orchestrator/total?style=flat-square&label=downloads&color=28B905" alt="GitHub Downloads (all assets, all releases)" />
</p>

<p align="center">
  <!-- TOC -->
  <a href="#support">
    <b>Support</b>
  </a>
  ·
  <a href="#installation">
    <b>Installation</b>
  </a>
  ·
  <a href="#license">
    <b>License</b>
  </a>
  ·
  <a href="https://github.com/orgs/Keyfactor/repositories?q=orchestrator">
    <b>Related Integrations</b>
  </a>
</p>


## Overview

The Hashicorp Vault Universal Orchestrator extension allows users to manage cryptographic certificates in Hashicorp Vault through Keyfactor Command. Vault is a tool for securely accessing secrets and managing sensitive data, including certificates. This extension integrates with Keyfactor Command to facilitate the management of certificates stored in different secrets engines of Hashicorp Vault.

### Certificate Store Types

This extension supports three certificate store types across two secrets engines in Hashicorp Vault: Key-Value Store and PKI/Keyfactor Plugin.

#### Key-Value Store

The Key-Value Store type interacts with various key-value secrets engines in Vault, treating each stored file or path as a certificate store. There are four specific store types within the Key-Value Store:

- **HCVKVJKS**: Manages JKS certificate files, treating each file as a separate store.
- **HCVKVPFX**: Manages PFX certificate files, treating each file as a separate store.
- **HCVKVP12**: Manages PKCS12 certificate files, treating each file as a separate store.
- **HCVKVPEM**: Manages PEM-encoded certificates, treating each path as a store, with certificates located in sub-paths.

The supported operations in Key-Value Store types include discovery, inventory, management (add/remove), and creating new certificate stores.

#### PKI/Keyfactor Plugin

The Hashicorp PKI and Keyfactor Plugin secrets engines focus on managing certificates directly on the Vault instance. The store type for these engines is `HCVPKI`, which supports inventory operations.

In summary, the primary differences between these certificate store types lie in the specific formats and structures they manage, as well as the supported operations. The Key-Value Store types handle different certificate file formats and PEM-encoded certificates within specific paths, while the PKI/Keyfactor Plugin store type is geared towards managing certificates on the Vault instance itself.

## Compatibility

This integration is compatible with Keyfactor Universal Orchestrator version 10.1 and later.

## Support
The for Hashicorp Vault Universal Orchestrator extension is supported by Keyfactor for Keyfactor customers. If you have a support issue, please open a support ticket with your Keyfactor representative. If you have a support issue, please open a support ticket via the Keyfactor Support Portal at https://support.keyfactor.com. 
 
> To report a problem or suggest a new feature, use the **[Issues](../../issues)** tab. If you want to contribute actual bug fixes or proposed enhancements, use the **[Pull requests](../../pulls)** tab.

## Installation
Before installing the for Hashicorp Vault Universal Orchestrator extension, it's recommended to install [kfutil](https://github.com/Keyfactor/kfutil). Kfutil is a command-line tool that simplifies the process of creating store types, installing extensions, and instantiating certificate stores in Keyfactor Command.

The for Hashicorp Vault Universal Orchestrator extension implements 5 Certificate Store Types. Depending on your use case, you may elect to install one, or all of these Certificate Store Types. An overview for each type is linked below:
* [Hashicorp Vault Key-Value PEM](docs/hcvkvpem.md)
* [Hashicorp Vault PKI](docs/hcvpki.md)
* [Hashicorp Vault Key-Value JKS](docs/hcvkvjks.md)
* [Hashicorp Vault Key-Value PKCS12](docs/hcvkvp12.md)
* [Hashicorp Vault Key-Value PFX](docs/hcvkvpfx.md)

<details><summary>Hashicorp Vault Key-Value PEM</summary>


1. Follow the [requirements section](docs/hcvkvpem.md#requirements) to configure a Service Account and grant necessary API permissions.

    <details><summary>Requirements</summary>

    To configure the Hashicorp Vault Key-Value PEM Certificate Store Type, follow these steps:

    1. **Configure Hashicorp Vault:**
        - Ensure you have a running instance of Hashicorp Vault accessible by the Keyfactor Universal Orchestrator.
        - Configure the Key-Value secrets engine on your Vault instance if not already done. This can be achieved by running the command:
          ```bash
          vault secrets enable kv-v2
          ```
        - Create the path where the certificates will be stored within the Key-Value secrets engine, for example:
          ```bash
          vault kv put kv-v2/my-cert-path private_key="<base64-encoded-private-key>" certificate="<base64-encoded-certificate>"
          ```

    2. **Service Account Creation:**
        - Create a token with the necessary policies for accessing the Key-Value secrets engine. Ensure to provide the least privilege required for operations:
          ```bash
          vault token create -policy="<your-policy>"
          ```
        - The policy should include the following capabilities for certificate operations: `read`, `list`, `create`, `update`, `patch`, `delete` on the path of your certificates, and `list` capability on the `metadata` path.

    3. **Custom Fields in Keyfactor Command:**
        - When adding the certificate store type to Keyfactor Command, use the following field configuration:
          - **Client Machine**: Identifier for the orchestrator host (not used by the extension).
          - **Store Path**: The path where the PEM certificates will be stored within the Key-Value secrets engine (e.g., `/kv-v2/my-cert-path`).
          - **Mount Point**: The mount point name of the Key-Value secrets engine (default is `kv-v2`). Include the namespace if using Vault enterprise namespaces.
          - **Subfolder Inventory**: Set to `True` if inventory should include certificates in sub-paths; otherwise, set to `False`.
        
        ```json
        {
            "customFields": [
                {"name": "MountPoint", "type": "string"},
                {"name": "SubfolderInventory", "type": "bool", "optional": true},
                {"name": "IncludeCertChain", "type": "bool", "optional": true}
            ]
        }
        ```

    4. **Configure the Orchestrator Agent Machine:**
        - Stop the Orchestrator service (e.g., `KeyfactorOrchestrator-Default`).
        - Extract the Hashicorp Vault extension files into a new folder within the `extensions` directory of the orchestrator installation (e.g., `C:\Program Files\Keyfactor\Keyfactor Orchestrator\extensions\HCV`).
        - Restart the Orchestrator service.

    5. **Version Requirement:**
        - Ensure the orchestration system is compatible with the .NET Core 3.1 target framework.
        - The orchestrator must be able to connect to Keyfactor Command and the Hashicorp Vault instance.



    </details>

2. Create Certificate Store Types for the for Hashicorp Vault Orchestrator extension. 

    * **Using kfutil**:

        ```shell
        # Hashicorp Vault Key-Value PEM
        kfutil store-types create HCVKVPEM
        ```

    * **Manually**:
        * [Hashicorp Vault Key-Value PEM](docs/hcvkvpem.md#certificate-store-type-configuration)

3. Install the for Hashicorp Vault Universal Orchestrator extension.
    
    * **Using kfutil**: On the server that that hosts the Universal Orchestrator, run the following command:

        ```shell
        # Windows Server
        kfutil orchestrator extension -e hashicorp-vault-orchestrator@latest --out "C:\Program Files\Keyfactor\Keyfactor Orchestrator\extensions"

        # Linux
        kfutil orchestrator extension -e hashicorp-vault-orchestrator@latest --out "/opt/keyfactor/orchestrator/extensions"
        ```

    * **Manually**: Follow the [official Command documentation](https://software.keyfactor.com/Core-OnPrem/Current/Content/InstallingAgents/NetCoreOrchestrator/CustomExtensions.htm?Highlight=extensions) to install the latest [for Hashicorp Vault Universal Orchestrator extension](https://github.com/Keyfactor/hashicorp-vault-orchestrator/releases/latest).

4. Create new certificate stores in Keyfactor Command for the Sample Universal Orchestrator extension.

    * [Hashicorp Vault Key-Value PEM](docs/hcvkvpem.md#certificate-store-configuration)


</details>

<details><summary>Hashicorp Vault PKI</summary>


1. Follow the [requirements section](docs/hcvpki.md#requirements) to configure a Service Account and grant necessary API permissions.

    <details><summary>Requirements</summary>

    To configure the Hashicorp Vault PKI Certificate Store Type, follow these steps:

    1. **Configure Hashicorp Vault:**
        - Ensure you have a running instance of Hashicorp Vault accessible by the Keyfactor Universal Orchestrator.
        - Enable the PKI secret engine if it is not already enabled. This can be done using the command:
          ```bash
          vault secrets enable pki
          ```
        - Configure the PKI secret engine to generate certificates. This involves setting the URL for the CA and setting the maximum lease time for certificates:
          ```bash
          vault write pki/config/urls issuing_certificates="http://127.0.0.1:8200/v1/pki/ca" crl_distribution_points="http://127.0.0.1:8200/v1/pki/crl"
          vault write pki/root/generate/internal common_name="example.com" ttl=8760h
          ```

    2. **Service Account Creation:**
        - Create a token with the necessary policies for accessing the PKI secret engine. Ensure to provide the least privilege required for operations:
          ```bash
          vault token create -policy="<your-policy>"
          ```
        - The policy should include the following capabilities for certificate operations: `read`, `list` on the path of your certificates.

    3. **Custom Fields in Keyfactor Command:**
        - When adding the certificate store type to Keyfactor Command, use the following field configuration:
          - **Client Machine**: The URL for the Vault host machine.
          - **Store Path**: This should be set to `/`.
          - **Mount Point**: The mount point name for the instance of the PKI or Keyfactor plugins. If using the PKI plugin, the default is usually `pki`. If using the Keyfactor plugin, it corresponds to the configured mount point.
          - **Vault Token**: The access token that will be used by the orchestrator for requests to Vault.
          - **Vault Server URL**: The full URL and port of the Vault server instance.

        ```json
        {
            "customFields": [
                {"name": "MountPoint", "type": "string"},
                {"name": "VaultServerUrl", "type": "string", "required": true},
                {"name": "VaultToken", "type": "secret", "required": true}
            ]
        }
        ```

    4. **Configure the Orchestrator Agent Machine:**
        - Stop the Orchestrator service (e.g., `KeyfactorOrchestrator-Default`).
        - Extract the Hashicorp Vault extension files into a new folder within the `extensions` directory of the orchestrator installation (e.g., `C:\Program Files\Keyfactor\Keyfactor Orchestrator\extensions\HCV`).
        - Restart the Orchestrator service.

    5. **Version Requirement:**
        - Ensure the orchestration system is compatible with the .NET Core 3.1 target framework.
        - The orchestrator must be able to connect to Keyfactor Command and the Hashicorp Vault instance.



    </details>

2. Create Certificate Store Types for the for Hashicorp Vault Orchestrator extension. 

    * **Using kfutil**:

        ```shell
        # Hashicorp Vault PKI
        kfutil store-types create HCVPKI
        ```

    * **Manually**:
        * [Hashicorp Vault PKI](docs/hcvpki.md#certificate-store-type-configuration)

3. Install the for Hashicorp Vault Universal Orchestrator extension.
    
    * **Using kfutil**: On the server that that hosts the Universal Orchestrator, run the following command:

        ```shell
        # Windows Server
        kfutil orchestrator extension -e hashicorp-vault-orchestrator@latest --out "C:\Program Files\Keyfactor\Keyfactor Orchestrator\extensions"

        # Linux
        kfutil orchestrator extension -e hashicorp-vault-orchestrator@latest --out "/opt/keyfactor/orchestrator/extensions"
        ```

    * **Manually**: Follow the [official Command documentation](https://software.keyfactor.com/Core-OnPrem/Current/Content/InstallingAgents/NetCoreOrchestrator/CustomExtensions.htm?Highlight=extensions) to install the latest [for Hashicorp Vault Universal Orchestrator extension](https://github.com/Keyfactor/hashicorp-vault-orchestrator/releases/latest).

4. Create new certificate stores in Keyfactor Command for the Sample Universal Orchestrator extension.

    * [Hashicorp Vault PKI](docs/hcvpki.md#certificate-store-configuration)


</details>

<details><summary>Hashicorp Vault Key-Value JKS</summary>


1. Follow the [requirements section](docs/hcvkvjks.md#requirements) to configure a Service Account and grant necessary API permissions.

    <details><summary>Requirements</summary>

    To configure the Hashicorp Vault Key-Value JKS Certificate Store Type, follow these steps:

    1. **Configure Hashicorp Vault:**
        - Ensure you have a running instance of Hashicorp Vault accessible by the Keyfactor Universal Orchestrator.
        - Enable the Key-Value secrets engine if it is not already enabled. This can be done using the command:
          ```bash
          vault secrets enable kv-v2
          ```
        - Create the path where the JKS files will be stored within the Key-Value secrets engine. Each JKS file should be base64 encoded and stored with the proper key naming conventions (ending with `_jks`):
          ```bash
          vault kv put kv-v2/my-cert-path mycert_jks='<base64-encoded-jks>' passphrase='<store-passphrase>'
          ```

    2. **Service Account Creation:**
        - Create a token with the necessary policies for accessing the Key-Value secrets engine. Ensure to provide the least privilege required for operations:
          ```bash
          vault token create -policy="<your-policy>"
          ```
        - The policy should include the following capabilities for certificate operations: `read`, `list`, `create`, `update`, `patch`, `delete` on the path of your JKS files, and `list` capability on the `metadata` path.

    3. **Custom Fields in Keyfactor Command:**
        - When adding the certificate store type to Keyfactor Command, use the following field configuration:
          - **Client Machine**: Identifier for the orchestrator host (not used by the extension).
          - **Store Path**: The path where the JKS files will be stored within the Key-Value secrets engine (e.g., `/kv-v2/my-cert-path`).
          - **Mount Point**: The mount point name of the Key-Value secrets engine (default is `kv-v2`). Include the namespace if using Vault enterprise namespaces.
          - **Passphrase**: The passphrase for accessing the JKS file. This must be included for each JKS file.

        ```json
        {
            "customFields": [
                {"name": "MountPoint", "type": "string"},
                {"name": "Passphrase", "type": "secret", "required": true}
            ]
        }
        ```

    4. **Configure the Orchestrator Agent Machine:**
        - Stop the Orchestrator service (e.g., `KeyfactorOrchestrator-Default`).
        - Extract the Hashicorp Vault extension files into a new folder within the `extensions` directory of the orchestrator installation (e.g., `C:\Program Files\Keyfactor\Keyfactor Orchestrator\extensions\HCV`).
        - Restart the Orchestrator service.

    5. **Version Requirement:**
        - Ensure the orchestration system is compatible with the .NET Core 3.1 target framework.
        - The orchestrator must be able to connect to Keyfactor Command and the Hashicorp Vault instance.



    </details>

2. Create Certificate Store Types for the for Hashicorp Vault Orchestrator extension. 

    * **Using kfutil**:

        ```shell
        # Hashicorp Vault Key-Value JKS
        kfutil store-types create HCVKVJKS
        ```

    * **Manually**:
        * [Hashicorp Vault Key-Value JKS](docs/hcvkvjks.md#certificate-store-type-configuration)

3. Install the for Hashicorp Vault Universal Orchestrator extension.
    
    * **Using kfutil**: On the server that that hosts the Universal Orchestrator, run the following command:

        ```shell
        # Windows Server
        kfutil orchestrator extension -e hashicorp-vault-orchestrator@latest --out "C:\Program Files\Keyfactor\Keyfactor Orchestrator\extensions"

        # Linux
        kfutil orchestrator extension -e hashicorp-vault-orchestrator@latest --out "/opt/keyfactor/orchestrator/extensions"
        ```

    * **Manually**: Follow the [official Command documentation](https://software.keyfactor.com/Core-OnPrem/Current/Content/InstallingAgents/NetCoreOrchestrator/CustomExtensions.htm?Highlight=extensions) to install the latest [for Hashicorp Vault Universal Orchestrator extension](https://github.com/Keyfactor/hashicorp-vault-orchestrator/releases/latest).

4. Create new certificate stores in Keyfactor Command for the Sample Universal Orchestrator extension.

    * [Hashicorp Vault Key-Value JKS](docs/hcvkvjks.md#certificate-store-configuration)


</details>

<details><summary>Hashicorp Vault Key-Value PKCS12</summary>


1. Follow the [requirements section](docs/hcvkvp12.md#requirements) to configure a Service Account and grant necessary API permissions.

    <details><summary>Requirements</summary>

    To configure the Hashicorp Vault Key-Value PKCS12 Certificate Store Type, follow these steps:

    1. **Configure Hashicorp Vault:**
        - Ensure you have a running instance of Hashicorp Vault accessible by the Keyfactor Universal Orchestrator.
        - Enable the Key-Value secrets engine if it is not already enabled. This can be done using the command:
          ```bash
          vault secrets enable kv-v2
          ```
        - Create the path where the PKCS12 files will be stored within the Key-Value secrets engine. Each PKCS12 file should be base64 encoded and stored with the proper key naming conventions (ending with `_p12`):
          ```bash
          vault kv put kv-v2/my-cert-path mycert_p12='<base64-encoded-pkcs12>' passphrase='<store-passphrase>'
          ```

    2. **Service Account Creation:**
        - Create a token with the necessary policies for accessing the Key-Value secrets engine. Ensure to provide the least privilege required for operations:
          ```bash
          vault token create -policy="<your-policy>"
          ```
        - The policy should include the following capabilities for certificate operations: `read`, `list`, `create`, `update`, `patch`, `delete` on the path of your PKCS12 files, and `list` capability on the `metadata` path.

    3. **Custom Fields in Keyfactor Command:**
        - When adding the certificate store type to Keyfactor Command, use the following field configuration:
          - **Client Machine**: Identifier for the orchestrator host (not used by the extension).
          - **Store Path**: The path where the PKCS12 files will be stored within the Key-Value secrets engine (e.g., `/kv-v2/my-cert-path`).
          - **Mount Point**: The mount point name of the Key-Value secrets engine (default is `kv-v2`). Include the namespace if using Vault enterprise namespaces.
          - **Passphrase**: The passphrase for accessing the PKCS12 file. This must be included for each PKCS12 file.

        ```json
        {
            "customFields": [
                {"name": "MountPoint", "type": "string"},
                {"name": "Passphrase", "type": "secret", "required": true}
            ]
        }
        ```

    4. **Configure the Orchestrator Agent Machine:**
        - Stop the Orchestrator service (e.g., `KeyfactorOrchestrator-Default`).
        - Extract the Hashicorp Vault extension files into a new folder within the `extensions` directory of the orchestrator installation (e.g., `C:\Program Files\Keyfactor\Keyfactor Orchestrator\extensions\HCV`).
        - Restart the Orchestrator service.

    5. **Version Requirement:**
        - Ensure the orchestration system is compatible with the .NET Core 3.1 target framework.
        - The orchestrator must be able to connect to Keyfactor Command and the Hashicorp Vault instance.



    </details>

2. Create Certificate Store Types for the for Hashicorp Vault Orchestrator extension. 

    * **Using kfutil**:

        ```shell
        # Hashicorp Vault Key-Value PKCS12
        kfutil store-types create HCVKVP12
        ```

    * **Manually**:
        * [Hashicorp Vault Key-Value PKCS12](docs/hcvkvp12.md#certificate-store-type-configuration)

3. Install the for Hashicorp Vault Universal Orchestrator extension.
    
    * **Using kfutil**: On the server that that hosts the Universal Orchestrator, run the following command:

        ```shell
        # Windows Server
        kfutil orchestrator extension -e hashicorp-vault-orchestrator@latest --out "C:\Program Files\Keyfactor\Keyfactor Orchestrator\extensions"

        # Linux
        kfutil orchestrator extension -e hashicorp-vault-orchestrator@latest --out "/opt/keyfactor/orchestrator/extensions"
        ```

    * **Manually**: Follow the [official Command documentation](https://software.keyfactor.com/Core-OnPrem/Current/Content/InstallingAgents/NetCoreOrchestrator/CustomExtensions.htm?Highlight=extensions) to install the latest [for Hashicorp Vault Universal Orchestrator extension](https://github.com/Keyfactor/hashicorp-vault-orchestrator/releases/latest).

4. Create new certificate stores in Keyfactor Command for the Sample Universal Orchestrator extension.

    * [Hashicorp Vault Key-Value PKCS12](docs/hcvkvp12.md#certificate-store-configuration)


</details>

<details><summary>Hashicorp Vault Key-Value PFX</summary>


1. Follow the [requirements section](docs/hcvkvpfx.md#requirements) to configure a Service Account and grant necessary API permissions.

    <details><summary>Requirements</summary>

    To configure the Hashicorp Vault Key-Value PFX Certificate Store Type, follow these steps:

    1. **Configure Hashicorp Vault:**
        - Ensure you have a running instance of Hashicorp Vault accessible by the Keyfactor Universal Orchestrator.
        - Enable the Key-Value secrets engine if it is not already enabled. This can be done using the command:
          ```bash
          vault secrets enable kv-v2
          ```
        - Create the path where the PFX files will be stored within the Key-Value secrets engine. Each PFX file should be base64 encoded and stored with the proper key naming conventions (ending with `_pfx`):
          ```bash
          vault kv put kv-v2/my-cert-path mycert_pfx='<base64-encoded-pfx>' passphrase='<store-passphrase>'
          ```

    2. **Service Account Creation:**
        - Create a token with the necessary policies for accessing the Key-Value secrets engine. Ensure to provide the least privilege required for operations:
          ```bash
          vault token create -policy="<your-policy>"
          ```
        - The policy should include the following capabilities for certificate operations: `read`, `list`, `create`, `update`, `patch`, `delete` on the path of your PFX files, and `list` capability on the `metadata` path.

    3. **Custom Fields in Keyfactor Command:**
        - When adding the certificate store type to Keyfactor Command, use the following field configuration:
          - **Client Machine**: Identifier for the orchestrator host (not used by the extension).
          - **Store Path**: The path where the PFX files will be stored within the Key-Value secrets engine (e.g., `/kv-v2/my-cert-path`).
          - **Mount Point**: The mount point name of the Key-Value secrets engine (default is `kv-v2`). Include the namespace if using Vault enterprise namespaces.
          - **Passphrase**: The passphrase for accessing the PFX file. This must be included for each PFX file.

        ```json
        {
            "customFields": [
                {"name": "MountPoint", "type": "string"},
                {"name": "Passphrase", "type": "secret", "required": true}
            ]
        }
        ```

    4. **Configure the Orchestrator Agent Machine:**
        - Stop the Orchestrator service (e.g., `KeyfactorOrchestrator-Default`).
        - Extract the Hashicorp Vault extension files into a new folder within the `extensions` directory of the orchestrator installation (e.g., `C:\Program Files\Keyfactor\Keyfactor Orchestrator\extensions\HCV`).
        - Restart the Orchestrator service.

    5. **Version Requirement:**
        - Ensure the orchestration system is compatible with the .NET Core 3.1 target framework.
        - The orchestrator must be able to connect to Keyfactor Command and the Hashicorp Vault instance.



    </details>

2. Create Certificate Store Types for the for Hashicorp Vault Orchestrator extension. 

    * **Using kfutil**:

        ```shell
        # Hashicorp Vault Key-Value PFX
        kfutil store-types create HCVKVPFX
        ```

    * **Manually**:
        * [Hashicorp Vault Key-Value PFX](docs/hcvkvpfx.md#certificate-store-type-configuration)

3. Install the for Hashicorp Vault Universal Orchestrator extension.
    
    * **Using kfutil**: On the server that that hosts the Universal Orchestrator, run the following command:

        ```shell
        # Windows Server
        kfutil orchestrator extension -e hashicorp-vault-orchestrator@latest --out "C:\Program Files\Keyfactor\Keyfactor Orchestrator\extensions"

        # Linux
        kfutil orchestrator extension -e hashicorp-vault-orchestrator@latest --out "/opt/keyfactor/orchestrator/extensions"
        ```

    * **Manually**: Follow the [official Command documentation](https://software.keyfactor.com/Core-OnPrem/Current/Content/InstallingAgents/NetCoreOrchestrator/CustomExtensions.htm?Highlight=extensions) to install the latest [for Hashicorp Vault Universal Orchestrator extension](https://github.com/Keyfactor/hashicorp-vault-orchestrator/releases/latest).

4. Create new certificate stores in Keyfactor Command for the Sample Universal Orchestrator extension.

    * [Hashicorp Vault Key-Value PFX](docs/hcvkvpfx.md#certificate-store-configuration)


</details>


## License

Apache License 2.0, see [LICENSE](LICENSE).

## Related Integrations

See all [Keyfactor Universal Orchestrator extensions](https://github.com/orgs/Keyfactor/repositories?q=orchestrator).