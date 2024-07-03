## Hashicorp Vault Key-Value PFX

The Hashicorp Vault Key-Value PFX Certificate Store Type allows users to manage Personal Information Exchange (PFX) certificate files stored within Hashicorp Vault using the Key-Value secrets engine. Each PFX file is treated as an independent certificate store, facilitating precise management through Keyfactor Command. This store type supports operations such as discovery, inventory, as well as the addition and removal of certificates within the PFX files.

### Representation and Functionality

The Hashicorp Vault Key-Value PFX Certificate Store Type represents individual PFX files stored in the Vault's Key-Value secrets engine. These PFX files must be base64 encoded and are identified within the Vault using specific naming conventions, allowing seamless interaction and management with Keyfactor Command.

### Caveats and Considerations

Users should be aware of several important considerations when utilizing this Certificate Store Type:

- **Base64 Encoding:** All PFX files must be base64 encoded before being stored in Vault. Proper encoding is essential for the files to be correctly recognized and managed.
- **Key Naming Convention:** The name (key) for each PFX entry must end with the suffix '_pfx' to ensure proper identification during operations. Adherence to this naming convention is critical for accurate processing.
- **Passphrase Requirement:** Each PFX file entry must include a `passphrase` field containing the password for the store. The absence of this field will result in the PFX file being ignored during inventory scans, potentially leading to incomplete results.

### Limitations and Potential Confusion

A primary limitation is the dependency on strict naming conventions and the requirement for base64 encoding. Users must ensure that each entry is named and encoded correctly to avoid errors during management operations. Additionally, including the `passphrase` field accurately for each PFX file is vital for successful inventory and management.

### SDK Use

Although the documentation does not explicitly mention the use of an SDK, it can be inferred that interactions are conducted through the Hashicorp Vault API. This suggests that the Keyfactor Command orchestrator utilizes an API client to perform the necessary operations.

### Summary

In summary, the Hashicorp Vault Key-Value PFX Certificate Store Type offers an effective solution for managing PFX files within Vault's Key-Value secrets engine. Representing each PFX file as a distinct store enhances organizational capability and manageability. However, to ensure smooth operations and accurate results, users need to be meticulous with base64 encoding, naming conventions, and the inclusion of passphrases.



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



## Certificate Store Type Configuration

The recommended method for creating the `HCVKVPFX` Certificate Store Type is to use [kfutil](https://github.com/Keyfactor/kfutil). After installing, use the following command to create the `` Certificate Store Type:

```shell
kfutil store-types create HCVKVPFX
```

<details><summary>HCVKVPFX</summary>

Create a store type called `HCVKVPFX` with the attributes in the tables below:

### Basic Tab
| Attribute | Value | Description |
| --------- | ----- | ----- |
| Name | Hashicorp Vault Key-Value PFX | Display name for the store type (may be customized) |
| Short Name | HCVKVPFX | Short display name for the store type |
| Capability | HCVKVPFX | Store type name orchestrator will register with. Check the box to allow entry of value |
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

![HCVKVPFX Basic Tab](../docsource/images/HCVKVPFX-basic-store-type-dialog.png)

### Advanced Tab
| Attribute | Value | Description |
| --------- | ----- | ----- |
| Supports Custom Alias | Optional | Determines if an individual entry within a store can have a custom Alias. |
| Private Key Handling | Optional | This determines if Keyfactor can send the private key associated with a certificate to the store. Required because IIS certificates without private keys would be invalid. |
| PFX Password Style | Default | 'Default' - PFX password is randomly generated, 'Custom' - PFX password may be specified when the enrollment job is created (Requires the Allow Custom Password application setting to be enabled.) |

The Advanced tab should look like this:

![HCVKVPFX Advanced Tab](../docsource/images/HCVKVPFX-advanced-store-type-dialog.png)

### Custom Fields Tab
Custom fields operate at the certificate store level and are used to control how the orchestrator connects to the remote target server containing the certificate store to be managed. The following custom fields should be added to the store type:

| Name | Display Name | Type | Default Value/Options | Required | Description |
| ---- | ------------ | ---- | --------------------- | -------- | ----------- |


The Custom Fields tab should look like this:

![HCVKVPFX Custom Fields Tab](../docsource/images/HCVKVPFX-custom-fields-store-type-dialog.png)



</details>

## Certificate Store Configuration

After creating the `HCVKVPFX` Certificate Store Type and installing the for Hashicorp Vault Universal Orchestrator extension, you can create new [Certificate Stores](https://software.keyfactor.com/Core-OnPrem/Current/Content/ReferenceGuide/Certificate%20Stores.htm?Highlight=certificate%20store) to manage certificates in the remote platform.

The following table describes the required and optional fields for the `HCVKVPFX` certificate store type.

| Attribute | Description | Attribute is PAM Eligible |
| --------- | ----------- | ------------------------- |
| Category | Select "Hashicorp Vault Key-Value PFX" or the customized certificate store name from the previous step. | |
| Container | Optional container to associate certificate store with. | |
| Client Machine | An identifier for the client machine which could be the host name of the Orchestrator or any meaningful label. This value is not used by the Hashicorp Vault Key-Value PFX extension. | |
| Store Path | The specific path within the Hashicorp Vault's Key-Value secrets engine where the PFX certificate files will be stored. Example: 'kv-v2/pfx-certificates'. | |
| Orchestrator | Select an approved orchestrator capable of managing `HCVKVPFX` certificates. Specifically, one with the `HCVKVPFX` capability. | |

* **Using kfutil**

    ```shell
    # Generate a CSV template for the AzureApp certificate store
    kfutil stores import generate-template --store-type-name HCVKVPFX --outpath HCVKVPFX.csv

    # Open the CSV file and fill in the required fields for each certificate store.

    # Import the CSV file to create the certificate stores
    kfutil stores import csv --store-type-name HCVKVPFX --file HCVKVPFX.csv
    ```

* **Manually with the Command UI**: In Keyfactor Command, navigate to Certificate Stores from the Locations Menu. Click the Add button to create a new Certificate Store using the attributes in the table above.