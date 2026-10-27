<#
.SYNOPSIS
    Discovers every environment-specific value that udp.yml (udpi30) needs and writes them out
    as a report, JSON, a ready-to-paste udp.yml snippet, and an environment script.

.DESCRIPTION
    udpi30 needs the following values before `validate`, `plan`, or `deploy` will succeed. This script
    resolves them from your live Azure / Fabric / Entra / git context using the Azure CLI:

      udp.yml key / env var                  Source used by this script
      -------------------------------------  ---------------------------------------------------------
      deployment.name                        -DeploymentName, else the current folder name
      variables.capacity_id                  GET https://api.fabric.microsoft.com/v1/capacities
      workspace.capacity_id (per target)     (same; must be a GUID)
      targets.<t>.workspace.name             "<deployment>-<target>"
      targets.<t>.workspace.workspace_id     GET /v1/workspaces (only when a workspace already exists)
      variables.subscription_id              az account show / az account list
      azure.subscription / azure.location    az account, Fabric capacity region (or -Location)
      state.config.*                         storage account list / Fabric lakehouse list (-StateBackend)
      security.roles[].entra_group           az ad group list (-SecurityGroups "name:role")
      admin.tenant_settings graph_id         az ad group list (object id of each group)
      run_as / security service_principal    az ad sp list (-ServicePrincipalName); created + granted with -ProvisionServicePrincipal
      workspace.git_integration.*            git remote origin / current branch
      AZURE_TENANT_ID                        az account show
      AZURE_CLIENT_ID                        az ad sp list (-ServicePrincipalName)
      AZURE_CLIENT_SECRET                    NOT retrievable - reported as a manual step (-CreateClientSecret makes a new one)

    By default, nothing is written into udp.yml itself; review the generated snippet and merge what you need.
    -UdpFile merges discovered values into an existing file, preserving unrelated configuration.
    No secrets are read, printed, or saved. Azure changes require -ProvisionServicePrincipal.

.PARAMETER DeploymentName
    Value for deployment.name. Defaults to the sanitized current folder name.

.PARAMETER Targets
    Target names to generate. The first is marked default. Default: dev, staging, prod.

.PARAMETER SubscriptionId
    Azure subscription id or name. Must belong to TenantId when specified.
    Defaults to the current az account (interactive mode offers a menu).

.PARAMETER TenantId
    Entra tenant GUID. Limits subscription selection to this tenant and uses its selected subscription
    for Fabric and Entra discovery. Sign in first with az login --tenant <id>. The original Azure CLI
    subscription is restored afterward. Defaults to the current Azure CLI tenant.

.PARAMETER CapacityId
    Fabric capacity GUID to use for every target. Skips the capacity menu.

.PARAMETER CapacityName
    Fabric capacity display name to use for every target. Skips the capacity menu.

.PARAMETER CapacityIdByTarget
    Per-target capacity overrides, e.g. @{ prod = '1111...'; dev = '2222...' }.

.PARAMETER Location
    Azure region name (for example eastus). Defaults to the chosen capacity's region, else eastus.

.PARAMETER StateBackend
    local (default), azureblob, adls, or onelake. Non-local backends resolve their config values.

.PARAMETER StorageAccountName
    Storage account for azureblob/adls state. Skips the storage menu.

.PARAMETER StateWorkspaceName
    Workspace holding the state lakehouse for onelake state. Skips the workspace menu.

.PARAMETER StateLakehouseName
    Lakehouse for onelake state. Skips the lakehouse menu.

.PARAMETER SecurityGroups
    Entra groups for security.roles as "displayName:workspace_role" (role: admin|member|contributor|viewer;
    defaults to contributor). Example: "sg-data-engineering:contributor","sg-analysts:viewer".

.PARAMETER ServicePrincipalName
    Display name or app id of the CI/CD service principal (resolves AZURE_CLIENT_ID and run_as).
    With -ProvisionServicePrincipal the default is sp-udpi30.

.PARAMETER ProvisionServicePrincipal
    Creates the app registration + service principal when it does not exist, then grants it everything
    udpi30 needs (see -GrantScopes). Idempotent: existing apps, groups, and grants are detected and
    left alone. Honors -WhatIf / -Confirm. Needs rights to create apps and grant admin consent
    (Application Administrator or Cloud Application Administrator plus Privileged Role Administrator, or
    Global Administrator), Owner or User Access Administrator on the subscription, and Admin on the
    target workspaces. Anything it cannot grant is reported as a MANUAL step; it never fails silently.

.PARAMETER GrantScopes
    What to grant when provisioning (default: all).
      Graph            Microsoft Graph application permissions (Group.ReadWrite.All, GroupMember.ReadWrite.All,
                       Application.ReadWrite.All, User.Read.All, Directory.Read.All) with admin consent.
      Azure            -AzureRoles at the subscription scope (Azure and Bicep resource types).
      FabricWorkspace  -WorkspaceRole on every target workspace that already exists.
      FabricTenant     Membership in -FabricAccessGroupName, the group your Fabric tenant settings must enable
                       for "Service principals can call Fabric public APIs", "...create workspaces,
                       connections, and deployment pipelines", and the admin-API settings.
      StateStorage     Storage Blob Data Contributor on the -StateBackend azureblob/adls account.
      KeyVault         Key Vault Secrets User on each vault in -KeyVaultNames (for ${keyvault.*.*}).

.PARAMETER AzureRoles
    Subscription-scope Azure roles granted by the Azure scope. Default: Contributor, Resource Policy
    Contributor (policy assignments), Security Admin (Defender for Cloud plans). Add 'User Access
    Administrator' if you deploy resource locks or role assignments through azure_deployments.

.PARAMETER WorkspaceRole
    Workspace role granted by FabricWorkspace: admin (default; needed for security roles), member,
    contributor, or viewer.

.PARAMETER FabricAccessGroupName
    Entra security group the service principal joins for FabricTenant. Created (security-enabled) when
    missing. Default: sg-fabric-automation (the group examples/00-default scopes its API settings to).

.PARAMETER KeyVaultNames
    Key Vault names the service principal may read secrets from (RBAC-enabled vaults).

.PARAMETER CreateClientSecret
    Creates a client secret for the service principal (180 days). The value is never printed or written
    to the report. It is stored in -SecretKeyVaultName when given, and always set as AZURE_CLIENT_SECRET
    in the current process only.

.PARAMETER SecretKeyVaultName
    Key Vault that receives the secret as 'udpi30-client-secret' (you need set-secret rights).

.PARAMETER IncludeGitIntegration
    Emit workspace.git_integration in the YAML snippet (values are always reported).

.PARAMETER OutputDirectory
    Where to write results. Default: <user profile>\PIQ_Output\udp-values

.PARAMETER UdpFile
    Optional path to an existing udp.yml to update. Merges generated values without replacing resources,
    unrelated settings, existing deployment.version, or the existing default target. Security roles merge
    by name. Requires the powershell-yaml module only when the file exists. Honors -WhatIf / -Confirm.
    Creates a uniquely named .bak backup; comments and formatting may change during YAML serialization.
    Missing files are reported as a warning, not created; normal output reports are still generated.

.PARAMETER NonInteractive
    Never prompt; pick the first match for any unresolved selection.

.PARAMETER AzTimeoutSeconds
    Per-call timeout for Azure CLI queries (default 60). A timeout is reported as a note instead of hanging.

.EXAMPLE
    .\scripts\Get-UdpYmlValues.ps1

.EXAMPLE
    .\scripts\Get-UdpYmlValues.ps1 -DeploymentName sales-analytics -CapacityName fabric-f64 `
        -SecurityGroups 'sg-platform:admin','sg-analysts:viewer' -ServicePrincipalName sp-udpi30 -NonInteractive

.EXAMPLE
    .\scripts\Get-UdpYmlValues.ps1 -StateBackend adls -StorageAccountName mydatalake

.EXAMPLE
    .\scripts\Get-UdpYmlValues.ps1 -UdpFile .\examples\01-minimal\udp.yml -Targets dev -NonInteractive

.EXAMPLE
    az login --tenant '11111111-1111-1111-1111-111111111111'
    .\scripts\Get-UdpYmlValues.ps1 -TenantId '11111111-1111-1111-1111-111111111111' -NonInteractive

.EXAMPLE
    # Preview everything the provisioning would create or grant (no changes made)
    .\scripts\Get-UdpYmlValues.ps1 -ProvisionServicePrincipal -WhatIf -NonInteractive

.EXAMPLE
    # Create sp-udpi30 if missing, grant it all udpi30 permissions, and store a new secret in Key Vault
    .\scripts\Get-UdpYmlValues.ps1 -ProvisionServicePrincipal -CreateClientSecret -SecretKeyVaultName kv-governance `
        -KeyVaultNames kv-governance -NonInteractive

