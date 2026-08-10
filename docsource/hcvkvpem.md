## Overview

The Hashicorp Vault Key-Value PEM Certificate Store manages certificates in the PEM format that are stored in the Hashicorp Vault Key-Value secrets engine.
As of version 4.0+ of this integration, each HCVKVPEM certificate store maps to a single certificate secret (plus an optional, separate private key secret) — the same "one store, one secret" model already used by HCVKVJKS, HCVKVP12, and HCVKVPFX — rather than a folder that could contain many certificate entries across sub-paths.

> :warning: **Breaking change note for existing HCVKVPEM stores (upgrading from a version prior to 4.0):** `StorePath` used to be a folder path that could contain many certificates, optionally including sub-paths (via the now-removed `SubfolderInventory` field). It now points directly to the single secret containing the certificate. The private key, which used to live as a `private_key` property alongside `certificate` in that same secret, is now read from a separate secret referenced by the new `PassphrasePath` field. Existing HCVKVPEM certificate stores must be reconfigured after upgrading — there is no automatic migration.

## Requirements

### Secret naming

A certificate store is comprised of one or two secret entries:
- The certificate, at the path configured in `StorePath`.
- Optionally, a secret containing the PEM-encoded private key, at the path configured in `PassphrasePath`. Omit `PassphrasePath` entirely for certificate-only stores (e.g. a CA trust chain) that have no private key — unlike the other Key-Value store types, no sibling-secret convention (such as a secret named `passphrase` at the same level) is assumed when it's omitted.

This is what allows a PEM certificate and its private key to each be created as their own secret containing a single key-value pair — useful when your secret-management tooling only supports creating secrets with a single key-value pair per secret, which the old combined-secret shape did not allow.

Additionally, we can read the certificate and/or private key from a JSON secret that contains the value on a specific property.
To indicate the property name that should be used to retrieve the value, add a "?" at the end of the path, followed by the property name.

**examples:**

StorePath = `kv-v2/mycerts/mycert_pem?certData`
> This path indicates that the secret containing the certificate is named "mycert_pem" and is a JSON secret with the `certData` property containing the PEM-formatted certificate.
>

StorePath = `kv-v2/mycerts/mycert_pem`
> This path indicates that the entire secret value is the PEM-formatted certificate.

> Generally, the paths to the certificate and private key secrets should be in the following format
> `<namespace>/<mount point>/<path-to-secret>?<json property name>`
> if namespaces are not used, that section can be omitted.

This convention applies to both `StorePath` and `PassphrasePath`.

## Configuration in Keyfactor Command

### Create the Store Type

Here are the steps for manually creating the store type in Keyfactor Command.

- Log into Keyfactor Command as Administrator or a user with permissions to add certificate store types.
- Click on the gear icon in the top right and then navigate to the "Certificate Store Types"
- Click "Add" and enter the following information:

- Set the following values in the "Basic" tab:
  - **Name:** "Hashicorp PEM Certificate Store" (or another preferred name)
  - **Short Name:** "HCVKVPEM"
  - **Supported Job Types** - "Inventory", "Add", "Remove", "Discovery"
  - **Needs Server** - should be checked (true).

![](images/cert-store-type-kv-pem-basic-tab.png)

- Click the "Advanced" tab and update the following:
  - **Supports Custom Alias** - "Required"
  - **Private Key Handling** - "Optional"

![](images/cert-store-type-kv-advanced-tab.png)

- Click the "Custom Fields" tab to add the following custom fields:
  - **MountPoint** - Type: *string*
  - **IncludeCertChain** - Type: *bool* (If true, the available intermediate certificates will also be written to Vault during enrollment)
  - **PassphrasePath** - Type: *string* (The path to the secret containing the PEM-encoded private key. Optional — omit for certificate-only / CA trust chain stores with no private key)

![](images/cert-store-type-kv-custom-tab.png)

- Click **Save** to save the new Store Type.

#### Create a Certificate Store

- Navigate to **Locations** > **Certificate Stores** from the main menu
- Click **ADD** to open the new Certificate Store Dialog

Create a new Certificate Store that resembles the one below:

![](images/cert-store-add-pem.png)

- **Client Machine** - Enter an identifier for the client machine.  This could be the Orchestrator host name, or anything else useful.  This value is not used by the extension.
- **Store Path** - This is the path to the secret containing the certificate.
  - example: `kv-v2\kf-secrets\mycert_pem`
- **Mount Point** - This is the mount point name for the instance of the Key Value secrets engine.
  - If left blank, will default to "kv-v2".
  - If your organization utilizes Vault enterprise namespaces, you should include the namespace here.
- **Passphrase Path** - The path to the secret (and optional JSON property) where the PEM-encoded private key is located. Leave blank for certificate-only stores with no private key.

#### Set the server username and password

- **SERVER USERNAME** should be the full URL to the instance of Vault that will be accessible by the orchestrator. (example: `http://127.0.0.1:8200`)
- **SERVER PASSWORD** should be the Vault token that will be used for authenticating.

At this point, the certificate store should be created and ready to perform inventory on your certificate stored in the Key-Value secrets engine.

## Discovery Job Configuration

When the discovery job is executed, it will scan the provided vault path, and any sub-paths contained within it.
The certificate store entry is returned from a discovery job when..

1. A secret entry is found with a key ending in the configured Discovery Suffix (defaults to `_pem`; see below).
1. The entry for the certificate contains the PEM formatted certificate file.

**Note**: Key/Value secrets that do not include a key ending in the Discovery Suffix will be ignored during discovery.

Set the following fields to configure a discovery job for PEM Certificate Stores:
- **Client Machine** - any string; it is unused by the Discovery job
- **SERVER USERNAME** - the full URL to the instance of Vault
- **SERVER PASSWORD** - the Vault Token to be used by the Orchestrator for authenticating into Vault
- **Directories to Search** - used to restrict the certificate store search to a sub-path within the Secrets Engine
- **Extensions** - The namespace (if used) and mount-point of the secrets engine to search.
- **Discovery Suffix** (custom job property) - Overrides the default secret-key-name suffix (`_pem`) Discovery uses to identify candidate PEM certificate secrets. Use this if your organization's secret-naming convention doesn't end in `_pem`.

> :warning: *If your mount point is different than the default "kv-v2" and/or enterprise namespaces are used, you should enter the mount point and namespace into the "Extensions" field in order for discovery to work.  Also, if you need to scope discovery to a sub-path rather than the root of the engine mount point, enter that in the "Directories to search" field.*

![](images/discovery.png)

**Note**: The discovery job will return a collection of secret paths beneath the provided root path whose keys end in the configured Discovery Suffix.
