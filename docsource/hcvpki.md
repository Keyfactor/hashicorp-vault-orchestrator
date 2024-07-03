## Overview

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

