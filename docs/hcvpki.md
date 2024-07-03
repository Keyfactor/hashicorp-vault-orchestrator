## Hashicorp Vault PKI

The Hashicorp Vault PKI Certificate Store Type allows users to manage and inventory certificates directly on a Hashicorp Vault instance using the PKI or Keyfactor Plugin secrets engines. This store type is intended for managing certificates issued and stored within the Vault's PKI, enabling seamless integration with Keyfactor Command for efficient certificate lifecycle management.

### Representation and Functionality

The Hashicorp Vault PKI Certificate Store Type represents a path within Vault where certificates are stored and managed using either the native PKI engine or the Keyfactor Secrets Engine plugin. This configuration allows for streamlined certificate management, including inventory operations to keep track of all certificates within a specified Vault path.

### Caveats and Considerations

There are a few considerations to be aware of when using this store type:

- **Mount Point Configuration:** It's crucial to correctly specify the mount point for the PKI or Keyfactor Plugin secrets engines. This ensures that the orchestrator can accurately access and manage the certificates.
- **Vault Token Requirements:** The token used for Vault interactions must be configured with appropriate policies to permit read and list operations on the certificate path. Incorrect or insufficient permissions will impede the functionality of the certificate store.

### Limitations and Potential Confusion

The Hashicorp Vault PKI Certificate Store Type primarily supports inventory operations. This is a limitation to note if you require more extensive management capabilities such as adding or removing certificates. Additionally, users need to be careful with the path configurations to ensure accurate inventory results and avoid potential errors.

### SDK Use

While the documentation does not explicitly mention the use of an SDK, interactions are performed through the Hashicorp Vault API, implying that API clients or services are employed by the Keyfactor Command orchestrator to facilitate required operations.

### Summary

In summary, the Hashicorp Vault PKI Certificate Store Type is specialized for managing certificates stored in Vault's PKI or Keyfactor Plugin secrets engines. It focuses on inventory operations, representing a specific path within the Vault. Proper configuration of mount points and Vault tokens is essential for proper operation, and while it provides robust inventory capabilities, users should be aware of its limitations regarding additional management operations.



### Supported Job Types

| Job Name | Supported |
| -------- | --------- |
| Inventory | ✅ |
| Management Add |  |
| Management Remove |  |
| Discovery |  |
| Create |  |
| Reenrollment |  |

## Requirements

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



## Certificate Store Type Configuration

The recommended method for creating the `HCVPKI` Certificate Store Type is to use [kfutil](https://github.com/Keyfactor/kfutil). After installing, use the following command to create the `` Certificate Store Type:

```shell
kfutil store-types create HCVPKI
```

<details><summary>HCVPKI</summary>

Create a store type called `HCVPKI` with the attributes in the tables below:

### Basic Tab
| Attribute | Value | Description |
| --------- | ----- | ----- |
| Name | Hashicorp Vault PKI | Display name for the store type (may be customized) |
| Short Name | HCVPKI | Short display name for the store type |
| Capability | HCVPKI | Store type name orchestrator will register with. Check the box to allow entry of value |
| Supported Job Types (check the box for each) | Add, Discovery, Remove | Job types the extension supports |
| Supports Add |  |  Indicates that the Store Type supports Management Add |
| Supports Remove |  |  Indicates that the Store Type supports Management Remove |
| Supports Discovery |  |  Indicates that the Store Type supports Discovery |
| Supports Reenrollment |  |  Indicates that the Store Type supports Reenrollment |
| Supports Create |  |  Indicates that the Store Type supports store creation |
| Needs Server |  | Determines if a target server name is required when creating store |
| Blueprint Allowed |  | Determines if store type may be included in an Orchestrator blueprint |
| Uses PowerShell |  | Determines if underlying implementation is PowerShell |
| Requires Store Password |  | Determines if a store password is required when configuring an individual store. |
| Supports Entry Password |  | Determines if an individual entry within a store can have a password. |

The Basic tab should look like this:

![HCVPKI Basic Tab](../docsource/images/HCVPKI-basic-store-type-dialog.png)

### Advanced Tab
| Attribute | Value | Description |
| --------- | ----- | ----- |
| Supports Custom Alias | Optional | Determines if an individual entry within a store can have a custom Alias. |
| Private Key Handling | Optional | This determines if Keyfactor can send the private key associated with a certificate to the store. Required because IIS certificates without private keys would be invalid. |
| PFX Password Style | Default | 'Default' - PFX password is randomly generated, 'Custom' - PFX password may be specified when the enrollment job is created (Requires the Allow Custom Password application setting to be enabled.) |

The Advanced tab should look like this:

![HCVPKI Advanced Tab](../docsource/images/HCVPKI-advanced-store-type-dialog.png)

### Custom Fields Tab
Custom fields operate at the certificate store level and are used to control how the orchestrator connects to the remote target server containing the certificate store to be managed. The following custom fields should be added to the store type:

| Name | Display Name | Type | Default Value/Options | Required | Description |
| ---- | ------------ | ---- | --------------------- | -------- | ----------- |


The Custom Fields tab should look like this:

![HCVPKI Custom Fields Tab](../docsource/images/HCVPKI-custom-fields-store-type-dialog.png)



</details>

## Certificate Store Configuration

After creating the `HCVPKI` Certificate Store Type and installing the for Hashicorp Vault Universal Orchestrator extension, you can create new [Certificate Stores](https://software.keyfactor.com/Core-OnPrem/Current/Content/ReferenceGuide/Certificate%20Stores.htm?Highlight=certificate%20store) to manage certificates in the remote platform.

The following table describes the required and optional fields for the `HCVPKI` certificate store type.

| Attribute | Description | Attribute is PAM Eligible |
| --------- | ----------- | ------------------------- |
| Category | Select "Hashicorp Vault PKI" or the customized certificate store name from the previous step. | |
| Container | Optional container to associate certificate store with. | |
| Client Machine | Enter the full URL for the Vault host machine where the Hashicorp Vault PKI secrets engine is running. Example: `http://127.0.0.1:8200`. | |
| Store Path | Enter the path for the PKI secrets engine within Vault. This is typically set to `/` for the Hashicorp Vault PKI store type. Example: `/`. | |
| Orchestrator | Select an approved orchestrator capable of managing `HCVPKI` certificates. Specifically, one with the `HCVPKI` capability. | |

* **Using kfutil**

    ```shell
    # Generate a CSV template for the AzureApp certificate store
    kfutil stores import generate-template --store-type-name HCVPKI --outpath HCVPKI.csv

    # Open the CSV file and fill in the required fields for each certificate store.

    # Import the CSV file to create the certificate stores
    kfutil stores import csv --store-type-name HCVPKI --file HCVPKI.csv
    ```

* **Manually with the Command UI**: In Keyfactor Command, navigate to Certificate Stores from the Locations Menu. Click the Add button to create a new Certificate Store using the attributes in the table above.