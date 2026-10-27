#Requires -Version 7.2

BeforeAll {
    $scriptPath = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'scripts\Get-UdpYmlValues.ps1'
    $source = Get-Content -LiteralPath $scriptPath -Raw
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseInput($source, [ref]$null, [ref]$parseErrors)
    if ($parseErrors) { throw ($parseErrors.Message -join '; ') }
    # Substitute only the CLI boundary so the script runs without real credentials or background jobs.
    $cliFunction = $ast.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-AzJson'
    }, $false)
    $script:discoveryScript = [scriptblock]::Create($source.Remove(
        $cliFunction.Extent.StartOffset,
        $cliFunction.Extent.EndOffset - $cliFunction.Extent.StartOffset
    ))
    function Invoke-AzJson {
        param([string[]]$Arguments)
        throw 'The Azure CLI boundary must be mocked.'
    }
}

Describe 'udp.yml value discovery tenant selection' {
    BeforeEach {
        $script:originalTenant = '11111111-1111-1111-1111-111111111111'
        $script:otherTenant = '22222222-2222-2222-2222-222222222222'
        $script:originalAccount = [pscustomobject]@{
            id = '33333333-3333-3333-3333-333333333333'
            tenantId = $script:originalTenant
            name = 'Original'
            state = 'Enabled'
            user = @{ name = 'test@example.com' }
        }
        $script:otherAccount = [pscustomobject]@{
            id = '44444444-4444-4444-4444-444444444444'
            tenantId = $script:otherTenant
            name = 'Other'
            state = 'Enabled'
            user = @{ name = 'test@example.com' }
        }
        $script:accounts = @($script:originalAccount, $script:otherAccount)
        $script:activeAccount = $script:originalAccount
        $script:cliCalls = [System.Collections.Generic.List[object]]::new()
        $script:failSwitch = $false
        $script:parameters = @{
            DeploymentName = 'tenant-test'
            Targets = @('dev')
            Location = 'eastus'
            NonInteractive = $true
            OutputDirectory = Join-Path $TestDrive ([guid]::NewGuid().ToString())
        }
        Mock Get-Command { [pscustomobject]@{ Name = 'az' } } -ParameterFilter { $Name -eq 'az' }
        Mock Get-Command { $null } -ParameterFilter { $Name -eq 'git' }
        Mock Write-Host {}
        Mock Invoke-AzJson {
            param([string[]]$Arguments)
            $script:cliCalls.Add([pscustomobject]@{
                Arguments = $Arguments
                TenantId = $script:activeAccount.tenantId
            })
            switch ("$($Arguments[0]) $($Arguments[1])") {
                'account show' {
                    if ($Arguments.Count -gt 2) {
                        return $script:accounts | Where-Object { $_.id -eq $Arguments[3] -or $_.name -eq $Arguments[3] }
                    }
                    return $script:activeAccount
                }
                'account list' { return $script:accounts }
                'account set' {
                    if (-not $script:failSwitch) {
                        $script:activeAccount = $script:accounts | Where-Object { $_.id -eq $Arguments[3] }
                    }
                    return $null
                }
                'rest --method' {
                    if ($Arguments[4] -like '*/capacities') {
                        return @{ value = @(@{ id = '55555555-5555-5555-5555-555555555555'; displayName = 'Test'; state = 'Active' }) }
                    }
                    return @{ value = @() }
                }
                'ad group' { return @(@{ id = 'group-id'; displayName = 'test-group' }) }
                'ad sp' { return @{ appId = 'app-id'; id = 'object-id'; displayName = 'test-sp' } }
                default { throw "Unexpected CLI call: $($Arguments -join ' ')" }
            }
        }
    }

    It 'preserves current-tenant discovery when TenantId is omitted' {
        & $script:discoveryScript @script:parameters
        $report = Get-Content (Join-Path $script:parameters.OutputDirectory 'udp-values.json') -Raw | ConvertFrom-Json
        $report.tenantId | Should -Be $script:originalTenant
        $report.subscription.id | Should -Be $script:originalAccount.id
        @($script:cliCalls | Where-Object { $_.Arguments[1] -eq 'set' }).Count | Should -Be 0
    }

    It 'uses the requested tenant for Fabric and Entra and restores the original subscription' {
        $script:parameters.TenantId = $script:otherTenant
        $script:parameters.SecurityGroups = @('test-group:viewer')
        $script:parameters.ServicePrincipalName = 'test-sp'
        & $script:discoveryScript @script:parameters
        $report = Get-Content (Join-Path $script:parameters.OutputDirectory 'udp-values.json') -Raw | ConvertFrom-Json
        $report.tenantId | Should -Be $script:otherTenant
        $report.subscription.id | Should -Be $script:otherAccount.id
        Get-Content (Join-Path $script:parameters.OutputDirectory 'udp-env.ps1') -Raw |
            Should -Match ([regex]::Escape("`$env:AZURE_TENANT_ID = '$($script:otherTenant)'"))
        $discoveryCalls = @($script:cliCalls | Where-Object { $_.Arguments[0] -in 'rest', 'ad' })
        $discoveryCalls.Count | Should -Be 4
        foreach ($call in $discoveryCalls) { $call.TenantId | Should -Be $script:otherTenant }
        $script:activeAccount.id | Should -Be $script:originalAccount.id
        $switches = @($script:cliCalls | Where-Object { $_.Arguments[1] -eq 'set' })
        $switches.Count | Should -Be 2
        $switches[0].Arguments[3] | Should -Be $script:otherAccount.id
        $switches[1].Arguments[3] | Should -Be $script:originalAccount.id
    }

    It 'accepts an explicit subscription in the requested tenant' {
        $script:parameters.TenantId = $script:otherTenant
        $script:parameters.SubscriptionId = $script:otherAccount.id
        & $script:discoveryScript @script:parameters
        $report = Get-Content (Join-Path $script:parameters.OutputDirectory 'udp-values.json') -Raw | ConvertFrom-Json
        $report.tenantId | Should -Be $script:otherTenant
        $script:activeAccount.id | Should -Be $script:originalAccount.id
    }

    It 'does not change context when the requested tenant is already current' {
        $script:parameters.TenantId = $script:originalTenant
        & $script:discoveryScript @script:parameters
        @($script:cliCalls | Where-Object { $_.Arguments[1] -eq 'set' }).Count | Should -Be 0
    }

    It 'rejects a subscription in another tenant before querying Fabric or Entra' {
        $script:parameters.TenantId = $script:otherTenant
        $script:parameters.SubscriptionId = $script:originalAccount.id
        { & $script:discoveryScript @script:parameters } | Should -Throw '*not requested tenant*'
        @($script:cliCalls | Where-Object { $_.Arguments[0] -in 'rest', 'ad' }).Count | Should -Be 0
        Test-Path $script:parameters.OutputDirectory | Should -BeFalse
    }

    It 'does not fall back to another tenant when no enabled subscription is available' {
        $script:otherAccount.state = 'Disabled'
        $script:parameters.TenantId = $script:otherTenant
        { & $script:discoveryScript @script:parameters } | Should -Throw "*az login --tenant $($script:otherTenant)*"
        @($script:cliCalls | Where-Object { $_.Arguments[0] -in 'rest', 'ad' }).Count | Should -Be 0
        Test-Path $script:parameters.OutputDirectory | Should -BeFalse
    }

    It 'fails before discovery if the requested context cannot be selected' {
        $script:failSwitch = $true
        $script:parameters.TenantId = $script:otherTenant
        { & $script:discoveryScript @script:parameters } | Should -Throw '*Could not select subscription*'
        @($script:cliCalls | Where-Object { $_.Arguments[0] -in 'rest', 'ad' }).Count | Should -Be 0
        $script:activeAccount.id | Should -Be $script:originalAccount.id
    }

    It 'restores the original subscription when discovery fails' {
        $script:parameters.TenantId = $script:otherTenant
        $script:parameters.CapacityName = 'missing-capacity'
        { & $script:discoveryScript @script:parameters } | Should -Throw "*Capacity named 'missing-capacity' was not found*"
        $script:activeAccount.id | Should -Be $script:originalAccount.id
        @($script:cliCalls | Where-Object { $_.Arguments[1] -eq 'set' }).Count | Should -Be 2
    }

    It 'rejects malformed tenant IDs before invoking Azure CLI' {
        $script:parameters.TenantId = 'not-a-guid'
        { & $script:discoveryScript @script:parameters } | Should -Throw
        $script:cliCalls.Count | Should -Be 0
    }

    It 'merges discovered values while retaining resources, metadata, and the existing default target' {
        $script:parameters.UdpFile = Join-Path $TestDrive 'existing udp.yml'
        @'
# Preserve this original in the backup
deployment:
  name: old-name
  version: "2.3.4"
  description: Keep deployment description
variables:
  subscription_id: old-subscription
  capacity_id:
    description: Keep capacity description
    default: old-capacity
  custom: keep-value
resources:
  lakehouses:
    analytics:
      description: Keep lakehouse
targets:
  dev:
    workspace:
      name: old-workspace
      description: Keep workspace description
    variables:
      custom: target-value
  prod:
    default: true
    workspace:
      name: existing-prod
security:
  roles:
    - name: test_group
      entra_group: old-group
      workspace_role: contributor
      onelake_roles:
        - tables: ["sales"]
          permissions: ["read"]
    - name: retained_role
      entra_group: retained-group
      workspace_role: admin
'@ | Set-Content -LiteralPath $script:parameters.UdpFile
        $original = [System.IO.File]::ReadAllBytes($script:parameters.UdpFile)
        $script:parameters.SecurityGroups = @('test-group:viewer')
        & $script:discoveryScript @script:parameters
        $updated = ConvertFrom-Yaml (Get-Content -LiteralPath $script:parameters.UdpFile -Raw)
        $updated.deployment.name | Should -Be 'tenant-test'
        $updated.deployment.version | Should -Be '2.3.4'
        $updated.deployment.description | Should -Be 'Keep deployment description'
        $updated.variables.subscription_id | Should -Be $script:originalAccount.id
        $updated.variables.capacity_id.default | Should -Be '55555555-5555-5555-5555-555555555555'
        $updated.variables.capacity_id.description | Should -Be 'Keep capacity description'
        $updated.variables.custom | Should -Be 'keep-value'
        $updated.resources.lakehouses.analytics.description | Should -Be 'Keep lakehouse'
        $updated.targets.dev.workspace.name | Should -Be 'tenant-test-dev'
        $updated.targets.dev.workspace.description | Should -Be 'Keep workspace description'
        $updated.targets.dev.variables.custom | Should -Be 'target-value'
        $updated.targets.prod.default | Should -BeTrue
        $updated.targets.dev.ContainsKey('default') | Should -BeFalse
        $updated.security.roles.Count | Should -Be 2
        $role = $updated.security.roles | Where-Object name -EQ 'test_group'
        $role.workspace_role | Should -Be 'viewer'
        $role.onelake_roles[0].tables[0] | Should -Be 'sales'
        $report = Get-Content (Join-Path $script:parameters.OutputDirectory 'udp-values.json') -Raw | ConvertFrom-Json
        $report.udpFile.status | Should -Be 'Updated'
        [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($report.udpFile.backup)) |
            Should -Be ([Convert]::ToBase64String($original))
    }

    It 'warns for a missing UdpFile without creating it or suppressing the normal reports' {
        $script:parameters.UdpFile = Join-Path $TestDrive 'missing.yml'
        Mock Write-Warning {}
        & $script:discoveryScript @script:parameters
        Test-Path -LiteralPath $script:parameters.UdpFile | Should -BeFalse
        Test-Path (Join-Path $script:parameters.OutputDirectory 'udp-values.yml') | Should -BeTrue
        Should -Invoke Write-Warning -Times 1 -ParameterFilter { $Message -like '*does not exist*' }
    }

    It 'honors WhatIf without updating the original file or creating a backup' {
        $script:parameters.UdpFile = Join-Path $TestDrive 'preview.yml'
        'resources: { lakehouses: { keep_me: {} } }' | Set-Content -LiteralPath $script:parameters.UdpFile
        $original = Get-Content -LiteralPath $script:parameters.UdpFile -Raw
        & $script:discoveryScript @script:parameters -WhatIf
        Get-Content -LiteralPath $script:parameters.UdpFile -Raw | Should -BeExactly $original
        @(Get-ChildItem -LiteralPath $TestDrive -Filter 'preview.yml.*.bak').Count | Should -Be 0
        $report = Get-Content (Join-Path $script:parameters.OutputDirectory 'udp-values.json') -Raw | ConvertFrom-Json
        $report.udpFile.status | Should -Be 'Skipped'
    }

    It 'rejects invalid or unsupported YAML before discovery without touching the file' -ForEach @(
        @{ Content = 'targets: [invalid'; Label = 'malformed' }
        @{ Content = '- not-a-mapping'; Label = 'sequence' }
        @{ Content = "deployment: {name: one}`n---`ndeployment: {name: two}"; Label = 'multiple-documents' }
    ) {
        $script:parameters.UdpFile = Join-Path $TestDrive "$Label.yml"
        $Content | Set-Content -LiteralPath $script:parameters.UdpFile
        $original = Get-Content -LiteralPath $script:parameters.UdpFile -Raw
        { & $script:discoveryScript @script:parameters } | Should -Throw
        Get-Content -LiteralPath $script:parameters.UdpFile -Raw | Should -BeExactly $original
        $script:cliCalls.Count | Should -Be 0
    }

    It 'rejects directory and output-report paths before discovery' {
        $script:parameters.UdpFile = $TestDrive
        { & $script:discoveryScript @script:parameters } | Should -Throw '*must be a file*'
        New-Item -ItemType Directory -Path $script:parameters.OutputDirectory | Out-Null
        $script:parameters.UdpFile = Join-Path $script:parameters.OutputDirectory 'udp-values.yml'
        'deployment: {name: keep}' | Set-Content -LiteralPath $script:parameters.UdpFile
        { & $script:discoveryScript @script:parameters } | Should -Throw '*output report path*'
        $script:cliCalls.Count | Should -Be 0
    }

    It 'does not overwrite a file that changes during discovery' {
        $script:parameters.UdpFile = Join-Path $TestDrive 'concurrent.yml'
        'deployment: {name: before}' | Set-Content -LiteralPath $script:parameters.UdpFile
        Mock Invoke-AzJson {
            'deployment: {name: changed}' | Set-Content -LiteralPath $script:parameters.UdpFile
            return $script:originalAccount
        } -ParameterFilter { $Arguments[0] -eq 'account' -and $Arguments[1] -eq 'show' }
        { & $script:discoveryScript @script:parameters } | Should -Throw '*changed during discovery*'
        Get-Content -LiteralPath $script:parameters.UdpFile -Raw | Should -Match 'name: changed'
        @(Get-ChildItem -LiteralPath $TestDrive -Filter 'concurrent.yml.*.bak').Count | Should -Be 0
    }

    It 'rejects incompatible sections instead of replacing unrelated configuration' {
        $script:parameters.UdpFile = Join-Path $TestDrive 'incompatible.yml'
        'targets: preserve-this-scalar' | Set-Content -LiteralPath $script:parameters.UdpFile
        { & $script:discoveryScript @script:parameters } | Should -Throw "*'targets'*expected a YAML mapping*"
        Get-Content -LiteralPath $script:parameters.UdpFile -Raw | Should -Match 'preserve-this-scalar'
    }

    It 'updates scalar capacity variables and appends new roles without losing unrelated ones' {
        $script:parameters.UdpFile = Join-Path $TestDrive 'short-variables.yml'
        @'
variables:
  capacity_id: old-capacity
security:
  roles:
    - name: retained
      entra_group: other-group
      workspace_role: admin
'@ | Set-Content -LiteralPath $script:parameters.UdpFile
        $script:parameters.SecurityGroups = @('test-group:viewer')
        & $script:discoveryScript @script:parameters
        $updated = ConvertFrom-Yaml (Get-Content -LiteralPath $script:parameters.UdpFile -Raw)
        $updated.variables.capacity_id | Should -Be '55555555-5555-5555-5555-555555555555'
        $updated.security.roles.Count | Should -Be 2
        $updated.security.roles[0].name | Should -Be 'retained'
        $updated.security.roles[1].name | Should -Be 'test_group'
    }

    It 'does not clear unresolved workspace IDs or add secrets to the updated file' {
        $script:parameters.UdpFile = Join-Path $TestDrive 'unresolved.yml'
        @'
targets:
  dev:
    workspace:
      workspace_id: preserve-existing-id
'@ | Set-Content -LiteralPath $script:parameters.UdpFile
        & $script:discoveryScript @script:parameters
        $updated = ConvertFrom-Yaml (Get-Content -LiteralPath $script:parameters.UdpFile -Raw)
        $updated.targets.dev.workspace.workspace_id | Should -Be 'preserve-existing-id'
        $updated.targets.dev.workspace.capacity_id | Should -Be '${var.capacity_id}'
        Get-Content -LiteralPath $script:parameters.UdpFile -Raw | Should -Not -Match 'AZURE_CLIENT_SECRET'
    }

    It 'updates all reference examples without changing their resource definitions' {
        $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        foreach ($example in Get-ChildItem (Join-Path $repoRoot 'examples') -Directory) {
            $sourceFile = Join-Path $example.FullName 'udp.yml'
            if (-not (Test-Path -LiteralPath $sourceFile)) { continue }
            $script:parameters.UdpFile = Join-Path $TestDrive "$($example.Name).yml"
            Copy-Item -LiteralPath $sourceFile -Destination $script:parameters.UdpFile
            $original = ConvertFrom-Yaml (Get-Content -LiteralPath $sourceFile -Raw)
            & $script:discoveryScript @script:parameters
            $updated = ConvertFrom-Yaml (Get-Content -LiteralPath $script:parameters.UdpFile -Raw)
            ConvertTo-Yaml -Data $updated.resources | Should -BeExactly (ConvertTo-Yaml -Data $original.resources)
            $updated.deployment.version | Should -Be $original.deployment.version
        }
    }
}
