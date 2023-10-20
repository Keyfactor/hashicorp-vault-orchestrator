## 3.0.0

* Added support for JKS, PKCS12 and PFX file stores in the Hashicorp Vault Key-Value secrets engine.
* Added PAM support for server credentials.

* **Breaking Changes**
    * The server url and Vault Token have been moved to the server username and server password fields of server credentials, respectively.
    * The HCVKV store type for PEM files has been renamed to HCVKVPEM
    

## 2.0.0

* Added support for storing certs in sub-paths
* Updated documentation to specify storing the token as a secret.
* Added inventory job support for the Hashicorp PKI secrets engine
* Added inventory job support for the Keyfactor secrets engine

* **Breaking Change**: the properties have been renamed from:
    * `PUBLIC_KEY` to `certificate`
    * `PRIVATE_KEY` to `private_key`
    * `PUBLIC_KEY_<n>` has been removed.  Now the chain is stored in `certificate` if the option is selected.

* **Breaking Change**: Added a flag on the Keyfactor Certificate store definition to indicate whether to store the full CA chain along with the certificate


* **Breaking Change**: the cert store types are now:
    * **HCVPKI** for the PKI and Keyfactor secrets engine
    * **HCVKV** for the Key-Value secrets engine