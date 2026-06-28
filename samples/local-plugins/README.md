# GoatShot Local Plugin Samples

These fixtures document the local plugin manifest shape used by the current Plugin SDK tranche.

Copy a sample plugin folder into GoatShot's app-owned plugin folder, shown by `goatshot plugins list`, to inspect it locally. Discovered plugins are disabled and untrusted by default. A plugin action only reaches dry-run approval when all three settings are present:

- `EnableLocalPlugins = true`
- plugin id in `TrustedPluginIds`
- plugin id in `EnabledPluginIds`
- action id, or `plugin-id:*`, in `AllowedPluginActionIds`

The current SDK validates manifests, reports diagnostics, dry-runs actions, and can run an action that declares an `execution` block after the plugin is trusted, enabled, and allowlisted. Local execution captures redacted stdout/stderr and enforces a bounded timeout.

Remote plugin package acquisition is staged separately from local execution:

- `registry.json` shows the `goatshot.plugin-registry.v1` shape.
- `goatshot plugins registry validate samples\local-plugins\registry.json` validates registry metadata.
- `goatshot plugins install-plan sample.redaction-note --registry samples\local-plugins\registry.json` reports whether a package can be staged.
- `goatshot plugins marketplace-plan --registry samples\local-plugins\registry.json` reports read-only marketplace governance, registry/local/staged/plugin-policy state, operator gates, privacy boundaries, and later-scope non-goals without downloading or mutating anything.
- `goatshot plugins schedule-updates --registry samples\local-plugins\registry.json --output artifacts\plugin-update-schedule` writes a local Task Scheduler handoff for the governed background update runner without registering a task by itself.
- `goatshot plugins update-task status --manifest artifacts\plugin-update-schedule\plugin-update-schedule.json` queries the generated task name, while `register --accept-task-registration` and `unregister --accept-task-removal` are the explicit scheduler lifecycle commands. Add `--dry-run` to prove register/unregister wiring without mutating Task Scheduler.
- `goatshot plugins stage sample.redaction-note --registry <registry>` downloads or copies the referenced ZIP into GoatShot's app-owned staging folder after SHA-256, size, optional RSA SHA-256 package signature, manifest, compatibility, and archive traversal checks.

Registry entries can include `signature`, `signatureAlgorithm`, and `signaturePublicKeyPem` to verify a package signature before staging. Supported algorithms are `rsa-pss-sha256` and `rsa-pkcs1-sha256`; unsigned entries remain checksum-only and are reported with a warning.

Staged packages are not copied into the active plugin root, trusted, enabled, allowlisted, or executed automatically. Signature verification does not imply trust or execution approval. Scheduler handoff scripts and `plugins update-task` lifecycle commands can run, register, query, or unregister check-only/stage-only background update tasks, but they do not install, trust, enable, allowlist, or execute plugin code. Hosted marketplaces, accounts, payments, ratings, remote execution, automatic trust/execute updates, and automatic self-registering updater services remain out of scope.
