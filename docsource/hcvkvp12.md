## Overview

The Hashicorp Vault Key-Value PKCS12 Certificate Store Type allows users to manage PKCS12 (P12) certificate files stored within Hashicorp Vault using the Key-Value secrets engine. This store type treats each PKCS12 file as a separate certificate store, enabling detailed management of these files through Keyfactor Command. It supports a range of operations such as discovery, inventory, and the addition and removal of certificates within the PKCS12 files.

### Representation and Functionality

The Hashicorp Vault Key-Value PKCS12 Certificate Store Type represents individual PKCS12 files stored in the Vault's Key-Value secrets engine. Each PKCS12 file is considered an independent store, facilitating the systematic management of multiple PKCS12 files. To operate correctly, the PKCS12 files need to be base64 encoded and stored with specific naming conventions.

### Caveats and Considerations

There are several important considerations when using this Certificate Store Type:

- **Base64 Encoding:** All PKCS12 files must be base64 encoded before being stored in Vault. This encoding ensures the files are properly recognized and managed.
- **Key Naming Convention:** The name (key) for each PKCS12 entry must end with the suffix '_p12' to be correctly identified during operations. Following this convention is critical for accurate inventory and management.
- **Passphrase Requirement:** Each PKCS12 file entry must include a `passphrase` field containing the password for the store. Omitting this field will cause the PKCS12 file to be ignored during inventory scans, leading to potential incomplete results.

### Limitations and Potential Confusion

The principal limitation of the Key-Value PKCS12 Certificate Store Type is its reliance on strict naming conventions and base64 encoding. Users must ensure that each entry is accurately named and encoded to avoid errors during management operations. Proper inclusion of the `passphrase` field is also crucial for successful inventory and management.

### SDK Use

Although the documentation does not explicitly mention the use of an SDK, interactions are facilitated through the Hashicorp Vault API. This implicitly suggests that the Keyfactor Command orchestrator employs an API client or service to execute the required operations.

### Summary

In summary, the Hashicorp Vault Key-Value PKCS12 Certificate Store Type provides a robust solution for managing PKCS12 files within Vault's Key-Value secrets engine. Treating each PKCS12 file as an independent store enhances manageability and organization. However, users must be vigilant about base64 encoding, adhering to strict naming conventions, and including passphrases to ensure smooth operations and accurate results.

## Requirements

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

