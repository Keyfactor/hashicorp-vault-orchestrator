## Hashicorp Vault Key-Value PEM

The Hashicorp Vault Key-Value PEM Certificate Store Type allows users to manage PEM-encoded certificates stored in Hashicorp Vault using the Key-Value secrets engine. This store type treats each path in the Key-Value store as a certificate store, with individual certificates residing in sub-paths. It supports various operations, including discovery, inventory, certificate addition, certificate removal, and creating new certificate stores.

### Representation and Functionality

The Hashicorp Vault Key-Value PEM Certificate Store Type represents a hierarchical structure where the root path defined in the store contains multiple sub-paths, each potentially holding separate certificates and their associated private keys. This design enables users to manage large collections of PEM-encoded certificates efficiently and flexibly.

### Caveats and Considerations

There are several important considerations to keep in mind when using this Certificate Store Type:

- **Sub-Folder Inventory:** Users can configure the store to include or exclude sub-folders during inventory operations. Setting 'Subfolder Inventory' to 'True' will inventory certificates in both the root path and its sub-paths. Conversely, setting it to 'False' will limit the inventory to certificates in the root path only. This flexibility helps avoid duplication and manage the store size effectively.
- **Base64 Encoding:** All certificates and private keys in the Key-Value store must be base64 encoded. Incorrect encoding can lead to errors during inventory and management operations.
- **Complex Path Management:** The hierarchical nature of the Key-Value PEM Certificate Store Type necessitates careful path management to avoid redundancy and ensure accurate inventory. Users should be cautious about the path configurations to prevent the same certificate from appearing in multiple stores.

### Limitations and Potential Confusion

The primary limitation of this store type is the potential complexity in managing a large number of certificates within a hierarchical path structure. Additionally, users need to ensure that all PEM-encoded certificates and keys are correctly base64 encoded and correctly named to be recognized during inventory scans. If the required fields such as 'private_key' for PEM-encoded certificates are not present, those entries will be ignored during inventory scans.

### SDK Use

The documentation does not explicitly mention the use of an SDK for this Certificate Store Type. However, users interact with the Hashicorp Vault API to perform required operations, implying that some form of API client or service is in use by the Keyfactor Command orchestrator.

### Summary

The Hashicorp Vault Key-Value PEM Certificate Store Type is a powerful extension for managing PEM-encoded certificates stored in a hierarchical structure within Vault's Key-Value secrets engine. While it offers significant flexibility and efficiency, it also demands careful management of paths and proper encoding to avoid errors and ensure smooth operation.



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



## Certificate Store Type Configuration

The recommended method for creating the `HCVKVPEM` Certificate Store Type is to use [kfutil](https://github.com/Keyfactor/kfutil). After installing, use the following command to create the `` Certificate Store Type:

```shell
kfutil store-types create HCVKVPEM
```

<details><summary>HCVKVPEM</summary>

Create a store type called `HCVKVPEM` with the attributes in the tables below:

### Basic Tab
| Attribute | Value | Description |
| --------- | ----- | ----- |
| Name | Hashicorp Vault Key-Value PEM | Display name for the store type (may be customized) |
| Short Name | HCVKVPEM | Short display name for the store type |
| Capability | HCVKVPEM | Store type name orchestrator will register with. Check the box to allow entry of value |
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

![HCVKVPEM Basic Tab](../docsource/images/HCVKVPEM-basic-store-type-dialog.png)

### Advanced Tab
| Attribute | Value | Description |
| --------- | ----- | ----- |
| Supports Custom Alias | Optional | Determines if an individual entry within a store can have a custom Alias. |
| Private Key Handling | Optional | This determines if Keyfactor can send the private key associated with a certificate to the store. Required because IIS certificates without private keys would be invalid. |
| PFX Password Style | Default | 'Default' - PFX password is randomly generated, 'Custom' - PFX password may be specified when the enrollment job is created (Requires the Allow Custom Password application setting to be enabled.) |

The Advanced tab should look like this:

![HCVKVPEM Advanced Tab](../docsource/images/HCVKVPEM-advanced-store-type-dialog.png)

### Custom Fields Tab
Custom fields operate at the certificate store level and are used to control how the orchestrator connects to the remote target server containing the certificate store to be managed. The following custom fields should be added to the store type:

| Name | Display Name | Type | Default Value/Options | Required | Description |
| ---- | ------------ | ---- | --------------------- | -------- | ----------- |


The Custom Fields tab should look like this:

![HCVKVPEM Custom Fields Tab](../docsource/images/HCVKVPEM-custom-fields-store-type-dialog.png)



</details>

## Certificate Store Configuration

After creating the `HCVKVPEM` Certificate Store Type and installing the for Hashicorp Vault Universal Orchestrator extension, you can create new [Certificate Stores](https://software.keyfactor.com/Core-OnPrem/Current/Content/ReferenceGuide/Certificate%20Stores.htm?Highlight=certificate%20store) to manage certificates in the remote platform.

The following table describes the required and optional fields for the `HCVKVPEM` certificate store type.

| Attribute | Description | Attribute is PAM Eligible |
| --------- | ----------- | ------------------------- |
| Category | Select "Hashicorp Vault Key-Value PEM" or the customized certificate store name from the previous step. | |
| Container | Optional container to associate certificate store with. | |
| Client Machine | An identifier for the client machine which could be the host name of the Orchestrator or any meaningful label. This value is not used by the Hashicorp Vault Key-Value PEM extension. | |
| Store Path | The specific path within the Hashicorp Vault's Key-Value secrets engine where the certificates will be stored. Example: 'kv-v2/kf-secrets'. | |
| Orchestrator | Select an approved orchestrator capable of managing `HCVKVPEM` certificates. Specifically, one with the `HCVKVPEM` capability. | |

* **Using kfutil**

    ```shell
    # Generate a CSV template for the AzureApp certificate store
    kfutil stores import generate-template --store-type-name HCVKVPEM --outpath HCVKVPEM.csv

    # Open the CSV file and fill in the required fields for each certificate store.

    # Import the CSV file to create the certificate stores
    kfutil stores import csv --store-type-name HCVKVPEM --file HCVKVPEM.csv
    ```

* **Manually with the Command UI**: In Keyfactor Command, navigate to Certificate Stores from the Locations Menu. Click the Add button to create a new Certificate Store using the attributes in the table above.