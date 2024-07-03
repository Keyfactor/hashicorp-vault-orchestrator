## Overview

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