.EXAMPLE
    # PASSING THE CLIENT SECRET - prompt for it (nothing is echoed or stored in history), then run udpi30
    $env:AZURE_TENANT_ID     = '<tenant id>'
    $env:AZURE_CLIENT_ID     = '<sp-udpi30 appId>'
    $env:AZURE_CLIENT_SECRET = [System.Net.NetworkCredential]::new('', (Read-Host 'Client secret' -AsSecureString)).Password
    udpi30 validate; udpi30 plan -t dev

.EXAMPLE
    # PASSING THE CLIENT SECRET - read it from Key Vault (stored earlier with -SecretKeyVaultName)
    $env:AZURE_TENANT_ID     = (az account show --query tenantId -o tsv)
    $env:AZURE_CLIENT_ID     = (az ad sp list --display-name sp-udpi30 --query '[0].appId' -o tsv)
    $env:AZURE_CLIENT_SECRET = (az keyvault secret show --vault-name kv-governance --name udpi30-client-secret --query value -o tsv)
    udpi30 deploy -t dev

.EXAMPLE
    # PASSING THE CLIENT SECRET - create one and use it in the SAME session
    # Run the script directly (not via 'pwsh -File', which discards the variable when it exits).
    .\scripts\Get-UdpYmlValues.ps1 -ProvisionServicePrincipal -CreateClientSecret -NonInteractive
    $env:AZURE_CLIENT_ID = (az ad sp list --display-name sp-udpi30 --query '[0].appId' -o tsv)
    $env:AZURE_TENANT_ID = (az account show --query tenantId -o tsv)
    udpi30 plan -t dev       # AZURE_CLIENT_SECRET was set by the script (this process only)

.EXAMPLE
    # PASSING THE CLIENT SECRET - CI/CD: map the pipeline secret to the environment variable
    #   GitHub Actions:  env: { AZURE_CLIENT_SECRET: '${{ secrets.AZURE_CLIENT_SECRET }}' }
    #   Azure DevOps:    env: { AZURE_CLIENT_SECRET: $(AZURE_CLIENT_SECRET) }   # secret variables are not exposed unless mapped
    #   then:            udpi30 deploy -t prod

.NOTES
    Passing the client secret
      udpi30 reads the secret from the AZURE_CLIENT_SECRET environment variable (with AZURE_TENANT_ID and
      AZURE_CLIENT_ID). This script has NO secret parameter on purpose: a value passed as an argument lands in
      shell history, process listings, and CI logs. Use one of the patterns in the examples above instead.
      Rules of thumb: never put the secret in udp.yml; never write it to a file; set it only in the process that
      runs udpi30; prefer Key Vault (-SecretKeyVaultName) or managed identity / federated credentials in CI.
      The script itself authenticates with your current 'az login' - it does not need the secret.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$DeploymentName,
    [string[]]$Targets = @('dev', 'staging', 'prod'),
    [string]$SubscriptionId,
    [ValidatePattern('^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$')]
    [string]$CapacityId,
    [string]$CapacityName,
    [hashtable]$CapacityIdByTarget = @{},
    [string]$Location,
    [ValidateSet('local', 'azureblob', 'adls', 'onelake')]
    [string]$StateBackend = 'local',
    [string]$StorageAccountName,
    [string]$StateWorkspaceName,
    [string]$StateLakehouseName,
    [string[]]$SecurityGroups = @(),
    [string]$ServicePrincipalName,
    [switch]$ProvisionServicePrincipal,
    [ValidateSet('Graph', 'Azure', 'FabricWorkspace', 'FabricTenant', 'StateStorage', 'KeyVault')]
    [string[]]$GrantScopes = @('Graph', 'Azure', 'FabricWorkspace', 'FabricTenant', 'StateStorage', 'KeyVault'),
    [string[]]$AzureRoles = @('Contributor', 'Resource Policy Contributor', 'Security Admin'),
    [ValidateSet('admin', 'member', 'contributor', 'viewer')]
    [string]$WorkspaceRole = 'admin',
    [string]$FabricAccessGroupName = 'sg-fabric-automation',
    [string[]]$KeyVaultNames = @(),
    [switch]$CreateClientSecret,
    [string]$SecretKeyVaultName,
    [switch]$IncludeGitIntegration,
    [string]$OutputDirectory = (Join-Path $env:USERPROFILE 'PIQ_Output\udp-values'),
    [switch]$NonInteractive,
    [ValidateRange(10, 600)]
    [int]$AzTimeoutSeconds = 60,
    [ValidatePattern('^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$')]
    [string]$TenantId,
    [ValidateNotNullOrEmpty()]
    [string]$UdpFile
)

$ErrorActionPreference = 'Stop'
$guidPattern = '^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$'
$fabricResource = 'https://api.fabric.microsoft.com'
$validRoles = 'admin', 'member', 'contributor', 'viewer'

$rows = [System.Collections.Generic.List[object]]::new()
$notes = [System.Collections.Generic.List[string]]::new()

function Add-Value {
    param([string]$Key, $Value, [string]$Where, [string]$Source, [string]$Status = 'Found')
    $rows.Add([pscustomobject]@{
            Key    = $Key
            Value  = $Value
            Where  = $Where
            Source = $Source
            Status = $Status
        })
}

function Invoke-AzJson {
    param([Parameter(Mandatory)][string[]]$Arguments)
    # Run in a job so a hidden interactive prompt (re-auth, MFA) cannot hang the script.
    $job = Start-Job -ScriptBlock {
        param($azArgs)
        $out = & az @azArgs --only-show-errors -o json 2>$null
        [pscustomobject]@{ Exit = $LASTEXITCODE; Text = ($out -join "`n") }
    } -ArgumentList (, $Arguments)
    if (-not (Wait-Job $job -Timeout $AzTimeoutSeconds)) {
        Stop-Job $job -WhatIf:$false
        Remove-Job $job -Force -WhatIf:$false
        $notes.Add("'az $($Arguments[0..([Math]::Min(2, $Arguments.Count - 1))] -join ' ')' timed out after ${AzTimeoutSeconds}s (likely waiting for sign-in). Run 'az login' again and retry.")
        return $null
    }
    $res = Receive-Job $job
    Remove-Job $job -Force -WhatIf:$false
    if ($res.Exit -ne 0) { return $null }
    $text = "$($res.Text)".Trim()
    if (-not $text) { return $null }
    try { return $text | ConvertFrom-Json } catch { return $null }
}

function Invoke-AzCommand {
    # Runs az and returns Exit/Json/Text/Error so mutations can report why they failed.
    param([Parameter(Mandatory)][string[]]$Arguments, [switch]$NoJson)
    $job = Start-Job -ScriptBlock {
        param($azArgs, $wantJson)
        $err = [System.IO.Path]::GetTempFileName()
        try {
            $azArgs = if ($wantJson) { $azArgs + @('-o', 'json') } else { $azArgs + @('-o', 'none') }
            $out = & az @azArgs --only-show-errors 2>$err
            [pscustomobject]@{ Exit = $LASTEXITCODE; Text = ($out -join "`n"); Error = ((Get-Content $err -Raw -ErrorAction SilentlyContinue) + '').Trim() }
        } finally { Remove-Item $err -Force -ErrorAction SilentlyContinue -WhatIf:$false }
    } -ArgumentList (, $Arguments), (-not $NoJson)
    if (-not (Wait-Job $job -Timeout $AzTimeoutSeconds)) {
        Stop-Job $job -WhatIf:$false
        Remove-Job $job -Force -WhatIf:$false
        return [pscustomobject]@{ Ok = $false; Json = $null; Text = ''; Error = "timed out after ${AzTimeoutSeconds}s (likely waiting for sign-in)" }
    }
    $res = Receive-Job $job
    Remove-Job $job -Force -WhatIf:$false
    $json = $null
    if ($res.Exit -eq 0 -and -not $NoJson -and "$($res.Text)".Trim()) {
        try { $json = "$($res.Text)" | ConvertFrom-Json } catch { $json = $null }
    }
    [pscustomobject]@{ Ok = ($res.Exit -eq 0); Json = $json; Text = "$($res.Text)"; Error = "$($res.Error)" }
}

