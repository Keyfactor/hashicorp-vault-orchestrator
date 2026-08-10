## 4.0.0

* **Breaking Change:** HCVKVPEM now reads the certificate and (optional) private key from two independent Vault secrets, matching the `PassphrasePath` convention already used by HCVKVJKS/HCVKVP12/HCVKVPFX, instead of requiring both `certificate` and `private_key` as JSON properties within a single combined secret. This allows PEM secrets to be created through tooling that can only produce a single key-value pair per secret. Existing HCVKVPEM certificate stores must be reconfigured in Keyfactor Command after upgrading — there is no automatic migration or legacy-shape fallback.
* **Breaking Change:** `StorePath` for HCVKVPEM now points to the single secret containing the certificate (supporting the same optional `?propName` JSON-property suffix already used by the other Key-Value store types), rather than a folder that could contain many certificate entries across sub-paths.
* **Breaking Change:** The `SubfolderInventory` custom field has been removed from HCVKVPEM — a PEM store is now exactly one certificate (plus an optional private key), not a folder of many.
* **added:** `PassphrasePath` custom field to HCVKVPEM, pointing to the secret holding the PEM-encoded private key. Optional — omit it for certificate-only / CA trust chain stores that have no private key. Unlike the other Key-Value store types, no sibling-secret convention is assumed when omitted.
* **added:** `DiscoverySuffix` job property to HCVKVPEM, HCVKVJKS, HCVKVP12, and HCVKVPFX, allowing the secret-key-name suffix a Discovery job matches on to be customized per organization convention. Defaults are unchanged for JKS/P12/PFX (`_jks`/`_p12`/`_pfx`); HCVKVPEM's default changes from `certificate` to `_pem`.
* **fix:** Discovery's discovered store path is now correct when the matched suffix is a true JSON sub-property rather than the whole secret's only key (previously produced a non-existent path by appending the property name as a further path segment). Affects HCVKVJKS/HCVKVP12/HCVKVPFX as well as HCVKVPEM.
* **fix:** Management-Add on HCVKVPEM now fails clearly when the certificate being added includes a private key but no `PassphrasePath` is configured, instead of silently dropping the key.
* **fix:** `JobProperties.PassphraseSecretPath`/`PassphraseSecretPropName` no longer throw a `NullReferenceException` when `PassphrasePath` is legitimately unset (e.g. HCVKVPEM with no private key).
* **tests:** Added coverage for HCVKVPEM Create/Add/Inventory/Remove and for the Discovery `GetVaults` suffix-matching logic (previously untested for all KV store types).
* **removed:** Newtonsoft.Json is no longer used directly anywhere in this project's code — all JSON parsing/serialization now uses `System.Text.Json`. It can still appear as a transitive package (via `Keyfactor.Orchestrators.IOrchestratorJobExtensions` 0.7.0's nuspec, which declares but never actually references it), so both projects now carry an explicit pinned reference to a patched version (13.0.3) to override the vulnerable 12.0.3 that would otherwise resolve (GHSA-5crp-9r3c-p9vr).

## 3.2.1

* **bug fix:** Inventory and Management jobs against a Vault Enterprise namespaced KV store no longer fail with `permission denied` when the Vault token is scoped to a namespace. The `X-Vault-Namespace` header is now sent on all job types (Inventory, Management, Discovery) when a namespace is detected in the `MountPoint` field, not just Discovery.
* **bug fix:** `sys/mounts` returning HTTP 403 (token lacks `read` on `sys/mounts`) no longer crashes the job. The extension now logs a warning, defaults to KV v2, and continues normally.
* **bug fix:** KV v1 engine version was not being cached after detection, causing a redundant `sys/mounts` call on every subsequent operation within the same job.
* **fix:** `MountPoint` field now supports the `<namespace>/<mount>` format on Inventory and Management jobs. The namespace is parsed by splitting on the last `/`, supporting nested namespaces (e.g. `ep/common/secret` → namespace `ep/common`, mount `secret`). Previously this parsing only occurred for Discovery jobs.
* **build:** Added `net10.0` to `TargetFrameworks` for compatibility with Universal Orchestrator 25.5.x.
* **fix:** Management-Add against a file-format store (HCVKVPFX, HCVKVJKS, HCVKVP12) that was never successfully Created now auto-seeds an empty store and passphrase on first use rather than failing with a 404.
* **fix:** `StorePath` trailing slash normalization now applied consistently for PEM and PKI store types regardless of whether the value came from store properties or the job configuration directly.
* **fix:** `GetTokenPoliciesAsync` failure during job initialization no longer crashes the orchestrator process — errors are caught and logged at debug level.
* **tests:** Added `hashicorp-vault-orchestrator.Tests` xUnit project covering KV version detection (including 403 fallback and caching), Enterprise namespace/mount parsing, CreateFileStore path correctness, and Management-Add write behavior.
* **docs:** Updated README Security Considerations with `sys/mounts` permission requirement, a minimum recommended HCL policy example, and Vault Enterprise namespace guidance. Added Enterprise namespace parsing notes to all `MountPoint` field descriptions.

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