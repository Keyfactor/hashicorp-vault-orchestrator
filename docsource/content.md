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