function Get-FabricList {
    param([Parameter(Mandatory)][string]$Path)
    $items = [System.Collections.Generic.List[object]]::new()
    $token = $null
    do {
        $url = "$fabricResource/v1$Path"
        if ($token) {
            $sep = if ($url.Contains('?')) { '&' } else { '?' }
            $url = "$url${sep}continuationToken=$([uri]::EscapeDataString($token))"
        }
        $resp = Invoke-AzJson @('rest', '--method', 'get', '--url', $url, '--resource', $fabricResource)
        if (-not $resp) { return $null }
        foreach ($v in @($resp.value)) { if ($null -ne $v) { $items.Add($v) } }
        $token = $resp.continuationToken
    } while ($token)
    return , $items.ToArray()
}

function Select-FromList {
    param([object[]]$Items, [string]$Prompt, [scriptblock]$Label)
    $Items = @($Items | Where-Object { $null -ne $_ })
    if ($Items.Count -eq 0) { return $null }
    if ($Items.Count -eq 1 -or $NonInteractive) { return $Items[0] }
    Write-Host ''
    Write-Host $Prompt -ForegroundColor Cyan
    for ($i = 0; $i -lt $Items.Count; $i++) {
        Write-Host ("  {0,2}. {1}" -f ($i + 1), (& $Label $Items[$i]))
    }
    while ($true) {
        $answer = Read-Host "Select 1-$($Items.Count) [1]"
        if ([string]::IsNullOrWhiteSpace($answer)) { return $Items[0] }
        $n = 0
        if ([int]::TryParse($answer, [ref]$n) -and $n -ge 1 -and $n -le $Items.Count) { return $Items[$n - 1] }
        Write-Host 'Invalid selection.' -ForegroundColor Yellow
    }
}

function ConvertTo-YamlString {
    param([string]$Text)
    '"' + ($Text -replace '\\', '\\' -replace '"', '\"') + '"'
}

function Merge-UdpValues {
    param(
        [System.Collections.IDictionary]$Existing,
        [System.Collections.IDictionary]$Updates,
        [string]$Path = ''
    )
    foreach ($key in $Updates.Keys) {
        $value = $Updates[$key]
        $keyPath = if ($Path) { "$Path.$key" } else { [string]$key }
        if ($value -is [System.Collections.IDictionary]) {
            if ($Existing.Contains($key) -and $null -ne $Existing[$key] -and
                $Existing[$key] -isnot [System.Collections.IDictionary]) {
                if ($Path -eq 'variables' -and $value.Contains('default')) {
                    $Existing[$key] = $value['default']
                    continue
                }
                throw "Cannot merge discovered values into '$keyPath': expected a YAML mapping."
            }
            if (-not $Existing.Contains($key) -or $null -eq $Existing[$key]) {
                $Existing[$key] = [ordered]@{}
            }
            Merge-UdpValues -Existing $Existing[$key] -Updates $value -Path $keyPath
        } elseif ($keyPath -eq 'security.roles') {
            if ($null -ne $Existing[$key] -and $Existing[$key] -isnot [System.Collections.IList]) {
                throw "Cannot merge discovered values into '$keyPath': expected a YAML sequence."
            }
            $roles = [System.Collections.Generic.List[object]]::new()
            foreach ($role in $Existing[$key]) { $roles.Add($role) }
            foreach ($role in $value) {
                $match = @($roles | Where-Object { $_ -is [System.Collections.IDictionary] -and $_['name'] -eq $role['name'] })
                if ($match.Count -gt 1) { throw "Cannot merge security role '$($role['name'])': duplicate existing names." }
                if ($match.Count -eq 1) { Merge-UdpValues -Existing $match[0] -Updates $role -Path $keyPath }
                else { $roles.Add($role) }
            }
            $Existing[$key] = @($roles)
        } else {
            $Existing[$key] = $value
        }
    }
}

# ---------------------------------------------------------------------------------------------
# 0. Prerequisites
# ---------------------------------------------------------------------------------------------
$udpPath = $null
$udpOriginal = $null
$udpDocument = $null
$udpUpdate = [ordered]@{ path = $UdpFile; status = 'NotRequested'; backup = $null }
if ($UdpFile) {
    $udpPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($UdpFile)
    if (-not (Test-Path -LiteralPath $udpPath)) {
        $udpUpdate.status = 'NotFound'
        Write-Warning "UdpFile '$UdpFile' does not exist; no file will be created. Discovery reports will still be generated."
    } else {
        if (-not (Test-Path -LiteralPath $udpPath -PathType Leaf)) {
            throw "UdpFile '$UdpFile' must be a file, not a directory."
        }
        foreach ($reportName in 'udp-values.json', 'udp-values.yml', 'udp-env.ps1') {
            $reportPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path $OutputDirectory $reportName))
            if ($udpPath -eq $reportPath) { throw "UdpFile must not be an output report path: '$reportPath'." }
        }
        if (-not (Get-Module -ListAvailable powershell-yaml)) {
            throw 'Updating -UdpFile requires powershell-yaml. Run: Install-Module powershell-yaml -Scope CurrentUser'
        }
        Import-Module powershell-yaml -ErrorAction Stop
        $udpOriginal = [System.IO.File]::ReadAllText($udpPath)
        $documents = @(ConvertFrom-Yaml -Yaml $udpOriginal -Ordered -AllDocuments -ErrorAction Stop)
        if ($documents.Count -ne 1 -or $documents[0] -isnot [System.Collections.IDictionary]) {
            throw "UdpFile '$UdpFile' must contain exactly one YAML mapping document."
        }
        $udpDocument = $documents[0]
        $udpUpdate.path = $udpPath
        $udpUpdate.status = 'Pending'
    }
}
if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI (az) was not found on PATH. Install it from https://aka.ms/installazurecli and run "az login".'
}
$account = Invoke-AzJson @('account', 'show')
if (-not $account) {
    throw 'Azure CLI is not signed in. Run "az login" (add --tenant <id> if needed) and re-run this script.'
}

# ---------------------------------------------------------------------------------------------
# 1. Deployment identity
# ---------------------------------------------------------------------------------------------
if (-not $DeploymentName) {
    $DeploymentName = (Split-Path -Leaf (Get-Location).Path).ToLowerInvariant() -replace '[^a-z0-9]+', '-' -replace '^-|-$', ''
    $nameSource = 'Current folder name (override with -DeploymentName)'
} else {
    $nameSource = '-DeploymentName'
}
Add-Value 'deployment.name' $DeploymentName 'deployment.name' $nameSource
Add-Value 'deployment.version' '0.1.0' 'deployment.version' 'Schema default (optional)' 'Optional'

