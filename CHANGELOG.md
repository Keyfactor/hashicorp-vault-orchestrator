## 3.2.0
* added parameter "PassphrasePath" to support custom passphrase path (no longer needs to be a secret named 'passphrase' on the same level)
* added support for optional parameter on store path and passphrase path to indicate the property containing the value (if JSON secret)
* the additional parameter and JSON property identifier apply to the following store types: HCVKVJKS, HCVKVP12, HCVKVPKS

## 3.1.3

* documentation fix

## 3.1.2

* doctool migration and documentation improvements
* now support dual build for .NET 6.0 and .NET 8.0
* removed unused "subfolder inventory" parameter from store type definition for HCVKVPFX, HCVKVP12, and HCVKVJKS store types.

## 3.1.1

* bug fix: no longer stripping slashes from a mountpoint that includes them

## 3.1.0

* Added support for enterprise namespaces and alternate mount-points during discovery by allowing the value to be entered in the "directories to search" field.
* When error occurs attempting to load a JKS format certificate store, we will now attempt to load it as PKCS12 before failing.

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