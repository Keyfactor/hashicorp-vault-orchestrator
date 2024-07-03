## Hashicorp Vault Key-Value JKS

The Hashicorp Vault Key-Value JKS Certificate Store Type allows users to manage Java KeyStore (JKS) files stored within Hashicorp Vault using the Key-Value secrets engine. This store type treats each JKS file as a separate certificate store, enabling fine-grained management of these files through Keyfactor Command. It supports various operations such as discovery, inventory, and the addition and removal of certificates within the JKS files.

### Representation and Functionality

The Hashicorp Vault Key-Value JKS Certificate Store Type represents individual JKS files stored in the Vault's Key-Value secrets engine. Each JKS file is treated as an independent store, making it easy to manage multiple JKS files systematically. This interaction ensures that each JKS file contains a base64-encoded certificate and an accompanying passphrase stored under specific keys.

### Caveats and Considerations

There are several important considerations when using this Certificate Store Type:

- **Base64 Encoding:** All JKS files must be base64 encoded before being stored in Vault. This encoding ensures that the files can be accurately recognized and managed.
- **Key Naming Convention:** The name (key) for each JKS entry must end with the suffix '_jks' to be correctly identified during operations. This convention is crucial for proper inventory and management.
- **Passphrase Requirement:** Each JKS file entry must include a `passphrase` field containing the password for the store. Without this, the JKS file will be ignored during inventory scans, potentially leading to incomplete results.

### Limitations and Potential Confusion

The primary limitation of the Key-Value JKS Certificate Store Type is its dependence on strict naming conventions and base64 encoding. Users must ensure that each entry is correctly named and encoded to avoid errors during management operations. Additionally, the correct inclusion of the `passphrase` field is crucial for successful inventory and management.

### SDK Use

While not explicitly mentioned in the documentation, interactions are performed through the Hashicorp Vault API, implying that the Keyfactor Command orchestrator utilizes some API client to facilitate the required operations.

### Summary

In summary, the Hashicorp Vault Key-Value JKS Certificate Store Type offers a robust solution for managing JKS files within the Key-Value secrets engine of Hashicorp Vault. Representing each JKS file as an independent store enhances manageability and organization. However, users must be mindful of the necessary base64 encoding, strict naming conventions, and the inclusion of passphrases to ensure smooth operations and accurate results.



### Supported Job Types

| Job Name | Supported |
| -------- | --------- |
| Inventory | ✅ |
| Management Add | ✅ |
| Management Remove | ✅ |
| Discovery | ✅ |
| Create | ✅ |
| Reenrollment |  |

## Requirements

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



## Certificate Store Type Configuration

The recommended method for creating the `HCVKVJKS` Certificate Store Type is to use [kfutil](https://github.com/Keyfactor/kfutil). After installing, use the following command to create the `` Certificate Store Type:

```shell
kfutil store-types create HCVKVJKS
```

<details><summary>HCVKVJKS</summary>

Create a store type called `HCVKVJKS` with the attributes in the tables below:

### Basic Tab
| Attribute | Value | Description |
| --------- | ----- | ----- |
| Name | Hashicorp Vault Key-Value JKS | Display name for the store type (may be customized) |
| Short Name | HCVKVJKS | Short display name for the store type |
| Capability | HCVKVJKS | Store type name orchestrator will register with. Check the box to allow entry of value |
| Supported Job Types (check the box for each) | Add, Discovery, Remove | Job types the extension supports |
| Supports Add | ✅ | Check the box. Indicates that the Store Type supports Management Add |
| Supports Remove | ✅ | Check the box. Indicates that the Store Type supports Management Remove |
| Supports Discovery | ✅ | Check the box. Indicates that the Store Type supports Discovery |
| Supports Reenrollment |  |  Indicates that the Store Type supports Reenrollment |
| Supports Create | ✅ | Check the box. Indicates that the Store Type supports store creation |
| Needs Server | ✅ | Determines if a target server name is required when creating store |
| Blueprint Allowed |  | Determines if store type may be included in an Orchestrator blueprint |
| Uses PowerShell |  | Determines if underlying implementation is PowerShell |
| Requires Store Password |  | Determines if a store password is required when configuring an individual store. |
| Supports Entry Password |  | Determines if an individual entry within a store can have a password. |

The Basic tab should look like this:

![HCVKVJKS Basic Tab](../docsource/images/HCVKVJKS-basic-store-type-dialog.png)

### Advanced Tab
| Attribute | Value | Description |
| --------- | ----- | ----- |
| Supports Custom Alias | Optional | Determines if an individual entry within a store can have a custom Alias. |
| Private Key Handling | Optional | This determines if Keyfactor can send the private key associated with a certificate to the store. Required because IIS certificates without private keys would be invalid. |
| PFX Password Style | Default | 'Default' - PFX password is randomly generated, 'Custom' - PFX password may be specified when the enrollment job is created (Requires the Allow Custom Password application setting to be enabled.) |

The Advanced tab should look like this:

![HCVKVJKS Advanced Tab](../docsource/images/HCVKVJKS-advanced-store-type-dialog.png)

### Custom Fields Tab
Custom fields operate at the certificate store level and are used to control how the orchestrator connects to the remote target server containing the certificate store to be managed. The following custom fields should be added to the store type:

| Name | Display Name | Type | Default Value/Options | Required | Description |
| ---- | ------------ | ---- | --------------------- | -------- | ----------- |


The Custom Fields tab should look like this:

![HCVKVJKS Custom Fields Tab](../docsource/images/HCVKVJKS-custom-fields-store-type-dialog.png)



</details>

## Certificate Store Configuration

After creating the `HCVKVJKS` Certificate Store Type and installing the for Hashicorp Vault Universal Orchestrator extension, you can create new [Certificate Stores](https://software.keyfactor.com/Core-OnPrem/Current/Content/ReferenceGuide/Certificate%20Stores.htm?Highlight=certificate%20store) to manage certificates in the remote platform.

The following table describes the required and optional fields for the `HCVKVJKS` certificate store type.

| Attribute | Description | Attribute is PAM Eligible |
| --------- | ----------- | ------------------------- |
| Category | Select "Hashicorp Vault Key-Value JKS" or the customized certificate store name from the previous step. | |
| Container | Optional container to associate certificate store with. | |
| Client Machine | An identifier for the client machine which could be the host name of the Orchestrator or any meaningful label. This value is not used by the Hashicorp Vault Key-Value JKS extension. | |
| Store Path | The specific path within the Hashicorp Vault's Key-Value secrets engine where the JKS certificate files will be stored. Example: 'kv-v2/jks-certificates'. | |
| Orchestrator | Select an approved orchestrator capable of managing `HCVKVJKS` certificates. Specifically, one with the `HCVKVJKS` capability. | |

* **Using kfutil**

    ```shell
    # Generate a CSV template for the AzureApp certificate store
    kfutil stores import generate-template --store-type-name HCVKVJKS --outpath HCVKVJKS.csv

    # Open the CSV file and fill in the required fields for each certificate store.

    # Import the CSV file to create the certificate stores
    kfutil stores import csv --store-type-name HCVKVJKS --file HCVKVJKS.csv
    ```

* **Manually with the Command UI**: In Keyfactor Command, navigate to Certificate Stores from the Locations Menu. Click the Add button to create a new Certificate Store using the attributes in the table above.