# ---------------------------------------------------------------------------------------------
# 2. Tenant + subscription
# ---------------------------------------------------------------------------------------------
$requestedTenantId = if ($TenantId) { $TenantId } else { $account.tenantId }
if ($SubscriptionId) {
    $subscription = Invoke-AzJson @('account', 'show', '--subscription', $SubscriptionId)
    if (-not $subscription) { throw "Subscription '$SubscriptionId' was not found for the signed-in account." }
    if ($TenantId -and $subscription.tenantId -ne $TenantId) {
        throw "Subscription '$SubscriptionId' belongs to tenant '$($subscription.tenantId)', not requested tenant '$TenantId'."
    }
} else {
    $subs = @(Invoke-AzJson @('account', 'list', '--all') | Where-Object { $_.state -eq 'Enabled' -and $_.tenantId -eq $requestedTenantId })
    $ordered = @($subs | Where-Object { $_.id -eq $account.id }) + @($subs | Where-Object { $_.id -ne $account.id })
    $subscription = Select-FromList $ordered 'Azure subscription for azure.subscription:' { param($s) "$($s.name)  [$($s.id)]" }
    if (-not $subscription) {
        if ($TenantId) {
            throw "No enabled subscription found for tenant '$TenantId'. Run 'az login --tenant $TenantId' and retry."
        }
        $subscription = $account
    }
}
$restoreSubscription = $TenantId -and $subscription.id -ne $account.id
try {
if ($restoreSubscription) {
    # Fabric REST and Graph commands use the active Azure CLI tenant.
    $null = Invoke-AzJson @('account', 'set', '--subscription', $subscription.id)
    $activeAccount = Invoke-AzJson @('account', 'show')
    if (-not $activeAccount -or $activeAccount.id -ne $subscription.id -or $activeAccount.tenantId -ne $requestedTenantId) {
        throw "Could not select subscription '$($subscription.id)' in tenant '$requestedTenantId'. Run 'az login --tenant $requestedTenantId' and retry."
    }
}
$resolvedTenantId = $subscription.tenantId
Add-Value 'AZURE_TENANT_ID' $resolvedTenantId 'CI/CD secret / environment variable' 'az account show'
Add-Value 'subscription_id' $subscription.id 'variables.subscription_id -> azure.subscription' "az account ($($subscription.name))"
Add-Value 'signed_in_user' $account.user.name 'Informational (identity running this script)' 'az account show' 'Info'

# ---------------------------------------------------------------------------------------------
# 3. Fabric capacities
# ---------------------------------------------------------------------------------------------
$capacities = Get-FabricList '/capacities'
$capacityJson = @()
if ($null -eq $capacities) {
    $notes.Add('Could not list Fabric capacities (az rest failed). Confirm your account can call the Fabric API, or pass -CapacityId.')
    $capacities = @()
} else {
    $capacityJson = @($capacities | ForEach-Object { [ordered]@{ id = $_.id; displayName = $_.displayName; sku = $_.sku; region = $_.region; state = $_.state } })
}

$capacityMap = @{}
function Resolve-Capacity {
    param([string]$Id, [string]$Name)
    if ($Id) {
        $hit = @($capacities | Where-Object { $_.id -eq $Id }) | Select-Object -First 1
        if ($hit) { return $hit }
        return [pscustomobject]@{ id = $Id; displayName = '(not in capacity list)'; sku = ''; region = '' }
    }
    if ($Name) {
        $hit = @($capacities | Where-Object { $_.displayName -eq $Name }) | Select-Object -First 1
        if (-not $hit) { throw "Capacity named '$Name' was not found. Available: $(($capacities.displayName) -join ', ')" }
        return $hit
    }
    return $null
}

$defaultCapacity = Resolve-Capacity -Id $CapacityId -Name $CapacityName
if (-not $defaultCapacity) {
    $active = @($capacities | Where-Object { $_.state -eq 'Active' })
    $pool = if ($active.Count -gt 0) { $active } else { $capacities }
    $defaultCapacity = Select-FromList $pool 'Fabric capacity for variables.capacity_id:' { param($c) "$($c.displayName)  [$($c.sku), $($c.region), $($c.state)]  $($c.id)" }
}
if ($defaultCapacity) {
    Add-Value 'capacity_id' $defaultCapacity.id 'variables.capacity_id -> workspace.capacity_id' "Fabric capacities ($($defaultCapacity.displayName), $($defaultCapacity.sku))"
} else {
    Add-Value 'capacity_id' $null 'variables.capacity_id -> workspace.capacity_id' 'No capacity found - create one in the Azure portal or pass -CapacityId' 'MISSING'
}

foreach ($t in $Targets) {
    $override = $CapacityIdByTarget[$t]
    if ($override) {
        if ($override -notmatch $guidPattern) { throw "CapacityIdByTarget['$t'] is not a valid GUID." }
        $capacityMap[$t] = Resolve-Capacity -Id $override
    } else {
        $capacityMap[$t] = $defaultCapacity
    }
}

# ---------------------------------------------------------------------------------------------
# 4. Azure location
# ---------------------------------------------------------------------------------------------
$locationSource = '-Location'
if (-not $Location) {
    $Location = 'eastus'
    $locationSource = 'Fallback default'
    if ($defaultCapacity -and $defaultCapacity.region) {
        $locations = Invoke-AzJson @('account', 'list-locations')
        $match = @($locations | Where-Object { $_.displayName -eq $defaultCapacity.region -or $_.name -eq $defaultCapacity.region }) | Select-Object -First 1
        if ($match) { $Location = $match.name; $locationSource = "Region of capacity '$($defaultCapacity.displayName)'" }
    }
}
Add-Value 'azure.location' $Location 'azure.location (and per-resource location)' $locationSource

# ---------------------------------------------------------------------------------------------
# 5. Existing Fabric workspaces (avoids duplicate creation; gives workspace_id)
# ---------------------------------------------------------------------------------------------
$workspaces = Get-FabricList '/workspaces'
if ($null -eq $workspaces) {
    $notes.Add('Could not list Fabric workspaces; workspace_id values were not resolved.')
    $workspaces = @()
}

$targetInfo = [ordered]@{}
foreach ($t in $Targets) {
    $wsName = "$DeploymentName-$t"
    $existing = @($workspaces | Where-Object { $_.displayName -eq $wsName }) | Select-Object -First 1
    $cap = $capacityMap[$t]
    $targetInfo[$t] = [ordered]@{
        workspace_name = $wsName
        workspace_id   = if ($existing) { $existing.id } else { $null }
        capacity_id    = if ($cap) { $cap.id } else { $null }
    }
    Add-Value "targets.$t.workspace.name" $wsName "targets.$t.workspace.name" 'Convention: <deployment>-<target> (change freely)'
    if ($existing) {
        Add-Value "targets.$t.workspace.workspace_id" $existing.id "targets.$t.workspace.workspace_id" 'Existing Fabric workspace' 'Found'
        if ($existing.capacityId -and $cap -and $existing.capacityId -ne $cap.id) {
            $notes.Add("Workspace '$wsName' is currently on capacity $($existing.capacityId), not the selected $($cap.id).")
        }
    } else {
        Add-Value "targets.$t.workspace.workspace_id" $null "targets.$t.workspace.workspace_id" 'Workspace does not exist yet - deploy creates it (leave unset)' 'Optional'
    }
}

# ---------------------------------------------------------------------------------------------
# 6. State backend
# ---------------------------------------------------------------------------------------------
$stateConfig = [ordered]@{}
$acct = $null
switch ($StateBackend) {
    { $_ -in 'azureblob', 'adls' } {
        $accounts = @(Invoke-AzJson @('storage', 'account', 'list', '--subscription', $subscription.id) | Where-Object { $null -ne $_ })
        if ($StateBackend -eq 'adls') { $accounts = @($accounts | Where-Object { $_.isHnsEnabled }) }
        $acct = $null
        if ($StorageAccountName) {
            $acct = @($accounts | Where-Object { $_.name -eq $StorageAccountName }) | Select-Object -First 1
            if (-not $acct) { throw "Storage account '$StorageAccountName' not found in subscription $($subscription.id) (adls requires hierarchical namespace)." }
        } else {
            $acct = Select-FromList $accounts "Storage account for the $StateBackend state backend:" { param($a) "$($a.name)  [$($a.resourceGroup), $($a.location)]" }
        }
        if ($acct) {
            $stateConfig['account_name'] = $acct.name
            if ($StateBackend -eq 'azureblob') { $stateConfig['container_name'] = 'udpi30-state' } else { $stateConfig['filesystem'] = 'udpi30-state' }
            Add-Value 'state.config.account_name' $acct.name 'state.config.account_name (or env AZURE_STORAGE_ACCOUNT_NAME)' 'az storage account list'
            $notes.Add("Grant the deploying identity 'Storage Blob Data Contributor' on $($acct.name); the container/filesystem 'udpi30-state' must exist. Omit account_key so DefaultAzureCredential is used.")
        } else {
            Add-Value 'state.config.account_name' $null 'state.config.account_name' 'No suitable storage account found' 'MISSING'
        }
    }
    'onelake' {
        $ws = $null
        if ($StateWorkspaceName) {
            $ws = @($workspaces | Where-Object { $_.displayName -eq $StateWorkspaceName }) | Select-Object -First 1
            if (-not $ws) { throw "Workspace '$StateWorkspaceName' not found." }
        } else {
            $ws = Select-FromList @($workspaces) 'Workspace that holds the state lakehouse:' { param($w) "$($w.displayName)  [$($w.id)]" }
        }
        if ($ws) {
            $lakehouses = Get-FabricList "/workspaces/$($ws.id)/lakehouses"
            $lh = $null
            if ($StateLakehouseName) {
                $lh = @($lakehouses | Where-Object { $_.displayName -eq $StateLakehouseName }) | Select-Object -First 1
            } else {
                $lh = Select-FromList @($lakehouses) 'Lakehouse for state files:' { param($l) "$($l.displayName)  [$($l.id)]" }
            }
            if ($lh) {
                $stateConfig['workspace_id'] = $ws.id
                $stateConfig['lakehouse_id'] = $lh.id
                $stateConfig['path'] = '.udpi30-state'
                Add-Value 'state.config.workspace_id' $ws.id 'state.config.workspace_id' "Fabric workspace '$($ws.displayName)'"
                Add-Value 'state.config.lakehouse_id' $lh.id 'state.config.lakehouse_id' "Lakehouse '$($lh.displayName)'"
            } else {
                Add-Value 'state.config.lakehouse_id' $null 'state.config.lakehouse_id' 'No lakehouse found in that workspace' 'MISSING'
            }
        } else {
            Add-Value 'state.config.workspace_id' $null 'state.config.workspace_id' 'No workspace found' 'MISSING'
        }
    }
    default {
        Add-Value 'state.backend' 'local' 'state.backend' 'Default (JSON under .udpi30/)' 'Optional'
    }
}

# ---------------------------------------------------------------------------------------------
# 7. Entra groups for security.roles
# ---------------------------------------------------------------------------------------------
$groupResults = [System.Collections.Generic.List[object]]::new()
foreach ($spec in $SecurityGroups) {
    $name, $role = $spec -split ':', 2
    if (-not $role) { $role = 'contributor' }
    $role = $role.ToLowerInvariant()
    if ($role -notin $validRoles) { throw "Invalid workspace_role '$role' in '$spec'. Use: $($validRoles -join ', ')." }
    $safe = $name -replace "'", "''"
    $found = @(Invoke-AzJson @('ad', 'group', 'list', '--filter', "displayName eq '$safe'", '--query', '[].{id:id,displayName:displayName}') | Where-Object { $null -ne $_ })
    if ($found.Count -gt 0) {
        $groupResults.Add([pscustomobject]@{ name = $name; role = $role; id = $found[0].id })
        Add-Value "security.group.$name" $found[0].id "security.roles[].entra_group = $name ($role); admin tenant_settings graph_id" 'az ad group list'
    } else {
        $groupResults.Add([pscustomobject]@{ name = $name; role = $role; id = $null })
        Add-Value "security.group.$name" $null "security.roles[].entra_group = $name" 'Group not found in Entra' 'MISSING'
    }
}

# ---------------------------------------------------------------------------------------------
# 8. Service principal
# ---------------------------------------------------------------------------------------------
$sp = $null
$provisioning = [System.Collections.Generic.List[object]]::new()
$createdSecret = $null

function Add-Step {
    param([string]$Step, [string]$Status, [string]$Detail = '')
    $provisioning.Add([pscustomobject]@{ Step = $Step; Status = $Status; Detail = $Detail })
    $rowStatus = switch ($Status) { 'FAILED' { 'MISSING' } 'MANUAL' { 'MANUAL' } default { 'Info' } }
    Add-Value "provision.$Step" $Status $Detail $Detail $rowStatus
}

function Find-ServicePrincipal {
    param([string]$Name)
    if ($Name -match $guidPattern) { return Invoke-AzJson @('ad', 'sp', 'show', '--id', $Name) }
    $safe = $Name -replace "'", "''"
    @(Invoke-AzJson @('ad', 'sp', 'list', '--filter', "displayName eq '$safe'") | Where-Object { $null -ne $_ }) | Select-Object -First 1
}

function Invoke-AzRestWrite {
    # Bodies go through a temp file: az.cmd mangles inline JSON quoting on Windows.
    param([string]$Url, $Body, [string]$Method = 'post', [string]$Resource)
    $tmp = [System.IO.Path]::GetTempFileName()
    try {
        [System.IO.File]::WriteAllText($tmp, ($Body | ConvertTo-Json -Depth 6 -Compress), [System.Text.UTF8Encoding]::new($false))
        $restArgs = @('rest', '--method', $Method, '--url', $Url, '--headers', 'Content-Type=application/json', '--body', "@$tmp")
        if ($Resource) { $restArgs += @('--resource', $Resource) }
        Invoke-AzCommand $restArgs -NoJson
    } finally { Remove-Item $tmp -Force -ErrorAction SilentlyContinue -WhatIf:$false }
}

function Invoke-WithRetry {
    # New principals take a while to replicate; retry only errors that mean "not visible yet".
    param([scriptblock]$Action, [int]$Attempts = 5, [int]$DelaySeconds = 10)
    for ($i = 1; $i -le $Attempts; $i++) {
        $r = & $Action
        if ($r.Ok -or $r.Error -notmatch 'PrincipalNotFound|does not exist|not found|Cannot find|ResourceNotFound|Request_ResourceNotFound') { return $r }
        if ($i -lt $Attempts) { Start-Sleep -Seconds $DelaySeconds }
    }
    $r
}

function Grant-AzureRole {
    param([string]$Step, [string]$Role, [string]$Scope)
    $have = Invoke-AzCommand @('role', 'assignment', 'list', '--assignee', $sp.id, '--scope', $Scope, '--role', $Role, '--query', '[].id')
    if ($have.Ok -and @($have.Json).Count -gt 0) { Add-Step $Step 'Exists' "$Role on $Scope"; return }
    if (-not $PSCmdlet.ShouldProcess($Scope, "Grant '$Role' to $($sp.displayName)")) { Add-Step $Step 'WhatIf' "Would grant $Role on $Scope"; return }
    $r = Invoke-WithRetry { Invoke-AzCommand @('role', 'assignment', 'create', '--assignee-object-id', $sp.id, '--assignee-principal-type', 'ServicePrincipal', '--role', $Role, '--scope', $Scope) -NoJson }
    if ($r.Ok) { Add-Step $Step 'Granted' "$Role on $Scope" } else { Add-Step $Step 'FAILED' "$Role on ${Scope}: $($r.Error)" }
}

if ($ProvisionServicePrincipal) {
    if (-not $ServicePrincipalName) { $ServicePrincipalName = 'sp-udpi30' }
    if ($ServicePrincipalName -match $guidPattern) { throw '-ProvisionServicePrincipal needs a display name for -ServicePrincipalName, not a GUID.' }

    # 8a. App registration + service principal (create only when missing)
    $safeName = $ServicePrincipalName -replace "'", "''"
    $apps = @(Invoke-AzJson @('ad', 'app', 'list', '--filter', "displayName eq '$safeName'") | Where-Object { $null -ne $_ })
    if ($apps.Count -gt 1) {
        throw "Found $($apps.Count) app registrations named '$ServicePrincipalName'. Remove the duplicates (or pass -ServicePrincipalName <appId>-owning name) and retry."
    }
    $app = $apps | Select-Object -First 1
    if ($app) {
        Add-Step 'app-registration' 'Exists' "$ServicePrincipalName ($($app.appId))"
    } elseif ($PSCmdlet.ShouldProcess("tenant $resolvedTenantId", "Create app registration '$ServicePrincipalName'")) {
        $r = Invoke-AzCommand @('ad', 'app', 'create', '--display-name', $ServicePrincipalName, '--sign-in-audience', 'AzureADMyOrg')
        if (-not $r.Ok) { throw "Could not create app registration '$ServicePrincipalName': $($r.Error)" }
        $app = $r.Json
        Add-Step 'app-registration' 'Created' "$ServicePrincipalName ($($app.appId))"
    } else {
        Add-Step 'app-registration' 'WhatIf' "Would create app registration '$ServicePrincipalName'"
    }

    if ($app) {
        $sp = Find-ServicePrincipal $app.appId
        if ($sp) {
            Add-Step 'service-principal' 'Exists' "object id $($sp.id)"
        } elseif ($PSCmdlet.ShouldProcess($app.appId, "Create service principal for '$ServicePrincipalName'")) {
            $r = Invoke-AzCommand @('ad', 'sp', 'create', '--id', $app.appId)
            if (-not $r.Ok -and $r.Error -notmatch 'already exists') { throw "Could not create the service principal: $($r.Error)" }
            for ($i = 0; $i -lt 6 -and -not $sp; $i++) {
                $sp = Find-ServicePrincipal $app.appId
                if (-not $sp) { Start-Sleep -Seconds 5 }
            }
            if (-not $sp) { throw "Service principal for app $($app.appId) was created but is not visible yet. Re-run in a minute." }
            Add-Step 'service-principal' 'Created' "object id $($sp.id)"
        } else {
            Add-Step 'service-principal' 'WhatIf' 'Would create the service principal'
        }
    } else {
        Add-Step 'service-principal' 'WhatIf' 'Would create the service principal'
    }

    if (-not $sp) {
        Add-Step 'grants' 'WhatIf' "Would grant: $($GrantScopes -join ', ')"
    } else {
        # 8b. Microsoft Graph application permissions (Entra groups/apps, principal lookups)
        if ('Graph' -in $GrantScopes) {
            $graphAppId = '00000003-0000-0000-c000-000000000000'
            $graphSp = Invoke-AzJson @('ad', 'sp', 'show', '--id', $graphAppId)
            $wanted = 'Group.ReadWrite.All', 'GroupMember.ReadWrite.All', 'Application.ReadWrite.All', 'User.Read.All', 'Directory.Read.All'
            $graphRoles = @($graphSp.appRoles | Where-Object { $_.value -in $wanted -and $_.allowedMemberTypes -contains 'Application' })
            if (-not $graphSp -or $graphRoles.Count -ne $wanted.Count) {
                Add-Step 'graph' 'FAILED' 'Could not resolve the Microsoft Graph application roles'
            } else {
                $current = Invoke-AzCommand @('rest', '--method', 'get', '--url', "https://graph.microsoft.com/v1.0/servicePrincipals/$($sp.id)/appRoleAssignments")
                $haveIds = if ($current.Ok) { @($current.Json.value | Where-Object { $_.resourceId -eq $graphSp.id } | ForEach-Object { $_.appRoleId }) } else { @() }
                foreach ($role in $graphRoles) {
                    $step = "graph:$($role.value)"
                    if ($haveIds -contains $role.id) { Add-Step $step 'Exists' 'Application permission with admin consent'; continue }
                    if (-not $PSCmdlet.ShouldProcess($sp.displayName, "Grant Microsoft Graph application permission $($role.value)")) { Add-Step $step 'WhatIf' "Would grant $($role.value)"; continue }
                    $null = Invoke-AzCommand @('ad', 'app', 'permission', 'add', '--id', $sp.appId, '--api', $graphAppId, '--api-permissions', "$($role.id)=Role") -NoJson
                    $r = Invoke-WithRetry { Invoke-AzRestWrite "https://graph.microsoft.com/v1.0/servicePrincipals/$($graphSp.id)/appRoleAssignedTo" @{ principalId = $sp.id; resourceId = $graphSp.id; appRoleId = $role.id } }
                    if ($r.Ok -or $r.Error -match 'already exists') { Add-Step $step 'Granted' 'Application permission with admin consent' }
                    else { Add-Step $step 'FAILED' "Needs Privileged Role Administrator / Global Administrator: $($r.Error)" }
                }
            }
        }

        # 8c. Azure RBAC for azure_* types and Bicep deployments
        if ('Azure' -in $GrantScopes) {
            foreach ($role in $AzureRoles) { Grant-AzureRole "azure:$role" $role "/subscriptions/$($subscription.id)" }
        }

        # 8d. Fabric workspace role on every target workspace that already exists
        if ('FabricWorkspace' -in $GrantScopes) {
            $fabricRole = (Get-Culture).TextInfo.ToTitleCase($WorkspaceRole)
            foreach ($t in $Targets) {
                $wsId = $targetInfo[$t].workspace_id
                $step = "fabric-workspace:$t"
                if (-not $wsId) { Add-Step $step 'Skipped' "Workspace '$($targetInfo[$t].workspace_name)' does not exist yet. Deploy creates it; the service principal becomes its admin when it creates it (needs FabricTenant)." ; continue }
                $ra = Invoke-AzCommand @('rest', '--method', 'get', '--url', "$fabricResource/v1/workspaces/$wsId/roleAssignments", '--resource', $fabricResource)
                $mine = if ($ra.Ok) { @($ra.Json.value | Where-Object { $_.principal.id -eq $sp.id }) | Select-Object -First 1 } else { $null }
                if ($mine) { Add-Step $step 'Exists' "$($mine.role) on $($targetInfo[$t].workspace_name)"; continue }
                if (-not $PSCmdlet.ShouldProcess($targetInfo[$t].workspace_name, "Grant workspace $fabricRole to $($sp.displayName)")) { Add-Step $step 'WhatIf' "Would grant $fabricRole on $($targetInfo[$t].workspace_name)"; continue }
                $r = Invoke-WithRetry { Invoke-AzRestWrite "$fabricResource/v1/workspaces/$wsId/roleAssignments" @{ principal = @{ id = $sp.id; type = 'ServicePrincipal' }; role = $fabricRole } -Resource $fabricResource }
                if ($r.Ok -or $r.Error -match 'already exists|PrincipalAlreadyHasWorkspaceRolePermissions') { Add-Step $step 'Granted' "$fabricRole on $($targetInfo[$t].workspace_name)" }
                else { Add-Step $step 'FAILED' "$($targetInfo[$t].workspace_name): $($r.Error)" }
            }
        }

        # 8e. Fabric tenant access: membership in the group the tenant settings allow
        if ('FabricTenant' -in $GrantScopes) {
            $safeGroup = $FabricAccessGroupName -replace "'", "''"
            $grp = @(Invoke-AzJson @('ad', 'group', 'list', '--filter', "displayName eq '$safeGroup'") | Where-Object { $null -ne $_ }) | Select-Object -First 1
            if (-not $grp) {
                if ($PSCmdlet.ShouldProcess($FabricAccessGroupName, 'Create Entra security group')) {
                    $nick = ($FabricAccessGroupName -replace '[^A-Za-z0-9]', '')
                    $r = Invoke-AzCommand @('ad', 'group', 'create', '--display-name', $FabricAccessGroupName, '--mail-nickname', $nick, '--description', 'Service principals allowed to call Fabric and admin APIs (udpi30)')
                    if ($r.Ok) { $grp = $r.Json; Add-Step 'fabric-access-group' 'Created' "$FabricAccessGroupName ($($grp.id))" }
                    else { Add-Step 'fabric-access-group' 'FAILED' $r.Error }
                } else { Add-Step 'fabric-access-group' 'WhatIf' "Would create group $FabricAccessGroupName" }
            } else {
                Add-Step 'fabric-access-group' 'Exists' "$FabricAccessGroupName ($($grp.id))"
            }
            if ($grp) {
                $member = Invoke-AzCommand @('ad', 'group', 'member', 'check', '--group', $grp.id, '--member-id', $sp.id, '--query', 'value')
                if ($member.Ok -and $member.Json -eq $true) {
                    Add-Step 'fabric-access-membership' 'Exists' "$($sp.displayName) in $FabricAccessGroupName"
                } elseif ($PSCmdlet.ShouldProcess($FabricAccessGroupName, "Add $($sp.displayName) to group")) {
                    $r = Invoke-WithRetry { Invoke-AzCommand @('ad', 'group', 'member', 'add', '--group', $grp.id, '--member-id', $sp.id) -NoJson }
                    if ($r.Ok) { Add-Step 'fabric-access-membership' 'Granted' "$($sp.displayName) in $FabricAccessGroupName" } else { Add-Step 'fabric-access-membership' 'FAILED' $r.Error }
                } else { Add-Step 'fabric-access-membership' 'WhatIf' "Would add $($sp.displayName) to $FabricAccessGroupName" }
            }
            Add-Step 'fabric-tenant-settings' 'MANUAL' "Enable group '$FabricAccessGroupName' for the tenant settings ServicePrincipalAccessPermissionAPIs, ServicePrincipalAccessGlobalAPIs and (for 'admin plan/apply') AllowServicePrincipalsUseReadAdminAPIs / AllowServicePrincipalsUseWriteAdminAPIs. examples/00-default declares these; apply with 'udpi30 admin apply' as a Fabric administrator."
            Add-Step 'fabric-capacity' 'MANUAL' 'Add the service principal as a Contributor of the Fabric capacity (Azure portal > Fabric capacity > Capacity administrators, or Fabric Admin portal > Capacity settings > Contributor permissions) so it can assign workspaces to it.'
        }

        # 8f. State storage
        if ('StateStorage' -in $GrantScopes -and $acct) {
            Grant-AzureRole 'state-storage' 'Storage Blob Data Contributor' $acct.id
        }

        # 8g. Key Vault secret access for ${keyvault.<vault>.<secret>}
        if ('KeyVault' -in $GrantScopes) {
            foreach ($vaultName in $KeyVaultNames) {
                $kv = Invoke-AzJson @('keyvault', 'show', '--name', $vaultName, '--subscription', $subscription.id)
                if (-not $kv) { Add-Step "keyvault:$vaultName" 'FAILED' 'Vault not found or not readable'; continue }
                if ($kv.properties.enableRbacAuthorization) {
                    Grant-AzureRole "keyvault:$vaultName" 'Key Vault Secrets User' $kv.id
                } elseif ($PSCmdlet.ShouldProcess($vaultName, 'Grant get/list secret access policy')) {
                    $r = Invoke-AzCommand @('keyvault', 'set-policy', '--name', $vaultName, '--object-id', $sp.id, '--secret-permissions', 'get', 'list', '--subscription', $subscription.id) -NoJson
                    if ($r.Ok) { Add-Step "keyvault:$vaultName" 'Granted' 'Access policy: secrets get, list' } else { Add-Step "keyvault:$vaultName" 'FAILED' $r.Error }
                } else { Add-Step "keyvault:$vaultName" 'WhatIf' 'Would grant secrets get, list' }
            }
        }

        # 8h. Optional client secret (never printed or written to the report)
        if ($CreateClientSecret) {
            if (-not $PSCmdlet.ShouldProcess($sp.displayName, 'Create a 180-day client secret')) {
                Add-Step 'client-secret' 'WhatIf' 'Would create a 180-day client secret'
            } else {
                $end = (Get-Date).AddDays(180).ToString('yyyy-MM-dd')
                $r = Invoke-AzCommand @('ad', 'app', 'credential', 'reset', '--id', $sp.appId, '--append', '--display-name', 'udpi30', '--end-date', $end)
                $createdSecret = if ($r.Ok -and $r.Json) { [string]$r.Json.password } else { $null }
                $r = $null
                if (-not $createdSecret) {
                    Add-Step 'client-secret' 'FAILED' 'Could not create the client secret'
                } else {
                    $env:AZURE_CLIENT_SECRET = $createdSecret
                    if ($SecretKeyVaultName) {
                        $tmp = [System.IO.Path]::GetTempFileName()
                        try {
                            [System.IO.File]::WriteAllText($tmp, $createdSecret, [System.Text.UTF8Encoding]::new($false))
                            $kvr = Invoke-AzCommand @('keyvault', 'secret', 'set', '--vault-name', $SecretKeyVaultName, '--name', 'udpi30-client-secret', '--file', $tmp, '--expires', "${end}T00:00:00Z", '--subscription', $subscription.id) -NoJson
                        } finally { Remove-Item $tmp -Force -ErrorAction SilentlyContinue -WhatIf:$false }
                        if ($kvr.Ok) { Add-Step 'client-secret' 'Created' "Expires $end. Stored in Key Vault '$SecretKeyVaultName' as udpi30-client-secret; also set in this process as AZURE_CLIENT_SECRET." }
                        else { Add-Step 'client-secret' 'FAILED' "Secret created (expires $end) but could not be stored in '$SecretKeyVaultName': $($kvr.Error). It exists only in this process's AZURE_CLIENT_SECRET - copy it now or reset the credential." }
                    } else {
                        Add-Step 'client-secret' 'Created' "Expires $end. Set as AZURE_CLIENT_SECRET in THIS process only (not stored anywhere). Copy it to your CI secret store now, or re-run with -SecretKeyVaultName."
                    }
                }
            }
        }

        Add-Step 'purview' 'MANUAL' 'If Purview scans this identity or it manages Purview data-plane objects, assign it a Purview collection role (for example Data Curator) in the Purview portal; collections are not ARM resources.'
    }
}

if (-not $sp -and $ServicePrincipalName) {
    $sp = Find-ServicePrincipal $ServicePrincipalName
}
if ($sp) {
    Add-Value 'AZURE_CLIENT_ID' $sp.appId 'CI/CD secret / environment variable' "az ad sp ($($sp.displayName))"
    Add-Value 'service_principal.object_id' $sp.id 'Fabric role assignment principal id (not stored in udp.yml)' 'az ad sp' 'Info'
    Add-Value 'targets.<t>.run_as.service_principal' $sp.displayName 'targets.<t>.run_as.service_principal (optional)' 'az ad sp' 'Optional'
} else {
    $why = if ($ServicePrincipalName) { "Service principal '$ServicePrincipalName' not found" } else { 'Not requested - pass -ServicePrincipalName, or see docs/guide/service-principal.md' }
    Add-Value 'AZURE_CLIENT_ID' $null 'CI/CD secret / environment variable' $why 'MISSING'
}
if ($createdSecret) {
    Add-Value 'AZURE_CLIENT_SECRET' '(created - not shown)' 'CI/CD secret (mark as secret)' 'Created by -CreateClientSecret; see the provision.client-secret row for where it is stored' 'Info'
    $createdSecret = $null
} else {
    Add-Value 'AZURE_CLIENT_SECRET' $null 'CI/CD secret (mark as secret)' 'Cannot be retrieved. Create with: az ad app credential reset --id <appId> --append (or re-run with -ProvisionServicePrincipal -CreateClientSecret)' 'MANUAL'
}

# ---------------------------------------------------------------------------------------------
# 9. Git integration (optional)
# ---------------------------------------------------------------------------------------------
$git = $null
if (Get-Command git -ErrorAction SilentlyContinue) {
    $remote = (& git remote get-url origin 2>$null)
    $branch = (& git rev-parse --abbrev-ref HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or -not $branch -or $branch -eq 'HEAD') { $branch = 'main' }
    if ($remote -match '^(?:https://(?:[^@/]+@)?github\.com/|git@github\.com:)(?<org>[^/]+)/(?<repo>[^/]+?)(?:\.git)?$') {
        $git = [ordered]@{ provider = 'github'; organization = $Matches.org; repository = $Matches.repo; branch = $branch; directory = '/' }
    } elseif ($remote -match 'dev\.azure\.com/(?<org>[^/]+)/(?<proj>[^/]+)/_git/(?<repo>[^/]+?)$') {
        $git = [ordered]@{ provider = 'azuredevops'; organization = $Matches.org; project = [uri]::UnescapeDataString($Matches.proj); repository = $Matches.repo; branch = $branch; directory = '/' }
    } elseif ($remote -match 'ssh\.dev\.azure\.com:v3/(?<org>[^/]+)/(?<proj>[^/]+)/(?<repo>.+)$') {
        $git = [ordered]@{ provider = 'azuredevops'; organization = $Matches.org; project = $Matches.proj; repository = $Matches.repo; branch = $branch; directory = '/' }
    }
}
if ($git) {
    Add-Value 'workspace.git_integration' (($git.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join '; ') 'workspace.git_integration (optional)' 'git remote origin' 'Optional'
}

# ---------------------------------------------------------------------------------------------
# 10. Local environment check (names only - values are never printed)
# ---------------------------------------------------------------------------------------------
foreach ($name in 'AZURE_TENANT_ID', 'AZURE_CLIENT_ID', 'AZURE_CLIENT_SECRET', 'FABRIC_CAPACITY_ID') {
    $isSet = [bool][Environment]::GetEnvironmentVariable($name)
    Add-Value "env:$name" $(if ($isSet) { '(set)' } else { '(not set)' }) 'Current shell environment' 'Environment check' 'Info'
}
$notes.Add('Without AZURE_* variables the tool falls back to DefaultAzureCredential (az login), which is fine for local runs.')
$notes.Add('Azure/Entra resource types deploy via the az CLI and Graph: the identity needs rights on the subscription, and Group/Application.ReadWrite.All for Entra types.')

# ---------------------------------------------------------------------------------------------
# 11. Build outputs
# ---------------------------------------------------------------------------------------------
$capacityIds = @($Targets | ForEach-Object { if ($capacityMap[$_]) { $capacityMap[$_].id } else { '' } } | Select-Object -Unique)
$sameCapacity = $capacityIds.Count -le 1

$yaml = [System.Collections.Generic.List[string]]::new()
$yaml.Add('# Generated by scripts\Get-UdpYmlValues.ps1 - review, then merge into udp.yml')
$yaml.Add('deployment:')
$yaml.Add("  name: $DeploymentName")
$yaml.Add('  version: "0.1.0"')
$yaml.Add('')
$yaml.Add('azure:')
$yaml.Add('  subscription: "${var.subscription_id}"')
$yaml.Add("  location: $Location")
$yaml.Add('')
$yaml.Add('variables:')
if ($sameCapacity -and $defaultCapacity) {
    $yaml.Add('  capacity_id:')
    $yaml.Add('    description: "Fabric capacity GUID"')
    $yaml.Add("    default: $(ConvertTo-YamlString $defaultCapacity.id)")
}
$yaml.Add('  subscription_id:')
$yaml.Add('    description: "Target Azure subscription id"')
$yaml.Add("    default: $(ConvertTo-YamlString $subscription.id)")

if ($IncludeGitIntegration -and $git) {
    $yaml.Add('')
    $yaml.Add('workspace:')
    $yaml.Add('  git_integration:')
    foreach ($kv in $git.GetEnumerator()) { $yaml.Add("    $($kv.Key): $(ConvertTo-YamlString $kv.Value)") }
}

if ($StateBackend -ne 'local' -and $stateConfig.Count -gt 0) {
    $yaml.Add('')
    $yaml.Add('state:')
    $yaml.Add("  backend: $StateBackend")
    $yaml.Add('  config:')
    foreach ($kv in $stateConfig.GetEnumerator()) { $yaml.Add("    $($kv.Key): $(ConvertTo-YamlString $kv.Value)") }
}

if ($groupResults.Count -gt 0) {
    $yaml.Add('')
    $yaml.Add('security:')
    $yaml.Add('  roles:')
    foreach ($g in $groupResults) {
        $roleName = $g.name -replace '[^A-Za-z0-9]+', '_'
        $yaml.Add("    - name: $roleName")
        $yaml.Add("      entra_group: $(ConvertTo-YamlString $g.name)")
        $yaml.Add("      workspace_role: $($g.role)")
    }
}

$yaml.Add('')
$yaml.Add('targets:')
$first = $true
foreach ($t in $Targets) {
    $info = $targetInfo[$t]
    $yaml.Add("  ${t}:")
    if ($first) { $yaml.Add('    default: true'); $first = $false }
    $yaml.Add('    workspace:')
    $yaml.Add("      name: $(ConvertTo-YamlString $info.workspace_name)")
    if ($info.workspace_id) { $yaml.Add("      workspace_id: $(ConvertTo-YamlString $info.workspace_id)") }
    if ($sameCapacity) {
        if ($defaultCapacity) { $yaml.Add('      capacity_id: "${var.capacity_id}"') }
    } elseif ($info.capacity_id) {
        $yaml.Add("      capacity_id: $(ConvertTo-YamlString $info.capacity_id)")
    }
    if ($sp -and $t -ne $Targets[0]) {
        $yaml.Add('    run_as:')
        $yaml.Add("      service_principal: $(ConvertTo-YamlString $sp.displayName)")
    }
}

if ($null -ne $udpDocument) {
    $updates = ConvertFrom-Yaml -Yaml ($yaml -join "`n") -Ordered -ErrorAction Stop
    if ($udpDocument['deployment'] -is [System.Collections.IDictionary] -and $udpDocument['deployment'].Contains('version')) {
        $updates['deployment'].Remove('version')
    }
    $existingTargets = $udpDocument['targets']
    if ($existingTargets -is [System.Collections.IDictionary] -and
        @($existingTargets.Values | Where-Object { $_ -is [System.Collections.IDictionary] -and $_['default'] -eq $true }).Count -gt 0) {
        foreach ($target in $updates['targets'].Values) { $target.Remove('default') }
    }
    foreach ($variable in $updates['variables'].Keys) {
        $existingVariables = $udpDocument['variables']
        if ($existingVariables -is [System.Collections.IDictionary] -and
            $existingVariables[$variable] -is [System.Collections.IDictionary] -and
            $existingVariables[$variable].Contains('description')) {
            $updates['variables'][$variable].Remove('description')
        }
    }
    Merge-UdpValues -Existing $udpDocument -Updates $updates
    $updatedYaml = ConvertTo-Yaml -Data $udpDocument -ErrorAction Stop
    if ($PSCmdlet.ShouldProcess($udpPath, 'Merge discovered udp.yml values (with backup)')) {
        if (-not (Test-Path -LiteralPath $udpPath -PathType Leaf) -or
            [System.IO.File]::ReadAllText($udpPath) -cne $udpOriginal) {
            throw "UdpFile '$udpPath' changed during discovery; no update was written. Re-run against the current file."
        }
        $tempPath = Join-Path (Split-Path -Parent $udpPath) ".$([guid]::NewGuid().ToString('N')).tmp"
        $backupPath = "$udpPath.$([guid]::NewGuid().ToString('N')).bak"
        try {
            [System.IO.File]::WriteAllText($tempPath, $updatedYaml, [System.Text.UTF8Encoding]::new($false))
            [System.IO.File]::Replace($tempPath, $udpPath, $backupPath)
        } finally {
            if (Test-Path -LiteralPath $tempPath) { Remove-Item -LiteralPath $tempPath -Force -WhatIf:$false }
        }
        $udpUpdate.status = 'Updated'
        $udpUpdate.backup = $backupPath
        $notes.Add("Updated '$udpPath'. Original saved to '$backupPath'. YAML comments and formatting may have changed.")
    } else {
        $udpUpdate.status = 'Skipped'
        $notes.Add("UdpFile '$udpPath' was not changed (WhatIf or confirmation declined).")
    }
}

$envLines = [System.Collections.Generic.List[string]]::new()
$envLines.Add('# Generated by scripts\Get-UdpYmlValues.ps1 - dot-source this file: . .\udp-env.ps1')
$envLines.Add("`$env:AZURE_TENANT_ID = '$resolvedTenantId'")
if ($sp) { $envLines.Add("`$env:AZURE_CLIENT_ID = '$($sp.appId)'") } else { $envLines.Add('# $env:AZURE_CLIENT_ID = ''<service principal appId>''') }
$envLines.Add('# The secret is never written by this script. Prompt for it instead of hard-coding:')
$envLines.Add('# $env:AZURE_CLIENT_SECRET = [System.Net.NetworkCredential]::new('''', (Read-Host ''Client secret'' -AsSecureString)).Password')
if ($defaultCapacity) { $envLines.Add("`$env:FABRIC_CAPACITY_ID = '$($defaultCapacity.id)'") }
if ($stateConfig['account_name']) { $envLines.Add("`$env:AZURE_STORAGE_ACCOUNT_NAME = '$($stateConfig['account_name'])'") }

$result = [ordered]@{
    generatedAt      = (Get-Date).ToString('o')
    deployment       = $DeploymentName
    tenantId         = $resolvedTenantId
    subscription     = [ordered]@{ id = $subscription.id; name = $subscription.name }
    location         = $Location
    capacities       = $capacityJson
    targets          = $targetInfo
    state            = [ordered]@{ backend = $StateBackend; config = $stateConfig }
    securityGroups   = @($groupResults)
    servicePrincipal = if ($sp) { [ordered]@{ displayName = $sp.displayName; appId = $sp.appId; objectId = $sp.id } } else { $null }
    provisioning     = @($provisioning)
    gitIntegration   = $git
    values           = @($rows)
    notes            = @($notes)
    udpFile          = $udpUpdate
}

New-Item -ItemType Directory -Path $OutputDirectory -Force -WhatIf:$false | Out-Null
$jsonPath = Join-Path $OutputDirectory 'udp-values.json'
$yamlPath = Join-Path $OutputDirectory 'udp-values.yml'
$envPath = Join-Path $OutputDirectory 'udp-env.ps1'
$result | ConvertTo-Json -Depth 8 | Set-Content -Path $jsonPath -Encoding UTF8 -WhatIf:$false
$yaml | Set-Content -Path $yamlPath -Encoding UTF8 -WhatIf:$false
$envLines | Set-Content -Path $envPath -Encoding UTF8 -WhatIf:$false

# ---------------------------------------------------------------------------------------------
# 12. Console report
# ---------------------------------------------------------------------------------------------
Write-Host ''
Write-Host 'udp.yml values' -ForegroundColor Green
$rows | Select-Object Key, Value, Status, Where | Format-Table -AutoSize -Wrap | Out-String -Width 220 | Write-Host

$missing = @($rows | Where-Object { $_.Status -in 'MISSING', 'MANUAL' })
if ($missing.Count -gt 0) {
    Write-Host 'Action needed:' -ForegroundColor Yellow
    $missing | ForEach-Object { Write-Host "  - $($_.Key): $($_.Source)" -ForegroundColor Yellow }
}
if ($notes.Count -gt 0) {
    Write-Host ''
    Write-Host 'Notes:' -ForegroundColor Cyan
    $notes | ForEach-Object { Write-Host "  - $_" }
}
Write-Host ''
Write-Host "Report:          $jsonPath"
Write-Host "udp.yml snippet: $yamlPath"
Write-Host "Env script:      $envPath"
if ($udpUpdate.status -eq 'Updated') {
    Write-Host "Updated udp.yml: $udpPath"
    Write-Host "Backup:          $($udpUpdate.backup)"
    Write-Host "Next: review the updated file, then run: udpi30 validate -f `"$udpPath`"; udpi30 plan -f `"$udpPath`" -t $($Targets[0])" -ForegroundColor Green
} else {
    Write-Host 'Next: merge the snippet into udp.yml, then run: udpi30 validate; udpi30 plan -t dev' -ForegroundColor Green
}
} finally {
    if ($restoreSubscription) {
        $null = Invoke-AzJson @('account', 'set', '--subscription', $account.id)
        $restoredAccount = Invoke-AzJson @('account', 'show')
        if (-not $restoredAccount -or $restoredAccount.id -ne $account.id -or $restoredAccount.tenantId -ne $account.tenantId) {
            Write-Error "Could not restore the original Azure CLI context. Run 'az account set --subscription $($account.id)'."
        }
    }
}
