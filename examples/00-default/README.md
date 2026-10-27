# 00 — Default (complete reference)

The most complete `udp.yml` in the repository. One deployment definition that uses **every** top-level section, field, resource type, tenant setting, security option, and Purview-related setting that udpi30 supports. Treat it as a **copy-paste catalogue** — real projects keep only what they need.

| Area | Coverage |
| --- | --- |
| Top-level sections | `deployment`, `workspace` (incl. `git_integration`), `variables` (short + long form), `azure`, `resources`, `security`, `connections`, `policies`, `notifications`, `state`, `admin`, `targets`; `include` / `extends` shown commented |
| Fabric resources | All 46 item types, every field, every pipeline schedule frequency, file and AI shortcut transformations |
| Entra | `entra_groups`, `entra_apps` (all fields, all sign-in audiences) |
| Azure | `azure_resource_groups`, `azure_storage_accounts`, `azure_deployments` (group + subscription scope) and all 61 `azure_*` service types |
| Tenant settings | **All 178** settings the tenant exposes, in portal groups, with delegation, security groups, and all five property types |
| Purview / information protection | Tenant settings, Purview account, scanner identity, private DNS, diagnostics, lock, private endpoints, policy and Defender baseline |
| Security | Workspace roles (group, user, service principal), OneLake data-access roles (`read`, `write`, `readwrite`) |
| Targets | `dev`, `staging`, `prod` (canary, run-as, overrides, post-deploy checks), `existing` (`workspace_id`) |

## Use it

```bash
cd examples/00-default

udpi30 validate --skip-connection-check --strict   # offline structural check
udpi30 graph                                       # dependency graph (148 resources)
udpi30 plan -t dev                                 # dry-run
udpi30 admin plan                                  # READ-ONLY tenant-settings diff (Fabric admin required)
```

Placeholders are zero-GUIDs and `REPLACE-WITH-*` values so the file validates unchanged and fails loudly if deployed unchanged. Set real values with `variables:` defaults, a target's `variables:`, or your pipeline.

> **Do not run `udpi30 admin apply` with this file as-is.** The `admin:` block is an opinionated baseline that changes org-wide policy for every user. Run `admin plan`, review each line, and keep only the settings you intend to manage.

## Tenant settings (`admin:`)

Names are the API `settingName` values from `GET /v1/admin/tenantsettings`, grouped as in the Admin portal, each with its portal title as a comment. The posture is intentionally conservative:

- External sharing, guest access, Publish to web, template apps, SAS tokens: off or limited to `fabric_external_sharing_group_id` (contractors excluded).
- Item creation and preview items: `fabric_creators_group_id`.
- Copilot and AI: `fabric_ai_users_group_id`; cross-region AI data movement off.
- Admin APIs / service principals: `fabric_automation_group_id`.
- Tenant Private Link is allowed, but **public access is not blocked** — blocking before your private network works can lock everyone out.

`delegate_to_*` appear only on settings where the API exposes the flag, and security groups only where `canSpecifySecurityGroups` is true. The portal's settings list changes over time; `admin plan` flags any name your tenant doesn't have.

## Purview scope

What udpi30 can express, and where it is in this example:

| Purview concern | Where |
| --- | --- |
| Sensitivity labels, inheritance, protected-label link blocking, Purview AI-interaction security, domain default labels | `admin:` → *Information protection* settings |
| Metadata scanning of Fabric / Power BI by a service principal | `AdminApisIncludeDetailedMetadata`, `AdminApisIncludeExpressions`, `AllowServicePrincipalsUseReadAdminAPIs` (scoped to `fabric_automation_group_id`), the `app-purview-scanner` Entra app, and its `viewer` role under `security:` |
| Purview account | `azure_purview_accounts` (public access off, managed event hub on) |
| Private connectivity | `azure_private_dns_zones` (`privatelink.purview*.azure.com`, blob, queue, Service Bus), private endpoints in `bicep/purview-governance.bicep` |
| Diagnostics, delete lock | `bicep/purview-governance.bicep` |
| Governance guardrails | `bicep/policy-baseline.bicep` (audit-only tag policy; Defender plans behind `enableDefender`) |

**Not expressible in udp.yml:** Purview *data-plane* objects — collections, data-source registrations and scans, glossary, classification rules, and sensitivity-label / DLP policies (these live in the Purview portal, Microsoft Graph, or Security & Compliance tooling). Enable the tenant-level hooks here, then configure those in Purview.

## Things to know

- **Contract-only Azure entries.** The generated Bicep emits only `location`, `sku.name`, `kind`, `properties`, and `tags`. Types that need `sku.capacity`/`tier`, identity, credentials, a parent, or an extension scope (IoT Hub, SQL database, Sentinel, diagnostic settings, Defender plans, …) are inventory entries — deploy them with `azure_deployments`, as the two `bicep/` templates do. Never put credentials in `properties`.
- **Resource keys are global.** They must be unique across *all* types, Fabric, Entra and Azure alike.
- **Policies.** `require_description` and `naming_convention: snake_case` apply to every key, including Azure and Entra names, so they are left commented out here.
- **Asset files are stubs.** Notebooks, SQL, KQL, TMDL, PBIR and the other definition files exist so every `path:` resolves; replace them with real definitions (for example, via `udpi30 export`) before deploying. Capacity-gated and list-only types (marked in `udp.yml`) may still not deploy on your capacity.
- **Not part of the deployment test harness.** The harness contract is exactly the 14 numbered examples; this catalogue touches tenant, Entra and Azure scopes at once, so validate it, don't mass-deploy it.
- **Connections.** `validate` opens TCP sockets to connection endpoints; use `--skip-connection-check` for the placeholder hosts here.
