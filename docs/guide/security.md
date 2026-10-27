# Security and Permissions

This page documents the `security` section of `udp.yml`: workspace role assignments, OneLake data access roles, and row-level and column-level security. udpi30 resolves Entra ID display names to GUIDs at deploy time, so the YAML can use human-readable names instead of opaque identifiers.

Workspace security roles are a stable feature. OneLake data access roles are in beta.

---

## 1. Workspace roles

Fabric workspaces have four built-in roles:

| Role | Capabilities |
|------|-------------|
| **Admin** | Full control. Manage access, delete the workspace, configure settings. |
| **Member** | Create, edit, and delete all items. Share items. Cannot manage workspace settings. |
| **Contributor** | Create, edit, and delete items they own. Cannot share items or manage access. |
| **Viewer** | View items and run reports. Cannot edit or create items. |

Assign roles to Entra ID groups, individual users, or service principals:

```yaml
security:
  roles:
    - name: data_engineers
      entra_group: sg-data-engineering
      workspace_role: contributor

    - name: analysts
      entra_group: sg-analytics-team
      workspace_role: viewer

    - name: project_lead
      entra_user: jane.doe@contoso.com
      workspace_role: admin

    - name: cicd_deployer
      service_principal: sp-udpi30
      workspace_role: admin
```

Each role entry requires exactly one principal and one `workspace_role`:

| Principal field | Identifies |
|-----------------|------------|
| `entra_group` | An Entra ID group, by display name or GUID |
| `entra_user` | A user, by UPN (for example, `jane.doe@contoso.com`) |
| `service_principal` | A service principal, by display name or application ID |

---

## 2. Entra ID group resolution

Entra ID groups and service principals can be referenced by display name or by GUID. When a display name is used, udpi30 calls the Microsoft Graph API at deploy time to resolve it to the object's GUID.

```yaml
# Display name (resolved via Graph API)
entra_group: sg-data-engineering

# GUID (used directly, no Graph API call)
entra_group: "a1b2c3d4-e5f6-7890-abcd-ef1234567890"
```

Display name resolution requires the deploying identity (your user account or the CI/CD service principal) to have `GroupMember.Read.All` or `Group.Read.All` permission in Microsoft Graph. If the permission is missing, udpi30 reports an error suggesting GUIDs instead.

Using GUIDs is faster (no API call) and avoids ambiguity when multiple groups share similar names.

---

## 3. OneLake data access roles

OneLake data access roles provide fine-grained, table-level and folder-level access control within a lakehouse. They operate below the workspace role level: a user can have Viewer access to the workspace but read access to only specific tables. This feature is in beta.

```yaml
security:
  roles:
    - name: sales_analysts
      entra_group: sg-sales-team
      workspace_role: viewer
      onelake_roles:
        - tables: ["fact_sales", "dim_customer", "dim_product"]
          permissions: [read]

    - name: data_engineers
      entra_group: sg-data-engineering
      workspace_role: contributor
      onelake_roles:
        - tables: ["*"]
          permissions: [read, write]
        - folders: ["raw/*"]
          permissions: [read, write]

    - name: finance
      entra_group: sg-finance
      workspace_role: viewer
      onelake_roles:
        - tables: ["fact_revenue", "dim_cost_center"]
          permissions: [read]
        - folders: ["reports/finance/*"]
          permissions: [read]
```

### 3.1 Supported permissions

| Permission | Description |
|-----------|-------------|
| `read` | Read data from the specified tables or folders |
| `write` | Write data to the specified tables or folders |

### 3.2 Wildcard patterns

| Pattern | Matches |
|---------|---------|
| `["*"]` | All tables or all folders in the lakehouse |
| `["raw/*"]` | All items under the `raw/` folder path |
| `["dim_*"]` | All tables whose names start with `dim_` |

!!! warning "Portal prerequisite"
    OneLake data access roles must be enabled per-lakehouse in the Fabric portal before udpi30 can manage them. Go to the lakehouse settings in the portal and enable **OneLake data access roles (Preview)**. Without this, the API calls will fail with a 403 error.

---

## 4. Row-level security

Row-level security (RLS) restricts which rows a user can see in a semantic model. RLS is **not managed by udpi30**: the `security` section supports workspace roles and OneLake data access roles only.

To apply RLS, define the roles and DAX filter expressions inside the semantic model itself (TMDL definition or Power BI Desktop) and manage role membership through the Fabric portal or the Power BI REST API. Because udpi30 deploys semantic model definitions from your `path` source, RLS roles defined in TMDL travel with the model on every deploy; only the member assignments need separate management.

---

## 5. Column-level security

Column-level security (CLS) restricts which columns a user can see. Like RLS, CLS is **not managed by udpi30**.

To apply CLS, use object-level security in the semantic model definition (TMDL), or enforce column restrictions upstream in the warehouse with T-SQL (`GRANT`/`DENY` on columns). For lakehouse tables, OneLake data access roles (section 3) control access at table and folder granularity, not column granularity.

---

## 6. Service principal permissions

The service principal used for CI/CD deployments needs specific permissions depending on what the deployment manages:

| Capability | Required Permission |
|-----------|-------------------|
| Create, update, delete items | Workspace **Contributor** or **Admin** role |
| Manage workspace role assignments | Workspace **Admin** role |
| Manage OneLake data access roles | Workspace **Admin** role |
| Resolve Entra display names to GUIDs | Microsoft Graph `Group.Read.All` (application permission) |

If the service principal only needs to deploy items without managing security, **Contributor** is sufficient. If the deployment includes a `security` section, **Admin** is required.

---

## 7. Security in CI/CD

In a CI/CD pipeline, the service principal authenticates via environment variables (`AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`) and must have the workspace roles listed above.

A common bootstrap pattern:

1. A workspace admin manually grants the service principal **Admin** access to each target workspace (dev, staging, prod) through the Fabric portal.
2. The service principal is also declared in the deployment's `security` section so that its access is codified and re-applied on every deploy:

    ```yaml
    security:
      roles:
        - name: cicd_deployer
          service_principal: sp-udpi30
          workspace_role: admin
    ```

3. From this point on, all security changes (adding new groups, modifying OneLake roles) go through the deployment and are deployed by the service principal.

See [Service Principal Setup](service-principal.md) for full instructions on creating the service principal, granting permissions, and configuring CI/CD secrets.

---

## 8. Full YAML example

```yaml
security:
  roles:
    # Workspace-level roles
    - name: data_engineers
      entra_group: sg-data-engineering
      workspace_role: contributor
      onelake_roles:
        - tables: ["*"]
          permissions: [read, write]

    - name: analysts
      entra_group: sg-analytics-team
      workspace_role: viewer
      onelake_roles:
        - tables: ["fact_sales", "dim_customer", "dim_product"]
          permissions: [read]

    - name: external_vendor
      entra_group: "b2c3d4e5-f6a7-8901-bcde-f12345678901"  # GUID for external group
      workspace_role: viewer
      onelake_roles:
        - tables: ["fact_sales_summary"]
          permissions: [read]

    - name: cicd_deployer
      service_principal: sp-udpi30
      workspace_role: admin
```
