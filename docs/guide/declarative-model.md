# The declarative model

This page explains the declarative model used by Unified Data Platform Deployment: how desired state is defined in `udp.yml`, how the engine reconciles it against a live Microsoft Fabric workspace, and how this differs from imperative scripting. Read this page before writing your first deployment.

Unified Data Platform Deployment is declarative. You describe the desired state of your Microsoft Fabric project in `udp.yml`, and the CLI determines what to create, update, or delete to make the live workspace match.

---

## 1. Imperative vs. declarative

| Concern | Imperative (scripts) | Declarative (`udp.yml`) |
|---|---|---|
| You write | The steps to take | The end state you want |
| Order | You sequence every call | Resolved from a dependency graph |
| Re-runs | May fail or duplicate | Converge on the same state (idempotent) |
| Drift | Invisible until something breaks | Detected by comparing desired vs. actual |
| Deletes | Easy to forget | Computed from the diff |
| Review | Diff is a code diff of logic | Diff is a state diff of resources |

A typical imperative Fabric script reads like a procedure: create the bronze lakehouse, then create the silver lakehouse, then upload notebook A, then update its default lakehouse to silver, then create the pipeline, then set its parameters. Every step is your responsibility, and every step is a place where the script and the real workspace can disagree.

A declarative deployment reads like an inventory: these are my lakehouses, these are my notebooks (each attached to one of those lakehouses), this is the pipeline that runs them. The tool figures out the order and the operations.

---

## 2. The desired-state reconcile loop

Every `udpi30` command that touches a workspace runs through the same engine pipeline. The engine (`Udpi30.Core`) has five stages:

| Stage | Component | Responsibility |
|---|---|---|
| 1. Load | Loader (YamlFactory) | Parses `udp.yml` with YamlDotNet, merges `include:` files, substitutes variables for the chosen `--target`, and validates against the schema. Produces the desired state. |
| 2. Resolve | Resolver | Sorts resources into a dependency graph by topological sort (for example, environments before notebooks, lakehouses before semantic models, semantic models before reports and Data Agents). |
| 3. Plan | Planner | Compares the desired state against the actual state of the workspace (read live from the Fabric REST API, augmented by the state file). The output is a diff: Create, Update, Delete, or No-op per resource. |
| 4. Apply | Deployer | Executes the diff in dependency order through FabricClient. Unchanged resources are skipped via content hashing. |
| 5. Record | StateManager | Updates the per-target state file (`.udpi30/state-<target>.json`) so the next run starts from a known baseline. The state file also powers drift detection and idempotency. |

```text
udp.yml ──► load ──► resolve ──► plan ──► apply ──► state
   (desired)            (graph)    (diff)   (REST)    (actual)
                                     ▲                  │
                                     └──── drift ◄──────┘
```

Each workflow command stops at a different stage:

| Command | Stages run | Effect |
|---|---|---|
| `validate` | Load only | Schema and reference checks; no workspace contact |
| `plan` | Load → Resolve → Plan | Shows the diff without touching the workspace |
| `deploy` | All five | Reconciles the workspace and records state |
| `drift` | Load → Resolve → Plan | Reports any actual-vs-desired differences |
| `destroy` | Inverted diff | Everything in state, nothing in desired; deletes in reverse dependency order |

---

## 3. What the desired state contains

The desired state is the fully resolved deployment for one target. That means:

- `${var.*}` and `${env.*}` substitutions are applied.
- `targets.<name>.variables` overrides are merged on top of top-level `variables`.
- `include:` files are merged.
- `extends:` parents are merged.
- Defaults from the schema are filled in.

What it does not contain:

- Anything that exists in the workspace but is not in the deployment. Such items become candidates for deletion only if the resource type is managed by the deployment and the resource is recorded in state.
- Implementation details such as REST endpoints or item IDs. These are looked up at plan time.

This separation is why a deployment is portable across workspaces and capacities. The YAML is the *what*; the engine is the *how*.

---

## 4. Idempotency and content hashing

Running `udpi30 deploy` twice in a row against an unchanged deployment is a no-op. This is enforced two ways:

| Mechanism | Behavior |
|---|---|
| Resource-level diffing | Each resource's desired definition is compared to its recorded state. Equal definitions produce a No-op. |
| Content hashing | File-backed resources (notebooks, pipeline definitions, semantic model TMDL, report definitions) hash their payload. If the hash matches what was last deployed, the upload is skipped even if metadata changed elsewhere. |

This is what makes CI/CD pipelines safe to run on every merge: a deploy that finds nothing to do exits cleanly in seconds.

---

## 5. Drift detection

Because the tool always knows the desired state and can always read the actual state, drift is a plan with no apply. `udpi30 drift --target prod` answers one question: what has changed in the live workspace since the last deploy?

This catches:

- Manual edits in the Fabric portal.
- Out-of-band changes by other tools.
- Resources deleted in the portal that the deployment still expects.
- Configuration that was changed and not committed.

See [Drift Detection](../advanced/drift.md) for the full workflow and CI integration patterns.

---

## 6. Comparison with other tools

| Tool | Model | Relationship to udpi30 |
|---|---|---|
| Terraform | Desired-state plan/apply, dependency graph, state file | Same model. udpi30 applies it to Fabric items (notebooks, pipelines, semantic models, Data Agents) that Terraform providers do not cover. See [Migrate from Terraform](migrate-from-terraform.md). |
| Databricks Asset Bundles | Declarative YAML with targets and variables | Same idea, focused on Databricks. udpi30 applies it to Microsoft Fabric. |
| `fabric-cicd` | Imperative deployment helper | Promotes items between workspaces but does not describe a project, resolve dependencies, or detect drift. See [Migrate from fabric-cicd](migrate-from-fabric-cicd.md). |
| Fabric CLI (`fab`) | Imperative item-level operations (`export`, `import`, `create`) | A useful primitive layer; not a project model. |

---

## 7. When not to use the declarative model

Declarative tools are the wrong fit for a few cases:

| Case | Recommended approach |
|---|---|
| One-off exploration | If you are prototyping a single notebook in the portal, a deployment is overkill. Use [`udpi30 generate`](../cli/commands.md) later to capture it. |
| Highly dynamic resources | If the set of resources is computed at runtime (for example, one lakehouse per tenant, discovered from a database), a thin imperative wrapper that emits YAML and then calls `udpi30 deploy` is usually cleaner than expressing the loop in YAML. |
| Operations on data, not definitions | Running a notebook, refreshing a semantic model, or starting a pipeline is imperative by nature. Deployments define the resources; orchestration tools (Fabric pipelines, Airflow, ADF) run them. |

---

## 8. Next steps

- [`udp.yml` reference](udp-yml.md): every top-level key in the desired-state schema.
- [Development Workflows](development-workflows.md): `validate` → `plan` → `deploy` in practice.
- [Targets & Variables](targets-variables.md): how the desired state varies across dev, staging, and prod.
- [Drift Detection](../advanced/drift.md): comparing desired vs. actual on a schedule.
