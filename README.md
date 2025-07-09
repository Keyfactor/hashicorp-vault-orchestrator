<h1 align="center" style="border-bottom: none">
    Hashicorp Vault Universal Orchestrator Extension
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

The Hashicorp Vault Universal Orchestrator extension implements 5 Certificate Store Types. Depending on your use case, you may elect to use one, or all of these Certificate Store Types. Descriptions of each are provided below.

- [Hashicorp Vault Key-Value PEM](#HCVKVPEM)

- [Hashicorp Vault PKI](#HCVPKI)

- [Hashicorp Vault Key-Value JKS](#HCVKVJKS)

- [Hashicorp Vault Key-Value PKCS12](#HCVKVP12)

- [Hashicorp Vault Key-Value PFX](#HCVKVPFX)


## Compatibility

This integration is compatible with Keyfactor Universal Orchestrator version 10.1 and later.

## Support
The Hashicorp Vault Universal Orchestrator extension If you have a support issue, please open a support ticket by either contacting your Keyfactor representative or via the Keyfactor Support Portal at https://support.keyfactor.com.

> To report a problem or suggest a new feature, use the **[Issues](../../issues)** tab. If you want to contribute actual bug fixes or proposed enhancements, use the **[Pull requests](../../pulls)** tab.

## Requirements & Prerequisites

Before installing the Hashicorp Vault Universal Orchestrator extension, we recommend that you install [kfutil](https://github.com/Keyfactor/kfutil). Kfutil is a command-line tool that simplifies the process of creating store types, installing extensions, and instantiating certificate stores in Keyfactor Command.



## Certificate Store Types

To use the Hashicorp Vault Universal Orchestrator extension, you **must** create the Certificate Store Types required for your use-case. This only needs to happen _once_ per Keyfactor Command instance.

The Hashicorp Vault Universal Orchestrator extension implements 5 Certificate Store Types. Depending on your use case, you may elect to use one, or all of these Certificate Store Types.

### HCVKVPEM

<details><summary>Click to expand details</summary>


The Hashicorp Vault Key-Value PEM Certificate Store Type allows users to manage PEM-encoded certificates stored in Hashicorp Vault using the Key-Value secrets engine. This store type treats each path in the Key-Value store as a certificate store, with individual certificates residing in sub-paths. It supports various operations, including discovery, inventory, certificate addition, certificate removal, and creating new certificate stores.

#### Representation and Functionality

The Hashicorp Vault Key-Value PEM Certificate Store Type represents a hierarchical structure where the root path defined in the store contains multiple sub-paths, each potentially holding separate certificates and their associated private keys. This design enables users to manage large collections of PEM-encoded certificates efficiently and flexibly.

#### Caveats and Considerations

There are several important considerations to keep in mind when using this Certificate Store Type:

- **Sub-Folder Inventory:** Users can configure the store to include or exclude sub-folders during inventory operations. Setting 'Subfolder Inventory' to 'True' will inventory certificates in both the root path and its sub-paths. Conversely, setting it to 'False' will limit the inventory to certificates in the root path only. This flexibility helps avoid duplication and manage the store size effectively.
- **Base64 Encoding:** All certificates and private keys in the Key-Value store must be base64 encoded. Incorrect encoding can lead to errors during inventory and management operations.
- **Complex Path Management:** The hierarchical nature of the Key-Value PEM Certificate Store Type necessitates careful path management to avoid redundancy and ensure accurate inventory. Users should be cautious about the path configurations to prevent the same certificate from appearing in multiple stores.

#### Limitations and Potential Confusion

The primary limitation of this store type is the potential complexity in managing a large number of certificates within a hierarchical path structure. Additionally, users need to ensure that all PEM-encoded certificates and keys are correctly base64 encoded and correctly named to be recognized during inventory scans. If the required fields such as 'private_key' for PEM-encoded certificates are not present, those entries will be ignored during inventory scans.

#### SDK Use

The documentation does not explicitly mention the use of an SDK for this Certificate Store Type. However, users interact with the Hashicorp Vault API to perform required operations, implying that some form of API client or service is in use by the Keyfactor Command orchestrator.

#### Summary

The Hashicorp Vault Key-Value PEM Certificate Store Type is a powerful extension for managing PEM-encoded certificates stored in a hierarchical structure within Vault's Key-Value secrets engine. While it offers significant flexibility and efficiency, it also demands careful management of paths and proper encoding to avoid errors and ensure smooth operation.




#### Hashicorp Vault Key-Value PEM Requirements

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
    - Ensure the orchestration system is compatible with the .NET 6 or .NET 8 framework
    - The orchestrator must be able to connect to Keyfactor Command and the Hashicorp Vault instance.



#### Supported Operations

| Operation    | Is Supported                                                                                                           |
|--------------|------------------------------------------------------------------------------------------------------------------------|
| Add          | ✅ Checked        |
| Remove       | ✅ Checked     |
| Discovery    | ✅ Checked  |
| Reenrollment | 🔲 Unchecked |
| Create       | ✅ Checked     |

#### Store Type Creation

##### Using kfutil:
`kfutil` is a custom CLI for the Keyfactor Command API and can be used to create certificate store types.
For more information on [kfutil](https://github.com/Keyfactor/kfutil) check out the [docs](https://github.com/Keyfactor/kfutil?tab=readme-ov-file#quickstart)
   <details><summary>Click to expand HCVKVPEM kfutil details</summary>

   ##### Using online definition from GitHub:
   This will reach out to GitHub and pull the latest store-type definition
   ```shell
   # Hashicorp Vault Key-Value PEM
   kfutil store-types create HCVKVPEM
   ```

   ##### Offline creation using integration-manifest file:
   If required, it is possible to create store types from the [integration-manifest.json](./integration-manifest.json) included in this repo.
   You would first download the [integration-manifest.json](./integration-manifest.json) and then run the following command
   in your offline environment.
   ```shell
   kfutil store-types create --from-file integration-manifest.json
   ```
   </details>


#### Manual Creation
Below are instructions on how to create the HCVKVPEM store type manually in
the Keyfactor Command Portal
   <details><summary>Click to expand manual HCVKVPEM details</summary>

   Create a store type called `HCVKVPEM` with the attributes in the tables below:

   ##### Basic Tab
   | Attribute | Value | Description |
   | --------- | ----- | ----- |
   | Name | Hashicorp Vault Key-Value PEM | Display name for the store type (may be customized) |
   | Short Name | HCVKVPEM | Short display name for the store type |
   | Capability | HCVKVPEM | Store type name orchestrator will register with. Check the box to allow entry of value |
   | Supports Add | ✅ Checked | Check the box. Indicates that the Store Type supports Management Add |
   | Supports Remove | ✅ Checked | Check the box. Indicates that the Store Type supports Management Remove |
   | Supports Discovery | ✅ Checked | Check the box. Indicates that the Store Type supports Discovery |
   | Supports Reenrollment | 🔲 Unchecked |  Indicates that the Store Type supports Reenrollment |
   | Supports Create | ✅ Checked | Check the box. Indicates that the Store Type supports store creation |
   | Needs Server | ✅ Checked | Determines if a target server name is required when creating store |
   | Blueprint Allowed | 🔲 Unchecked | Determines if store type may be included in an Orchestrator blueprint |
   | Uses PowerShell | 🔲 Unchecked | Determines if underlying implementation is PowerShell |
   | Requires Store Password | 🔲 Unchecked | Enables users to optionally specify a store password when defining a Certificate Store. |
   | Supports Entry Password | 🔲 Unchecked | Determines if an individual entry within a store can have a password. |

   The Basic tab should look like this:

   ![HCVKVPEM Basic Tab](docsource/images/HCVKVPEM-basic-store-type-dialog.png)

   ##### Advanced Tab
   | Attribute | Value | Description |
   | --------- | ----- | ----- |
   | Supports Custom Alias | Optional | Determines if an individual entry within a store can have a custom Alias. |
   | Private Key Handling | Optional | This determines if Keyfactor can send the private key associated with a certificate to the store. Required because IIS certificates without private keys would be invalid. |
   | PFX Password Style | Default | 'Default' - PFX password is randomly generated, 'Custom' - PFX password may be specified when the enrollment job is created (Requires the Allow Custom Password application setting to be enabled.) |

   The Advanced tab should look like this:

   ![HCVKVPEM Advanced Tab](docsource/images/HCVKVPEM-advanced-store-type-dialog.png)

   > For Keyfactor **Command versions 24.4 and later**, a Certificate Format dropdown is available with PFX and PEM options. Ensure that **PFX** is selected, as this determines the format of new and renewed certificates sent to the Orchestrator during a Management job. Currently, all Keyfactor-supported Orchestrator extensions support only PFX.

   ##### Custom Fields Tab
   Custom fields operate at the certificate store level and are used to control how the orchestrator connects to the remote target server containing the certificate store to be managed. The following custom fields should be added to the store type:

   | Name | Display Name | Description | Type | Default Value/Options | Required |
   | ---- | ------------ | ---- | --------------------- | -------- | ----------- |

   The Custom Fields tab should look like this:

   ![HCVKVPEM Custom Fields Tab](docsource/images/HCVKVPEM-custom-fields-store-type-dialog.png)

   </details>
</details>

### HCVPKI

<details><summary>Click to expand details</summary>


The Hashicorp Vault PKI Certificate Store Type allows users to manage and inventory certificates directly on a Hashicorp Vault instance using the PKI or Keyfactor Plugin secrets engines. This store type is intended for managing certificates issued and stored within the Vault's PKI, enabling seamless integration with Keyfactor Command for efficient certificate lifecycle management.

#### Representation and Functionality

The Hashicorp Vault PKI Certificate Store Type represents a path within Vault where certificates are stored and managed using either the native PKI engine or the Keyfactor Secrets Engine plugin. This configuration allows for streamlined certificate management, including inventory operations to keep track of all certificates within a specified Vault path.

#### Caveats and Considerations

There are a few considerations to be aware of when using this store type:

- **Mount Point Configuration:** It's crucial to correctly specify the mount point for the PKI or Keyfactor Plugin secrets engines. This ensures that the orchestrator can accurately access and manage the certificates.
- **Vault Token Requirements:** The token used for Vault interactions must be configured with appropriate policies to permit read and list operations on the certificate path. Incorrect or insufficient permissions will impede the functionality of the certificate store.

#### Limitations and Potential Confusion

The Hashicorp Vault PKI Certificate Store Type primarily supports inventory operations. This is a limitation to note if you require more extensive management capabilities such as adding or removing certificates. Additionally, users need to be careful with the path configurations to ensure accurate inventory results and avoid potential errors.

#### SDK Use

While the documentation does not explicitly mention the use of an SDK, interactions are performed through the Hashicorp Vault API, implying that API clients or services are employed by the Keyfactor Command orchestrator to facilitate required operations.

#### Summary

In summary, the Hashicorp Vault PKI Certificate Store Type is specialized for managing certificates stored in Vault's PKI or Keyfactor Plugin secrets engines. It focuses on inventory operations, representing a specific path within the Vault. Proper configuration of mount points and Vault tokens is essential for proper operation, and while it provides robust inventory capabilities, users should be aware of its limitations regarding additional management operations.




#### Hashicorp Vault PKI Requirements

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
    - Ensure the orchestration system is compatible with the .NET 6 or .NET 8 framework
    - The orchestrator must be able to connect to Keyfactor Command and the Hashicorp Vault instance.



#### Supported Operations

| Operation    | Is Supported                                                                                                           |
|--------------|------------------------------------------------------------------------------------------------------------------------|
| Add          | 🔲 Unchecked        |
| Remove       | 🔲 Unchecked     |
| Discovery    | 🔲 Unchecked  |
| Reenrollment | 🔲 Unchecked |
| Create       | 🔲 Unchecked     |

#### Store Type Creation

##### Using kfutil:
`kfutil` is a custom CLI for the Keyfactor Command API and can be used to create certificate store types.
For more information on [kfutil](https://github.com/Keyfactor/kfutil) check out the [docs](https://github.com/Keyfactor/kfutil?tab=readme-ov-file#quickstart)
   <details><summary>Click to expand HCVPKI kfutil details</summary>

   ##### Using online definition from GitHub:
   This will reach out to GitHub and pull the latest store-type definition
   ```shell
   # Hashicorp Vault PKI
   kfutil store-types create HCVPKI
   ```

   ##### Offline creation using integration-manifest file:
   If required, it is possible to create store types from the [integration-manifest.json](./integration-manifest.json) included in this repo.
   You would first download the [integration-manifest.json](./integration-manifest.json) and then run the following command
   in your offline environment.
   ```shell
   kfutil store-types create --from-file integration-manifest.json
   ```
   </details>


#### Manual Creation
Below are instructions on how to create the HCVPKI store type manually in
the Keyfactor Command Portal
   <details><summary>Click to expand manual HCVPKI details</summary>

   Create a store type called `HCVPKI` with the attributes in the tables below:

   ##### Basic Tab
   | Attribute | Value | Description |
   | --------- | ----- | ----- |
   | Name | Hashicorp Vault PKI | Display name for the store type (may be customized) |
   | Short Name | HCVPKI | Short display name for the store type |
   | Capability | HCVPKI | Store type name orchestrator will register with. Check the box to allow entry of value |
   | Supports Add | 🔲 Unchecked |  Indicates that the Store Type supports Management Add |
   | Supports Remove | 🔲 Unchecked |  Indicates that the Store Type supports Management Remove |
   | Supports Discovery | 🔲 Unchecked |  Indicates that the Store Type supports Discovery |
   | Supports Reenrollment | 🔲 Unchecked |  Indicates that the Store Type supports Reenrollment |
   | Supports Create | 🔲 Unchecked |  Indicates that the Store Type supports store creation |
   | Needs Server | 🔲 Unchecked | Determines if a target server name is required when creating store |
   | Blueprint Allowed | 🔲 Unchecked | Determines if store type may be included in an Orchestrator blueprint |
   | Uses PowerShell | 🔲 Unchecked | Determines if underlying implementation is PowerShell |
   | Requires Store Password | 🔲 Unchecked | Enables users to optionally specify a store password when defining a Certificate Store. |
   | Supports Entry Password | 🔲 Unchecked | Determines if an individual entry within a store can have a password. |

   The Basic tab should look like this:

   ![HCVPKI Basic Tab](docsource/images/HCVPKI-basic-store-type-dialog.png)

   ##### Advanced Tab
   | Attribute | Value | Description |
   | --------- | ----- | ----- |
   | Supports Custom Alias | Optional | Determines if an individual entry within a store can have a custom Alias. |
   | Private Key Handling | Optional | This determines if Keyfactor can send the private key associated with a certificate to the store. Required because IIS certificates without private keys would be invalid. |
   | PFX Password Style | Default | 'Default' - PFX password is randomly generated, 'Custom' - PFX password may be specified when the enrollment job is created (Requires the Allow Custom Password application setting to be enabled.) |

   The Advanced tab should look like this:

   ![HCVPKI Advanced Tab](docsource/images/HCVPKI-advanced-store-type-dialog.png)

   > For Keyfactor **Command versions 24.4 and later**, a Certificate Format dropdown is available with PFX and PEM options. Ensure that **PFX** is selected, as this determines the format of new and renewed certificates sent to the Orchestrator during a Management job. Currently, all Keyfactor-supported Orchestrator extensions support only PFX.

   ##### Custom Fields Tab
   Custom fields operate at the certificate store level and are used to control how the orchestrator connects to the remote target server containing the certificate store to be managed. The following custom fields should be added to the store type:

   | Name | Display Name | Description | Type | Default Value/Options | Required |
   | ---- | ------------ | ---- | --------------------- | -------- | ----------- |

   The Custom Fields tab should look like this:

   ![HCVPKI Custom Fields Tab](docsource/images/HCVPKI-custom-fields-store-type-dialog.png)

   </details>
</details>

### HCVKVJKS

<details><summary>Click to expand details</summary>


The Hashicorp Vault Key-Value JKS Certificate Store Type allows users to manage Java KeyStore (JKS) files stored within Hashicorp Vault using the Key-Value secrets engine. This store type treats each JKS file as a separate certificate store, enabling fine-grained management of these files through Keyfactor Command. It supports various operations such as discovery, inventory, and the addition and removal of certificates within the JKS files.

#### Representation and Functionality

The Hashicorp Vault Key-Value JKS Certificate Store Type represents individual JKS files stored in the Vault's Key-Value secrets engine. Each JKS file is treated as an independent store, making it easy to manage multiple JKS files systematically. This interaction ensures that each JKS file contains a base64-encoded certificate and an accompanying passphrase stored under specific keys.

#### Caveats and Considerations

There are several important considerations when using this Certificate Store Type:

- **Base64 Encoding:** All JKS files must be base64 encoded before being stored in Vault. This encoding ensures that the files can be accurately recognized and managed.
- **Key Naming Convention:** The name (key) for each JKS entry must end with the suffix '_jks' to be correctly identified during operations. This convention is crucial for proper inventory and management.
- **Passphrase Requirement:** Each JKS file entry must include a `passphrase` field containing the password for the store. Without this, the JKS file will be ignored during inventory scans, potentially leading to incomplete results.

#### Limitations and Potential Confusion

The primary limitation of the Key-Value JKS Certificate Store Type is its dependence on strict naming conventions and base64 encoding. Users must ensure that each entry is correctly named and encoded to avoid errors during management operations. Additionally, the correct inclusion of the `passphrase` field is crucial for successful inventory and management.

#### SDK Use

While not explicitly mentioned in the documentation, interactions are performed through the Hashicorp Vault API, implying that the Keyfactor Command orchestrator utilizes some API client to facilitate the required operations.

#### Summary

In summary, the Hashicorp Vault Key-Value JKS Certificate Store Type offers a robust solution for managing JKS files within the Key-Value secrets engine of Hashicorp Vault. Representing each JKS file as an independent store enhances manageability and organization. However, users must be mindful of the necessary base64 encoding, strict naming conventions, and the inclusion of passphrases to ensure smooth operations and accurate results.




#### Hashicorp Vault Key-Value JKS Requirements

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
    - - Ensure the orchestration system is compatible with the .NET 6 or .NET 8 framework
    - The orchestrator must be able to connect to Keyfactor Command and the Hashicorp Vault instance.



#### Supported Operations

| Operation    | Is Supported                                                                                                           |
|--------------|------------------------------------------------------------------------------------------------------------------------|
| Add          | ✅ Checked        |
| Remove       | ✅ Checked     |
| Discovery    | ✅ Checked  |
| Reenrollment | 🔲 Unchecked |
| Create       | ✅ Checked     |

#### Store Type Creation

##### Using kfutil:
`kfutil` is a custom CLI for the Keyfactor Command API and can be used to create certificate store types.
For more information on [kfutil](https://github.com/Keyfactor/kfutil) check out the [docs](https://github.com/Keyfactor/kfutil?tab=readme-ov-file#quickstart)
   <details><summary>Click to expand HCVKVJKS kfutil details</summary>

   ##### Using online definition from GitHub:
   This will reach out to GitHub and pull the latest store-type definition
   ```shell
   # Hashicorp Vault Key-Value JKS
   kfutil store-types create HCVKVJKS
   ```

   ##### Offline creation using integration-manifest file:
   If required, it is possible to create store types from the [integration-manifest.json](./integration-manifest.json) included in this repo.
   You would first download the [integration-manifest.json](./integration-manifest.json) and then run the following command
   in your offline environment.
   ```shell
   kfutil store-types create --from-file integration-manifest.json
   ```
   </details>


#### Manual Creation
Below are instructions on how to create the HCVKVJKS store type manually in
the Keyfactor Command Portal
   <details><summary>Click to expand manual HCVKVJKS details</summary>

   Create a store type called `HCVKVJKS` with the attributes in the tables below:

   ##### Basic Tab
   | Attribute | Value | Description |
   | --------- | ----- | ----- |
   | Name | Hashicorp Vault Key-Value JKS | Display name for the store type (may be customized) |
   | Short Name | HCVKVJKS | Short display name for the store type |
   | Capability | HCVKVJKS | Store type name orchestrator will register with. Check the box to allow entry of value |
   | Supports Add | ✅ Checked | Check the box. Indicates that the Store Type supports Management Add |
   | Supports Remove | ✅ Checked | Check the box. Indicates that the Store Type supports Management Remove |
   | Supports Discovery | ✅ Checked | Check the box. Indicates that the Store Type supports Discovery |
   | Supports Reenrollment | 🔲 Unchecked |  Indicates that the Store Type supports Reenrollment |
   | Supports Create | ✅ Checked | Check the box. Indicates that the Store Type supports store creation |
   | Needs Server | ✅ Checked | Determines if a target server name is required when creating store |
   | Blueprint Allowed | 🔲 Unchecked | Determines if store type may be included in an Orchestrator blueprint |
   | Uses PowerShell | 🔲 Unchecked | Determines if underlying implementation is PowerShell |
   | Requires Store Password | 🔲 Unchecked | Enables users to optionally specify a store password when defining a Certificate Store. |
   | Supports Entry Password | 🔲 Unchecked | Determines if an individual entry within a store can have a password. |

   The Basic tab should look like this:

   ![HCVKVJKS Basic Tab](docsource/images/HCVKVJKS-basic-store-type-dialog.png)

   ##### Advanced Tab
   | Attribute | Value | Description |
   | --------- | ----- | ----- |
   | Supports Custom Alias | Optional | Determines if an individual entry within a store can have a custom Alias. |
   | Private Key Handling | Optional | This determines if Keyfactor can send the private key associated with a certificate to the store. Required because IIS certificates without private keys would be invalid. |
   | PFX Password Style | Default | 'Default' - PFX password is randomly generated, 'Custom' - PFX password may be specified when the enrollment job is created (Requires the Allow Custom Password application setting to be enabled.) |

   The Advanced tab should look like this:

   ![HCVKVJKS Advanced Tab](docsource/images/HCVKVJKS-advanced-store-type-dialog.png)

   > For Keyfactor **Command versions 24.4 and later**, a Certificate Format dropdown is available with PFX and PEM options. Ensure that **PFX** is selected, as this determines the format of new and renewed certificates sent to the Orchestrator during a Management job. Currently, all Keyfactor-supported Orchestrator extensions support only PFX.

   ##### Custom Fields Tab
   Custom fields operate at the certificate store level and are used to control how the orchestrator connects to the remote target server containing the certificate store to be managed. The following custom fields should be added to the store type:

   | Name | Display Name | Description | Type | Default Value/Options | Required |
   | ---- | ------------ | ---- | --------------------- | -------- | ----------- |

   The Custom Fields tab should look like this:

   ![HCVKVJKS Custom Fields Tab](docsource/images/HCVKVJKS-custom-fields-store-type-dialog.png)

   </details>
</details>

### HCVKVP12

<details><summary>Click to expand details</summary>


The Hashicorp Vault Key-Value PKCS12 Certificate Store Type allows users to manage PKCS12 (P12) certificate files stored within Hashicorp Vault using the Key-Value secrets engine. This store type treats each PKCS12 file as a separate certificate store, enabling detailed management of these files through Keyfactor Command. It supports a range of operations such as discovery, inventory, and the addition and removal of certificates within the PKCS12 files.

#### Representation and Functionality

The Hashicorp Vault Key-Value PKCS12 Certificate Store Type represents individual PKCS12 files stored in the Vault's Key-Value secrets engine. Each PKCS12 file is considered an independent store, facilitating the systematic management of multiple PKCS12 files. To operate correctly, the PKCS12 files need to be base64 encoded and stored with specific naming conventions.

#### Caveats and Considerations

There are several important considerations when using this Certificate Store Type:

- **Base64 Encoding:** All PKCS12 files must be base64 encoded before being stored in Vault. This encoding ensures the files are properly recognized and managed.
- **Key Naming Convention:** The name (key) for each PKCS12 entry must end with the suffix '_p12' to be correctly identified during operations. Following this convention is critical for accurate inventory and management.
- **Passphrase Requirement:** Each PKCS12 file entry must include a `passphrase` field containing the password for the store. Omitting this field will cause the PKCS12 file to be ignored during inventory scans, leading to potential incomplete results.

#### Limitations and Potential Confusion

The principal limitation of the Key-Value PKCS12 Certificate Store Type is its reliance on strict naming conventions and base64 encoding. Users must ensure that each entry is accurately named and encoded to avoid errors during management operations. Proper inclusion of the `passphrase` field is also crucial for successful inventory and management.

#### SDK Use

Although the documentation does not explicitly mention the use of an SDK, interactions are facilitated through the Hashicorp Vault API. This implicitly suggests that the Keyfactor Command orchestrator employs an API client or service to execute the required operations.

#### Summary

In summary, the Hashicorp Vault Key-Value PKCS12 Certificate Store Type provides a robust solution for managing PKCS12 files within Vault's Key-Value secrets engine. Treating each PKCS12 file as an independent store enhances manageability and organization. However, users must be vigilant about base64 encoding, adhering to strict naming conventions, and including passphrases to ensure smooth operations and accurate results.




#### Hashicorp Vault Key-Value PKCS12 Requirements

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
    - Ensure the orchestration system is compatible with the .NET 6 or .NET 8 framework
    - The orchestrator must be able to connect to Keyfactor Command and the Hashicorp Vault instance.



#### Supported Operations

| Operation    | Is Supported                                                                                                           |
|--------------|------------------------------------------------------------------------------------------------------------------------|
| Add          | ✅ Checked        |
| Remove       | ✅ Checked     |
| Discovery    | ✅ Checked  |
| Reenrollment | 🔲 Unchecked |
| Create       | ✅ Checked     |

#### Store Type Creation

##### Using kfutil:
`kfutil` is a custom CLI for the Keyfactor Command API and can be used to create certificate store types.
For more information on [kfutil](https://github.com/Keyfactor/kfutil) check out the [docs](https://github.com/Keyfactor/kfutil?tab=readme-ov-file#quickstart)
   <details><summary>Click to expand HCVKVP12 kfutil details</summary>

   ##### Using online definition from GitHub:
   This will reach out to GitHub and pull the latest store-type definition
   ```shell
   # Hashicorp Vault Key-Value PKCS12
   kfutil store-types create HCVKVP12
   ```

   ##### Offline creation using integration-manifest file:
   If required, it is possible to create store types from the [integration-manifest.json](./integration-manifest.json) included in this repo.
   You would first download the [integration-manifest.json](./integration-manifest.json) and then run the following command
   in your offline environment.
   ```shell
   kfutil store-types create --from-file integration-manifest.json
   ```
   </details>


#### Manual Creation
Below are instructions on how to create the HCVKVP12 store type manually in
the Keyfactor Command Portal
   <details><summary>Click to expand manual HCVKVP12 details</summary>

   Create a store type called `HCVKVP12` with the attributes in the tables below:

   ##### Basic Tab
   | Attribute | Value | Description |
   | --------- | ----- | ----- |
   | Name | Hashicorp Vault Key-Value PKCS12 | Display name for the store type (may be customized) |
   | Short Name | HCVKVP12 | Short display name for the store type |
   | Capability | HCVKVP12 | Store type name orchestrator will register with. Check the box to allow entry of value |
   | Supports Add | ✅ Checked | Check the box. Indicates that the Store Type supports Management Add |
   | Supports Remove | ✅ Checked | Check the box. Indicates that the Store Type supports Management Remove |
   | Supports Discovery | ✅ Checked | Check the box. Indicates that the Store Type supports Discovery |
   | Supports Reenrollment | 🔲 Unchecked |  Indicates that the Store Type supports Reenrollment |
   | Supports Create | ✅ Checked | Check the box. Indicates that the Store Type supports store creation |
   | Needs Server | ✅ Checked | Determines if a target server name is required when creating store |
   | Blueprint Allowed | 🔲 Unchecked | Determines if store type may be included in an Orchestrator blueprint |
   | Uses PowerShell | 🔲 Unchecked | Determines if underlying implementation is PowerShell |
   | Requires Store Password | 🔲 Unchecked | Enables users to optionally specify a store password when defining a Certificate Store. |
   | Supports Entry Password | 🔲 Unchecked | Determines if an individual entry within a store can have a password. |

   The Basic tab should look like this:

   ![HCVKVP12 Basic Tab](docsource/images/HCVKVP12-basic-store-type-dialog.png)

   ##### Advanced Tab
   | Attribute | Value | Description |
   | --------- | ----- | ----- |
   | Supports Custom Alias | Optional | Determines if an individual entry within a store can have a custom Alias. |
   | Private Key Handling | Optional | This determines if Keyfactor can send the private key associated with a certificate to the store. Required because IIS certificates without private keys would be invalid. |
   | PFX Password Style | Default | 'Default' - PFX password is randomly generated, 'Custom' - PFX password may be specified when the enrollment job is created (Requires the Allow Custom Password application setting to be enabled.) |

   The Advanced tab should look like this:

   ![HCVKVP12 Advanced Tab](docsource/images/HCVKVP12-advanced-store-type-dialog.png)

   > For Keyfactor **Command versions 24.4 and later**, a Certificate Format dropdown is available with PFX and PEM options. Ensure that **PFX** is selected, as this determines the format of new and renewed certificates sent to the Orchestrator during a Management job. Currently, all Keyfactor-supported Orchestrator extensions support only PFX.

   ##### Custom Fields Tab
   Custom fields operate at the certificate store level and are used to control how the orchestrator connects to the remote target server containing the certificate store to be managed. The following custom fields should be added to the store type:

   | Name | Display Name | Description | Type | Default Value/Options | Required |
   | ---- | ------------ | ---- | --------------------- | -------- | ----------- |

   The Custom Fields tab should look like this:

   ![HCVKVP12 Custom Fields Tab](docsource/images/HCVKVP12-custom-fields-store-type-dialog.png)

   </details>
</details>

### HCVKVPFX

<details><summary>Click to expand details</summary>


The Hashicorp Vault Key-Value PFX Certificate Store Type allows users to manage Personal Information Exchange (PFX) certificate files stored within Hashicorp Vault using the Key-Value secrets engine. Each PFX file is treated as an independent certificate store, facilitating precise management through Keyfactor Command. This store type supports operations such as discovery, inventory, as well as the addition and removal of certificates within the PFX files.

#### Representation and Functionality

The Hashicorp Vault Key-Value PFX Certificate Store Type represents individual PFX files stored in the Vault's Key-Value secrets engine. These PFX files must be base64 encoded and are identified within the Vault using specific naming conventions, allowing seamless interaction and management with Keyfactor Command.

#### Caveats and Considerations

Users should be aware of several important considerations when utilizing this Certificate Store Type:

- **Base64 Encoding:** All PFX files must be base64 encoded before being stored in Vault. Proper encoding is essential for the files to be correctly recognized and managed.
- **Key Naming Convention:** The name (key) for each PFX entry must end with the suffix '_pfx' to ensure proper identification during operations. Adherence to this naming convention is critical for accurate processing.
- **Passphrase Requirement:** Each PFX file entry must include a `passphrase` field containing the password for the store. The absence of this field will result in the PFX file being ignored during inventory scans, potentially leading to incomplete results.

#### Limitations and Potential Confusion

A primary limitation is the dependency on strict naming conventions and the requirement for base64 encoding. Users must ensure that each entry is named and encoded correctly to avoid errors during management operations. Additionally, including the `passphrase` field accurately for each PFX file is vital for successful inventory and management.

#### SDK Use

Although the documentation does not explicitly mention the use of an SDK, it can be inferred that interactions are conducted through the Hashicorp Vault API. This suggests that the Keyfactor Command orchestrator utilizes an API client to perform the necessary operations.

#### Summary

In summary, the Hashicorp Vault Key-Value PFX Certificate Store Type offers an effective solution for managing PFX files within Vault's Key-Value secrets engine. Representing each PFX file as a distinct store enhances organizational capability and manageability. However, to ensure smooth operations and accurate results, users need to be meticulous with base64 encoding, naming conventions, and the inclusion of passphrases.




#### Hashicorp Vault Key-Value PFX Requirements

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
    - Ensure the orchestration system is compatible with the .NET 6 or .NET 8 framework
    - The orchestrator must be able to connect to Keyfactor Command and the Hashicorp Vault instance.



#### Supported Operations

| Operation    | Is Supported                                                                                                           |
|--------------|------------------------------------------------------------------------------------------------------------------------|
| Add          | ✅ Checked        |
| Remove       | ✅ Checked     |
| Discovery    | ✅ Checked  |
| Reenrollment | 🔲 Unchecked |
| Create       | ✅ Checked     |

#### Store Type Creation

##### Using kfutil:
`kfutil` is a custom CLI for the Keyfactor Command API and can be used to create certificate store types.
For more information on [kfutil](https://github.com/Keyfactor/kfutil) check out the [docs](https://github.com/Keyfactor/kfutil?tab=readme-ov-file#quickstart)
   <details><summary>Click to expand HCVKVPFX kfutil details</summary>

   ##### Using online definition from GitHub:
   This will reach out to GitHub and pull the latest store-type definition
   ```shell
   # Hashicorp Vault Key-Value PFX
   kfutil store-types create HCVKVPFX
   ```

   ##### Offline creation using integration-manifest file:
   If required, it is possible to create store types from the [integration-manifest.json](./integration-manifest.json) included in this repo.
   You would first download the [integration-manifest.json](./integration-manifest.json) and then run the following command
   in your offline environment.
   ```shell
   kfutil store-types create --from-file integration-manifest.json
   ```
   </details>


#### Manual Creation
Below are instructions on how to create the HCVKVPFX store type manually in
the Keyfactor Command Portal
   <details><summary>Click to expand manual HCVKVPFX details</summary>

   Create a store type called `HCVKVPFX` with the attributes in the tables below:

   ##### Basic Tab
   | Attribute | Value | Description |
   | --------- | ----- | ----- |
   | Name | Hashicorp Vault Key-Value PFX | Display name for the store type (may be customized) |
   | Short Name | HCVKVPFX | Short display name for the store type |
   | Capability | HCVKVPFX | Store type name orchestrator will register with. Check the box to allow entry of value |
   | Supports Add | ✅ Checked | Check the box. Indicates that the Store Type supports Management Add |
   | Supports Remove | ✅ Checked | Check the box. Indicates that the Store Type supports Management Remove |
   | Supports Discovery | ✅ Checked | Check the box. Indicates that the Store Type supports Discovery |
   | Supports Reenrollment | 🔲 Unchecked |  Indicates that the Store Type supports Reenrollment |
   | Supports Create | ✅ Checked | Check the box. Indicates that the Store Type supports store creation |
   | Needs Server | ✅ Checked | Determines if a target server name is required when creating store |
   | Blueprint Allowed | 🔲 Unchecked | Determines if store type may be included in an Orchestrator blueprint |
   | Uses PowerShell | 🔲 Unchecked | Determines if underlying implementation is PowerShell |
   | Requires Store Password | 🔲 Unchecked | Enables users to optionally specify a store password when defining a Certificate Store. |
   | Supports Entry Password | 🔲 Unchecked | Determines if an individual entry within a store can have a password. |

   The Basic tab should look like this:

   ![HCVKVPFX Basic Tab](docsource/images/HCVKVPFX-basic-store-type-dialog.png)

   ##### Advanced Tab
   | Attribute | Value | Description |
   | --------- | ----- | ----- |
   | Supports Custom Alias | Optional | Determines if an individual entry within a store can have a custom Alias. |
   | Private Key Handling | Optional | This determines if Keyfactor can send the private key associated with a certificate to the store. Required because IIS certificates without private keys would be invalid. |
   | PFX Password Style | Default | 'Default' - PFX password is randomly generated, 'Custom' - PFX password may be specified when the enrollment job is created (Requires the Allow Custom Password application setting to be enabled.) |

   The Advanced tab should look like this:

   ![HCVKVPFX Advanced Tab](docsource/images/HCVKVPFX-advanced-store-type-dialog.png)

   > For Keyfactor **Command versions 24.4 and later**, a Certificate Format dropdown is available with PFX and PEM options. Ensure that **PFX** is selected, as this determines the format of new and renewed certificates sent to the Orchestrator during a Management job. Currently, all Keyfactor-supported Orchestrator extensions support only PFX.

   ##### Custom Fields Tab
   Custom fields operate at the certificate store level and are used to control how the orchestrator connects to the remote target server containing the certificate store to be managed. The following custom fields should be added to the store type:

   | Name | Display Name | Description | Type | Default Value/Options | Required |
   | ---- | ------------ | ---- | --------------------- | -------- | ----------- |

   The Custom Fields tab should look like this:

   ![HCVKVPFX Custom Fields Tab](docsource/images/HCVKVPFX-custom-fields-store-type-dialog.png)

   </details>
</details>


## Installation

1. **Download the latest Hashicorp Vault Universal Orchestrator extension from GitHub.**

    Navigate to the [Hashicorp Vault Universal Orchestrator extension GitHub version page](https://github.com/Keyfactor/hashicorp-vault-orchestrator/releases/latest). Refer to the compatibility matrix below to determine whether the `net6.0` or `net8.0` asset should be downloaded. Then, click the corresponding asset to download the zip archive.

   | Universal Orchestrator Version | Latest .NET version installed on the Universal Orchestrator server | `rollForward` condition in `Orchestrator.runtimeconfig.json` | `hashicorp-vault-orchestrator` .NET version to download |
   | --------- | ----------- | ----------- | ----------- |
   | Older than `11.0.0` | | | `net6.0` |
   | Between `11.0.0` and `11.5.1` (inclusive) | `net6.0` | | `net6.0` |
   | Between `11.0.0` and `11.5.1` (inclusive) | `net8.0` | `Disable` | `net6.0` |
   | Between `11.0.0` and `11.5.1` (inclusive) | `net8.0` | `LatestMajor` | `net8.0` |
   | `11.6` _and_ newer | `net8.0` | | `net8.0` |

    Unzip the archive containing extension assemblies to a known location.

    > **Note** If you don't see an asset with a corresponding .NET version, you should always assume that it was compiled for `net6.0`.

2. **Locate the Universal Orchestrator extensions directory.**

    * **Default on Windows** - `C:\Program Files\Keyfactor\Keyfactor Orchestrator\extensions`
    * **Default on Linux** - `/opt/keyfactor/orchestrator/extensions`

3. **Create a new directory for the Hashicorp Vault Universal Orchestrator extension inside the extensions directory.**

    Create a new directory called `hashicorp-vault-orchestrator`.
    > The directory name does not need to match any names used elsewhere; it just has to be unique within the extensions directory.

4. **Copy the contents of the downloaded and unzipped assemblies from __step 2__ to the `hashicorp-vault-orchestrator` directory.**

5. **Restart the Universal Orchestrator service.**

    Refer to [Starting/Restarting the Universal Orchestrator service](https://software.keyfactor.com/Core-OnPrem/Current/Content/InstallingAgents/NetCoreOrchestrator/StarttheService.htm).



> The above installation steps can be supplemented by the [official Command documentation](https://software.keyfactor.com/Core-OnPrem/Current/Content/InstallingAgents/NetCoreOrchestrator/CustomExtensions.htm?Highlight=extensions).



## Defining Certificate Stores

The Hashicorp Vault Universal Orchestrator extension implements 5 Certificate Store Types, each of which implements different functionality. Refer to the individual instructions below for each Certificate Store Type that you deemed necessary for your use case from the installation section.

<details><summary>Hashicorp Vault Key-Value PEM (HCVKVPEM)</summary>


### Store Creation

#### Manually with the Command UI

<details><summary>Click to expand details</summary>

1. **Navigate to the _Certificate Stores_ page in Keyfactor Command.**

    Log into Keyfactor Command, toggle the _Locations_ dropdown, and click _Certificate Stores_.

2. **Add a Certificate Store.**

    Click the Add button to add a new Certificate Store. Use the table below to populate the **Attributes** in the **Add** form.

   | Attribute | Description                                             |
   | --------- |---------------------------------------------------------|
   | Category | Select "Hashicorp Vault Key-Value PEM" or the customized certificate store name from the previous step. |
   | Container | Optional container to associate certificate store with. |
   | Client Machine | An identifier for the client machine which could be the host name of the Orchestrator or any meaningful label. This value is not used by the Hashicorp Vault Key-Value PEM extension. |
   | Store Path | The specific path within the Hashicorp Vault's Key-Value secrets engine where the certificates will be stored. Example: 'kv-v2/kf-secrets'. |
   | Orchestrator | Select an approved orchestrator capable of managing `HCVKVPEM` certificates. Specifically, one with the `HCVKVPEM` capability. |

</details>



#### Using kfutil CLI

<details><summary>Click to expand details</summary>

1. **Generate a CSV template for the HCVKVPEM certificate store**

    ```shell
    kfutil stores import generate-template --store-type-name HCVKVPEM --outpath HCVKVPEM.csv
    ```
2. **Populate the generated CSV file**

    Open the CSV file, and reference the table below to populate parameters for each **Attribute**.

   | Attribute | Description |
   | --------- | ----------- |
   | Category | Select "Hashicorp Vault Key-Value PEM" or the customized certificate store name from the previous step. |
   | Container | Optional container to associate certificate store with. |
   | Client Machine | An identifier for the client machine which could be the host name of the Orchestrator or any meaningful label. This value is not used by the Hashicorp Vault Key-Value PEM extension. |
   | Store Path | The specific path within the Hashicorp Vault's Key-Value secrets engine where the certificates will be stored. Example: 'kv-v2/kf-secrets'. |
   | Orchestrator | Select an approved orchestrator capable of managing `HCVKVPEM` certificates. Specifically, one with the `HCVKVPEM` capability. |

3. **Import the CSV file to create the certificate stores**

    ```shell
    kfutil stores import csv --store-type-name HCVKVPEM --file HCVKVPEM.csv
    ```

</details>



> The content in this section can be supplemented by the [official Command documentation](https://software.keyfactor.com/Core-OnPrem/Current/Content/ReferenceGuide/Certificate%20Stores.htm?Highlight=certificate%20store).


</details>

<details><summary>Hashicorp Vault PKI (HCVPKI)</summary>


### Store Creation

#### Manually with the Command UI

<details><summary>Click to expand details</summary>

1. **Navigate to the _Certificate Stores_ page in Keyfactor Command.**

    Log into Keyfactor Command, toggle the _Locations_ dropdown, and click _Certificate Stores_.

2. **Add a Certificate Store.**

    Click the Add button to add a new Certificate Store. Use the table below to populate the **Attributes** in the **Add** form.

   | Attribute | Description                                             |
   | --------- |---------------------------------------------------------|
   | Category | Select "Hashicorp Vault PKI" or the customized certificate store name from the previous step. |
   | Container | Optional container to associate certificate store with. |
   | Client Machine | The full URL for the Vault host machine that will be used by the orchestrator to access the Hashicorp Vault PKI instance. Example: 'http://127.0.0.1:8200'. |
   | Store Path | The specific path within the Hashicorp Vault PKI secrets engine where the certificates will be managed. Example: '/'. |
   | Orchestrator | Select an approved orchestrator capable of managing `HCVPKI` certificates. Specifically, one with the `HCVPKI` capability. |

</details>



#### Using kfutil CLI

<details><summary>Click to expand details</summary>

1. **Generate a CSV template for the HCVPKI certificate store**

    ```shell
    kfutil stores import generate-template --store-type-name HCVPKI --outpath HCVPKI.csv
    ```
2. **Populate the generated CSV file**

    Open the CSV file, and reference the table below to populate parameters for each **Attribute**.

   | Attribute | Description |
   | --------- | ----------- |
   | Category | Select "Hashicorp Vault PKI" or the customized certificate store name from the previous step. |
   | Container | Optional container to associate certificate store with. |
   | Client Machine | The full URL for the Vault host machine that will be used by the orchestrator to access the Hashicorp Vault PKI instance. Example: 'http://127.0.0.1:8200'. |
   | Store Path | The specific path within the Hashicorp Vault PKI secrets engine where the certificates will be managed. Example: '/'. |
   | Orchestrator | Select an approved orchestrator capable of managing `HCVPKI` certificates. Specifically, one with the `HCVPKI` capability. |

3. **Import the CSV file to create the certificate stores**

    ```shell
    kfutil stores import csv --store-type-name HCVPKI --file HCVPKI.csv
    ```

</details>



> The content in this section can be supplemented by the [official Command documentation](https://software.keyfactor.com/Core-OnPrem/Current/Content/ReferenceGuide/Certificate%20Stores.htm?Highlight=certificate%20store).


</details>

<details><summary>Hashicorp Vault Key-Value JKS (HCVKVJKS)</summary>


### Store Creation

#### Manually with the Command UI

<details><summary>Click to expand details</summary>

1. **Navigate to the _Certificate Stores_ page in Keyfactor Command.**

    Log into Keyfactor Command, toggle the _Locations_ dropdown, and click _Certificate Stores_.

2. **Add a Certificate Store.**

    Click the Add button to add a new Certificate Store. Use the table below to populate the **Attributes** in the **Add** form.

   | Attribute | Description                                             |
   | --------- |---------------------------------------------------------|
   | Category | Select "Hashicorp Vault Key-Value JKS" or the customized certificate store name from the previous step. |
   | Container | Optional container to associate certificate store with. |
   | Client Machine | An identifier for the client machine which could be the host name of the Orchestrator or any meaningful label. This value is not used by the Hashicorp Vault Key-Value JKS extension. |
   | Store Path | The specific path within the Hashicorp Vault's Key-Value secrets engine where the JKS certificate files will be stored. Example: 'kv-v2/jks-certificates'. |
   | Orchestrator | Select an approved orchestrator capable of managing `HCVKVJKS` certificates. Specifically, one with the `HCVKVJKS` capability. |

</details>



#### Using kfutil CLI

<details><summary>Click to expand details</summary>

1. **Generate a CSV template for the HCVKVJKS certificate store**

    ```shell
    kfutil stores import generate-template --store-type-name HCVKVJKS --outpath HCVKVJKS.csv
    ```
2. **Populate the generated CSV file**

    Open the CSV file, and reference the table below to populate parameters for each **Attribute**.

   | Attribute | Description |
   | --------- | ----------- |
   | Category | Select "Hashicorp Vault Key-Value JKS" or the customized certificate store name from the previous step. |
   | Container | Optional container to associate certificate store with. |
   | Client Machine | An identifier for the client machine which could be the host name of the Orchestrator or any meaningful label. This value is not used by the Hashicorp Vault Key-Value JKS extension. |
   | Store Path | The specific path within the Hashicorp Vault's Key-Value secrets engine where the JKS certificate files will be stored. Example: 'kv-v2/jks-certificates'. |
   | Orchestrator | Select an approved orchestrator capable of managing `HCVKVJKS` certificates. Specifically, one with the `HCVKVJKS` capability. |

3. **Import the CSV file to create the certificate stores**

    ```shell
    kfutil stores import csv --store-type-name HCVKVJKS --file HCVKVJKS.csv
    ```

</details>



> The content in this section can be supplemented by the [official Command documentation](https://software.keyfactor.com/Core-OnPrem/Current/Content/ReferenceGuide/Certificate%20Stores.htm?Highlight=certificate%20store).


</details>

<details><summary>Hashicorp Vault Key-Value PKCS12 (HCVKVP12)</summary>


### Store Creation

#### Manually with the Command UI

<details><summary>Click to expand details</summary>

1. **Navigate to the _Certificate Stores_ page in Keyfactor Command.**

    Log into Keyfactor Command, toggle the _Locations_ dropdown, and click _Certificate Stores_.

2. **Add a Certificate Store.**

    Click the Add button to add a new Certificate Store. Use the table below to populate the **Attributes** in the **Add** form.

   | Attribute | Description                                             |
   | --------- |---------------------------------------------------------|
   | Category | Select "Hashicorp Vault Key-Value PKCS12" or the customized certificate store name from the previous step. |
   | Container | Optional container to associate certificate store with. |
   | Client Machine | An identifier for the client machine which could be the host name of the Orchestrator or any meaningful label. This value is not used by the Hashicorp Vault Key-Value PKCS12 extension. |
   | Store Path | The specific path within the Hashicorp Vault's Key-Value secrets engine where the PKCS12 certificate files will be stored. Example: 'kv-v2/pkcs12-certificates'. |
   | Orchestrator | Select an approved orchestrator capable of managing `HCVKVP12` certificates. Specifically, one with the `HCVKVP12` capability. |

</details>



#### Using kfutil CLI

<details><summary>Click to expand details</summary>

1. **Generate a CSV template for the HCVKVP12 certificate store**

    ```shell
    kfutil stores import generate-template --store-type-name HCVKVP12 --outpath HCVKVP12.csv
    ```
2. **Populate the generated CSV file**

    Open the CSV file, and reference the table below to populate parameters for each **Attribute**.

   | Attribute | Description |
   | --------- | ----------- |
   | Category | Select "Hashicorp Vault Key-Value PKCS12" or the customized certificate store name from the previous step. |
   | Container | Optional container to associate certificate store with. |
   | Client Machine | An identifier for the client machine which could be the host name of the Orchestrator or any meaningful label. This value is not used by the Hashicorp Vault Key-Value PKCS12 extension. |
   | Store Path | The specific path within the Hashicorp Vault's Key-Value secrets engine where the PKCS12 certificate files will be stored. Example: 'kv-v2/pkcs12-certificates'. |
   | Orchestrator | Select an approved orchestrator capable of managing `HCVKVP12` certificates. Specifically, one with the `HCVKVP12` capability. |

3. **Import the CSV file to create the certificate stores**

    ```shell
    kfutil stores import csv --store-type-name HCVKVP12 --file HCVKVP12.csv
    ```

</details>



> The content in this section can be supplemented by the [official Command documentation](https://software.keyfactor.com/Core-OnPrem/Current/Content/ReferenceGuide/Certificate%20Stores.htm?Highlight=certificate%20store).


</details>

<details><summary>Hashicorp Vault Key-Value PFX (HCVKVPFX)</summary>


### Store Creation

#### Manually with the Command UI

<details><summary>Click to expand details</summary>

1. **Navigate to the _Certificate Stores_ page in Keyfactor Command.**

    Log into Keyfactor Command, toggle the _Locations_ dropdown, and click _Certificate Stores_.

2. **Add a Certificate Store.**

    Click the Add button to add a new Certificate Store. Use the table below to populate the **Attributes** in the **Add** form.

   | Attribute | Description                                             |
   | --------- |---------------------------------------------------------|
   | Category | Select "Hashicorp Vault Key-Value PFX" or the customized certificate store name from the previous step. |
   | Container | Optional container to associate certificate store with. |
   | Client Machine | An identifier for the client machine which could be the host name of the Orchestrator or any meaningful label. This value is not used by the Hashicorp Vault Key-Value PFX extension. |
   | Store Path | The specific path within the Hashicorp Vault's Key-Value secrets engine where the PFX certificate files will be stored. Example: 'kv-v2/pfx-certificates'. |
   | Orchestrator | Select an approved orchestrator capable of managing `HCVKVPFX` certificates. Specifically, one with the `HCVKVPFX` capability. |

</details>



#### Using kfutil CLI

<details><summary>Click to expand details</summary>

1. **Generate a CSV template for the HCVKVPFX certificate store**

    ```shell
    kfutil stores import generate-template --store-type-name HCVKVPFX --outpath HCVKVPFX.csv
    ```
2. **Populate the generated CSV file**

    Open the CSV file, and reference the table below to populate parameters for each **Attribute**.

   | Attribute | Description |
   | --------- | ----------- |
   | Category | Select "Hashicorp Vault Key-Value PFX" or the customized certificate store name from the previous step. |
   | Container | Optional container to associate certificate store with. |
   | Client Machine | An identifier for the client machine which could be the host name of the Orchestrator or any meaningful label. This value is not used by the Hashicorp Vault Key-Value PFX extension. |
   | Store Path | The specific path within the Hashicorp Vault's Key-Value secrets engine where the PFX certificate files will be stored. Example: 'kv-v2/pfx-certificates'. |
   | Orchestrator | Select an approved orchestrator capable of managing `HCVKVPFX` certificates. Specifically, one with the `HCVKVPFX` capability. |

3. **Import the CSV file to create the certificate stores**

    ```shell
    kfutil stores import csv --store-type-name HCVKVPFX --file HCVKVPFX.csv
    ```

</details>



> The content in this section can be supplemented by the [official Command documentation](https://software.keyfactor.com/Core-OnPrem/Current/Content/ReferenceGuide/Certificate%20Stores.htm?Highlight=certificate%20store).


</details>




## License

Apache License 2.0, see [LICENSE](LICENSE).

## Related Integrations

See all [Keyfactor Universal Orchestrator extensions](https://github.com/orgs/Keyfactor/repositories?q=orchestrator).