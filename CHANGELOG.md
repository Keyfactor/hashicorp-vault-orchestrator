2.0.1

* Added support for storing certs in sub-paths
* Updated documentation to specify storing the token as a secret.

* **Breaking Change**: the properties have been renamed from:
    * `PUBLIC_KEY` to `certificate`
    * `PRIVATE_KEY` to `private_key`
    * `PUBLIC_KEY_<n>` has been removed.  Now the chain is stored in `certificate` if the option is selected.

* **Breaking Change**: Added a flag on the Keyfactor Certificate store definition to indicate whether to store the full CA chain along with the certificate

2.0

* Added inventory job support for the Hashicorp PKI secrets engine
* Added inventory job support for the Keyfactor secrets engine

* **Breaking Change**: the cert store types are now:
    * **HCVPKI** for the PKI and Keyfactor secrets engine
    * **HCVKV** for the Key-Value secrets engine