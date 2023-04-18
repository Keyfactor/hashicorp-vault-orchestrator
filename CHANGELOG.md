2.0.1
* Added support for storing certs in sub-paths
* Updated documentation to specify storing the token as a secret.

2.0
* Added inventory job support for the Hashicorp PKI secrets engine
* Added inventory job support for the Keyfactor secrets engine
* **Breaking Change**: the cert store types are now:
    * **HCVPKI** for the PKI and Keyfactor secrets engine
    * **HCVKV** for the Key-Value secrets engine
