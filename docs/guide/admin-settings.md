# Admin / Tenant Settings

Declaratively manage **Fabric tenant (admin) settings** — the org-wide switches in the Admin portal's **Tenant settings** page — from your `udp.yml`, then apply them with a single gated command.

Unlike everything else in `udp.yml`, tenant settings are **not workspace items**. They apply tenant-wide through the [Fabric Admin API](https://learn.microsoft.com/en-us/rest/api/fabric/admin/tenants/update-tenant-setting), not per workspace. Because a single change affects every user in the organization, they live **outside** the normal `deploy` flow and are applied only by the explicit `udpi30 admin apply` command.

---

## 1. How it works

You declare the **desired state** of each setting keyed by its API **`settingName`** (for example `PublishToWeb`). On `admin plan`/`apply`, udpi30:

1. Reads your tenant's live settings via the [List Tenant Settings API](https://learn.microsoft.com/en-us/rest/api/fabric/admin/tenants/list-tenant-settings).
2. Validates every declared `settingName` against that live list — unknown names are reported, not silently ignored.
3. Diffs declared vs. live state and shows exactly what would change.
4. (`apply` only) Applies each change via the [Update Tenant Setting API](https://learn.microsoft.com/en-us/rest/api/fabric/admin/tenants/update-tenant-setting), paced under the API's 25-requests/minute limit.

Only settings you **explicitly declare** are touched. Security groups and properties are managed only when you declare them — udpi30 never clears them implicitly.

---

## 2. Finding the `settingName`

!!! important "Titles vs. identifiers"
    The [tenant settings index](https://learn.microsoft.com/en-us/fabric/admin/tenant-settings-index) lists **display titles** ("Publish to web", "Web content on dashboard tiles"), **not** the API `settingName` identifiers the Admin API requires. The authoritative list of identifiers comes from **your tenant**.

Get the real identifiers for your tenant:

```bash
az rest --method get \
  --url "https://api.fabric.microsoft.com/v1/admin/tenantsettings" \
  --resource "https://api.fabric.microsoft.com" \
  --query "tenantSettings[].{name:settingName, title:title, enabled:enabled}" -o table
```

Each row's `name` is what you put in `udp.yml`; `title` matches the [tenant settings index](https://learn.microsoft.com/en-us/fabric/admin/tenant-settings-index) so you can map a description to its identifier. The documented worked example is `PublishToWeb` (the "Publish to web" setting).

`udpi30 admin plan` also validates your names directly — any identifier that doesn't exist in your tenant is flagged as **unknown** so you get immediate feedback.

---

## 3. Declaring settings in `udp.yml`

```yaml
admin:
  tenant_settings:
    # Disable Publish to web org-wide
    PublishToWeb:
      enabled: false

    # Enable a setting only for specific security groups
    SomeFeatureSwitch:
      enabled: true
      enabled_security_groups:
        - graph_id: "f51b705f-a409-4d40-9197-c5d5f349e2f0"
          name: "Data Engineers"
      excluded_security_groups:
        - graph_id: "a1b2c3d4-0000-0000-0000-000000000000"
          name: "Contractors"

    # Allow workspace admins to override, and set a typed property
    DevelopmentTenantSettings:
      enabled: true
      delegate_to_workspace: true
      properties:
        - name: "MaxItems"
          value: "500"
          type: Integer
```

### Fields

| Field | Type | Description |
|-------|------|-------------|
| `enabled` | boolean (required) | Turn the setting on or off |
| `delegate_to_capacity` | boolean | Allow a capacity admin to override |
| `delegate_to_domain` | boolean | Allow a domain admin to override |
| `delegate_to_workspace` | boolean | Allow a workspace admin to override |
| `enabled_security_groups` | list | Groups the setting is enabled for (`graph_id` + `name`) |
| `excluded_security_groups` | list | Groups explicitly excluded |
| `properties` | list | Typed properties some settings require (`name`, `value`, `type`) |

`type` is one of `FreeText`, `Url`, `Boolean`, `MailEnabledSecurityGroup`, `Integer`. The `delegate_to_*` fields are only diffed/applied when you declare them, so you can leave them out to manage just `enabled`.

`${var.*}` substitution works inside any string value — handy for security-group GUIDs that differ per tenant.

---

## 4. Commands

```bash
# Preview changes against the live tenant (read-only)
udpi30 admin plan

# Apply — prompts for confirmation (tenant-wide!)
udpi30 admin apply

# Apply non-interactively (CI)
udpi30 admin apply -y

# Preview via apply without writing
udpi30 admin apply --dry-run
```

Sample `admin plan` output:

```
Tenant Settings Plan

  ~  PublishToWeb  Publish to web
      enabled: enabled -> disabled
  =  DevelopmentTenantSettings  no change
  !  PublishToWebb  (unknown setting)
      setting not found in this tenant — check the settingName

  Summary: 1 to update, 1 unchanged, 1 unknown
```

`admin apply` refuses to run while any setting is **unknown**, so a typo can't silently no-op.

---

## 5. Prerequisites

| Requirement | Detail |
|---|---|
| Permissions | A **Fabric administrator**, or a service principal with the `Tenant.ReadWrite.All` delegated scope |
| API status | The Update Tenant Setting API is **Preview** |
| Rate limit | 25 requests/minute (udpi30 paces `apply` automatically) |
| Propagation | Changes can take **up to 15 minutes** to take effect across the org |

```bash
# Service principal (CI)
export AZURE_TENANT_ID=...
export AZURE_CLIENT_ID=...
export AZURE_CLIENT_SECRET=...
udpi30 admin apply -y
```

---

## 6. Why not part of `deploy`?

`deploy` targets a single workspace and is safe to run per environment. Tenant settings are **tenant-global** — applying them on every `deploy --target dev` would repeatedly rewrite org-wide policy. Keeping them in a separate, explicitly-confirmed `admin apply` makes the blast radius obvious and lets you gate them behind a dedicated approval in CI.
