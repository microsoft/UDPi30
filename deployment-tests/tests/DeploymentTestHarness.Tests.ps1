#Requires -Version 7.2

BeforeAll {
    $harnessRoot = Split-Path -Parent $PSScriptRoot
    $repositoryRoot = Split-Path -Parent $harnessRoot
    Import-Module (Join-Path $harnessRoot 'Modules/DeploymentTestHarness.psm1') -Force
    $inventoryPath = Join-Path $harnessRoot 'config/examples.json'
    $inventory = Read-UdpJson -Path $inventoryPath
}

Describe 'Deployment example inventory' {
    It 'contains exactly 14 unique examples' {
        $inventory.examples.Count | Should -Be 14
        @($inventory.examples.id | Sort-Object -Unique).Count | Should -Be 14
        ($inventory.examples | Where-Object id -eq '12').verification | Should -Contain 'purview-account-ready'
        ($inventory.examples | Where-Object id -eq '11').verification | Should -Not -Contain 'purview-account-ready'
    }

    It 'points every entry to an example with udp.yml' {
        foreach ($example in $inventory.examples) {
            $manifest = Join-Path (Join-Path $repositoryRoot $example.path) 'udp.yml'
            $manifest | Should -Exist
        }
    }
}

Describe 'Manifest discovery' {
    It 'finds the minimal example resources and required capacity input' {
        $manifest = Join-Path $repositoryRoot 'examples/01-minimal/udp.yml'
        $facts = Get-UdpManifestFacts -ManifestPath $manifest

        @($facts.resources).Count | Should -Be 2
        $facts.variableReferences | Should -Contain 'capacity_id'
        $facts.missingAssets | Should -BeNullOrEmpty
    }

    It 'finds absent definition assets in the catalogue example' {
        $manifest = Join-Path $repositoryRoot 'examples/08-all-resource-types/udp.yml'
        $facts = Get-UdpManifestFacts -ManifestPath $manifest

        $facts.missingAssets | Should -Contain './notebooks/ingest.py'
        $facts.missingAssets.Count | Should -BeGreaterThan 5
    }

    It 'does not allow duplicate top-level variables in example 05' {
        $manifest = Join-Path $repositoryRoot 'examples/05-multi-environment/udp.yml'
        $facts = Get-UdpManifestFacts -ManifestPath $manifest

        $facts.duplicateTopLevelKeys | Should -Not -Contain 'variables'
        $facts.variableDefaults.Keys | Should -Contain 'capacity_id'
        $facts.variableDefaults.Keys | Should -Contain 'db_host'
        $facts.variableDefaults.Keys | Should -Not -Contain 'slack_webhook'
        $facts.secretReferences | Should -Contain 'UDP_TEST_SLACK_WEBHOOK'
        $facts.secretReferences | Should -Contain 'PROD_DB_HOST'
    }

    It 'requires secret environment presence without returning the value' {
        $manifest = Join-Path $repositoryRoot 'examples/05-multi-environment/udp.yml'
        $facts = Get-UdpManifestFacts -ManifestPath $manifest
        $secretName = 'UDP_TEST_SLACK_WEBHOOK'
        $secretValue = 'https://hooks.example.invalid/sentinel-secret'
        $previousValue = [Environment]::GetEnvironmentVariable($secretName)

        try {
            [Environment]::SetEnvironmentVariable($secretName, $null)
            $missing = @(Get-UdpRequirements -Facts $facts -Mode Deploy | Where-Object name -eq $secretName)
            $missing.Count | Should -Be 1
            $missing[0].kind | Should -Be 'secret'
            ($missing[0] | ConvertTo-Json -Compress) | Should -Not -Match ([regex]::Escape($secretValue))

            [Environment]::SetEnvironmentVariable($secretName, $secretValue)
            @(Get-UdpRequirements -Facts $facts -Mode Deploy | Where-Object name -eq $secretName) | Should -BeNullOrEmpty

            [Environment]::SetEnvironmentVariable($secretName, '$(' + $secretName + ')')
            @(Get-UdpRequirements -Facts $facts -Mode Deploy | Where-Object name -eq $secretName).Count | Should -Be 1
        }
        finally {
            [Environment]::SetEnvironmentVariable($secretName, $previousValue)
        }
    }

    It 'ignores unresolved Azure DevOps JSON variable expressions' {
        $principalName = 'UDP_TEST_PRINCIPAL_MAPPINGS_JSON'
        $replacementName = 'UDP_TEST_TEXT_REPLACEMENTS_JSON'
        $previousPrincipal = [Environment]::GetEnvironmentVariable($principalName)
        $previousReplacement = [Environment]::GetEnvironmentVariable($replacementName)

        try {
            [Environment]::SetEnvironmentVariable($principalName, '$(' + $principalName + ')')
            [Environment]::SetEnvironmentVariable($replacementName, '$(' + $replacementName + ')')
            $inputs = Get-UdpDefaultInputs
            $inputs.principalMappings.Count | Should -Be 0
            $inputs.textReplacements.Count | Should -Be 0
        }
        finally {
            [Environment]::SetEnvironmentVariable($principalName, $previousPrincipal)
            [Environment]::SetEnvironmentVariable($replacementName, $previousReplacement)
        }
    }

    It 'maps tenant-admin group object IDs from non-secret environment inputs' {
        $variableName = 'UDP_TEST_DATA_ENG_GROUP_ID'
        $objectId = '11111111-1111-1111-1111-111111111111'
        $previousValue = [Environment]::GetEnvironmentVariable($variableName)

        try {
            [Environment]::SetEnvironmentVariable($variableName, $objectId)
            $inputs = Get-UdpDefaultInputs
            $inputs.values.data_eng_group_id | Should -Be $objectId
        }
        finally {
            [Environment]::SetEnvironmentVariable($variableName, $previousValue)
        }
    }

    It 'requires exact tenant-setting coverage in the restore manifest' {
        $manifest = Join-Path $repositoryRoot 'examples/09-tenant-settings/udp.yml'
        $facts = Get-UdpManifestFacts -ManifestPath $manifest

        @($facts.tenantSettingNames).Count | Should -Be 4
        $facts.tenantSettingNames | Should -Contain 'PublishToWeb'
        $facts.tenantSettingFields.UsageMetricsSettings | Should -Contain 'delegate_to_capacity'
        $facts.tenantSettingFields.UsageMetricsSettings | Should -Contain 'enabled_security_groups'
        $facts.tenantSettingFields.ExportToCsvSettings | Should -Contain 'properties'
        $matching = Compare-UdpTenantSettingCoverage `
            -MutationSettingNames $facts.tenantSettingNames `
            -RestoreSettingNames $facts.tenantSettingNames `
            -MutationFieldsBySetting $facts.tenantSettingFields `
            -RestoreFieldsBySetting $facts.tenantSettingFields
        $matching.matches | Should -BeTrue

        $mismatch = Compare-UdpTenantSettingCoverage `
            -MutationSettingNames @('PublishToWeb', 'UsageMetricsSettings') `
            -RestoreSettingNames @('PublishToWeb', 'ExportToCsvSettings')
        $mismatch.matches | Should -BeFalse
        $mismatch.missingFromRestore | Should -Contain 'UsageMetricsSettings'
        $mismatch.extraInRestore | Should -Contain 'ExportToCsvSettings'

        $fieldMismatch = Compare-UdpTenantSettingCoverage `
            -MutationSettingNames @('UsageMetricsSettings') `
            -RestoreSettingNames @('UsageMetricsSettings') `
            -MutationFieldsBySetting @{ UsageMetricsSettings = @('enabled', 'delegate_to_capacity') } `
            -RestoreFieldsBySetting @{ UsageMetricsSettings = @('enabled') }
        $fieldMismatch.matches | Should -BeFalse
        $fieldMismatch.missingFieldsFromRestore | Should -Contain 'UsageMetricsSettings.delegate_to_capacity'
    }

    It 'polls tenant settings until the admin plan converges' {
        $attempts = [System.Collections.Generic.List[int]]::new()
        $phase = Wait-UdpAdminPlanConvergence -Name 'test-admin-convergence' -TimeoutSeconds 5 -PollIntervalSeconds 0 -InvokePlan {
            param($attempt)
            $attempts.Add($attempt)
            $updates = if ($attempt -eq 1) { 1 } else { 0 }
            return [ordered]@{
                name = "attempt-$attempt"
                status = 'Passed'
                stdout = "Summary: $updates to update, 0 unchanged"
                stderr = ''
                exitCode = 0
                timedOut = $false
                durationSeconds = 0
                stdoutPath = $null
                stderrPath = $null
            }
        }

        $phase.status | Should -Be 'Passed'
        $phase.attempts | Should -Be 2
        @($attempts) | Should -Be @(1, 2)
    }

    It 'captures and compares the complete live values of mutated tenant fields' {
        $fields = [ordered]@{
            UsageMetricsSettings = @('enabled', 'delegate_to_capacity', 'enabled_security_groups', 'excluded_security_groups', 'properties')
        }
        $live = @(
            [ordered]@{
                settingName = 'UsageMetricsSettings'
                enabled = $true
                delegateToCapacity = $false
                enabledSecurityGroups = @()
                excludedSecurityGroups = @([ordered]@{ graphId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'; name = 'Existing' })
                properties = @(
                    [ordered]@{ name = 'Zeta'; value = '2'; type = 'Integer' }
                    [ordered]@{ name = 'Alpha'; value = 'one'; type = 'FreeText' }
                )
            }
        )

        $snapshot = New-UdpTenantSettingSnapshot -LiveSettings $live -FieldsBySetting $fields
        $snapshot.UsageMetricsSettings.enabledSecurityGroups.Count | Should -Be 0
        $snapshot.UsageMetricsSettings.excludedSecurityGroups[0].graphId | Should -Be 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'
        $snapshot.UsageMetricsSettings.properties.name | Should -Be @('Alpha', 'Zeta')
        (Compare-UdpTenantSettingSnapshot -ExpectedSnapshot $snapshot -LiveSettings $live).matches | Should -BeTrue
        (Get-UdpTenantSettingSnapshotFingerprint -Snapshot $snapshot) | Should -Match '^[0-9a-f]{64}$'

        $live[0].delegateToCapacity = $true
        $comparison = Compare-UdpTenantSettingSnapshot -ExpectedSnapshot $snapshot -LiveSettings $live
        $comparison.matches | Should -BeFalse
        $comparison.mismatchedSettingNames | Should -Contain 'UsageMetricsSettings'
    }

    It 'polls until the full tenant snapshot is restored' {
        $expectedLive = @([ordered]@{ settingName = 'PublishToWeb'; enabled = $false })
        $expected = New-UdpTenantSettingSnapshot -LiveSettings $expectedLive -FieldsBySetting @{ PublishToWeb = @('enabled') }
        $attempts = [System.Collections.Generic.List[int]]::new()
        $phase = Wait-UdpTenantSettingSnapshotConvergence -Name 'restore-snapshot' -ExpectedSnapshot $expected -TimeoutSeconds 5 -PollIntervalSeconds 0 -InvokeRead {
            param($attempt)
            $attempts.Add($attempt)
            $enabled = $attempt -gt 1
            return [ordered]@{
                name = "attempt-$attempt"
                status = 'Passed'
                tenantSettings = @([ordered]@{ settingName = 'PublishToWeb'; enabled = (-not $enabled) })
                exitCode = 0
                timedOut = $false
                durationSeconds = 0
                stdoutPath = $null
                stderrPath = $null
            }
        }

        $phase.status | Should -Be 'Passed'
        $phase.attempts | Should -Be 2
        @($attempts) | Should -Be @(1, 2)
    }

    It 'fails tenant convergence immediately for an unknown setting' {
        $phase = Wait-UdpAdminPlanConvergence -Name 'test-admin-unknown' -TimeoutSeconds 5 -PollIntervalSeconds 0 -InvokePlan {
            return [ordered]@{
                name = 'attempt-1'
                status = 'Passed'
                stdout = "UnknownSetting (unknown setting)`nSummary: 0 to update, 0 unchanged, 1 unknown"
                stderr = ''
                exitCode = 0
                timedOut = $false
                durationSeconds = 0
                stdoutPath = $null
                stderrPath = $null
            }
        }

        $phase.status | Should -Be 'Failed'
        $phase.attempts | Should -Be 1
        $phase.message | Should -Match 'unknown tenant setting'
    }

    It 'fails tenant convergence when the propagation deadline expires' {
        $started = [DateTimeOffset]::Parse('2026-01-01T00:00:00Z')
        $times = [System.Collections.Generic.Queue[DateTimeOffset]]::new()
        $times.Enqueue($started)
        $times.Enqueue($started.AddSeconds(2))
        $phase = Wait-UdpAdminPlanConvergence -Name 'test-admin-timeout' -TimeoutSeconds 1 -PollIntervalSeconds 0 -GetUtcNow {
            $times.Dequeue()
        } -Delay {} -InvokePlan {
            return [ordered]@{
                name = 'attempt-1'
                status = 'Passed'
                stdout = 'Summary: 1 to update, 0 unchanged'
                stderr = ''
                exitCode = 0
                timedOut = $false
                durationSeconds = 0
                stdoutPath = $null
                stderrPath = $null
            }
        }

        $phase.status | Should -Be 'Failed'
        $phase.message | Should -Match 'did not converge within 1 seconds'
    }

    It 'authorizes cleanup only for the same ID, marker, and child resource set' {
        $expectedChildren = @(
            '/subscriptions/test/resourceGroups/rg-test/providers/Microsoft.Storage/storageAccounts/one'
            '/subscriptions/test/resourceGroups/rg-test/providers/Microsoft.KeyVault/vaults/two'
        )
        $matching = Test-UdpOwnershipSnapshot `
            -ExpectedId '/subscriptions/test/resourceGroups/rg-test' `
            -ActualId '/SUBSCRIPTIONS/TEST/RESOURCEGROUPS/RG-TEST' `
            -ExpectedMarker 'udpi30-test-run:test01' `
            -ActualMarker 'udpi30-test-run:test01' `
            -ExpectedChildIds $expectedChildren `
            -ActualChildIds @($expectedChildren[1], $expectedChildren[0])

        $matching.matches | Should -BeTrue

        $drifted = Test-UdpOwnershipSnapshot `
            -ExpectedId '/subscriptions/test/resourceGroups/rg-test' `
            -ActualId '/subscriptions/test/resourceGroups/rg-test' `
            -ExpectedMarker 'udpi30-test-run:test01' `
            -ActualMarker 'udpi30-test-run:other' `
            -ExpectedChildIds $expectedChildren `
            -ActualChildIds @($expectedChildren + '/subscriptions/test/resourceGroups/rg-test/providers/Microsoft.Storage/storageAccounts/foreign')

        $drifted.matches | Should -BeFalse
        $drifted.markerMatches | Should -BeFalse
        $drifted.unexpectedChildIds.Count | Should -Be 1

        $reusedId = Test-UdpOwnershipSnapshot `
            -ExpectedId '11111111-1111-1111-1111-111111111111' `
            -ActualId '22222222-2222-2222-2222-222222222222' `
            -ExpectedMarker 'udpi30-test-run:test01' `
            -ActualMarker 'udpi30-test-run:test01'
        $reusedId.matches | Should -BeFalse
        $reusedId.idMatches | Should -BeFalse
    }

    It 'accepts only a successful 201 resource-group creation operation' {
        $resourceId = '/subscriptions/test/resourceGroups/rg-test'
        $operations = @(
            [ordered]@{
                properties = [ordered]@{
                    provisioningOperation = 'Create'
                    provisioningState = 'Succeeded'
                    statusCode = 'Created'
                    targetResource = [ordered]@{
                        id = $resourceId
                        resourceType = 'Microsoft.Resources/resourceGroups'
                    }
                }
            }
        )

        $created = Test-UdpAzureCreationOperation -Operations $operations -ExpectedResourceId $resourceId
        $created.matches | Should -BeTrue
        $created.matchingOperationCount | Should -Be 1
        $created.statusCode | Should -Be 'Created'

        $operations[0].properties.statusCode = 'OK'
        $updated = Test-UdpAzureCreationOperation -Operations $operations -ExpectedResourceId $resourceId
        $updated.matches | Should -BeFalse
        $updated.statusCode | Should -Be 'OK'
    }

    It 'rejects ambiguous or mismatched Azure creation operations' {
        $resourceId = '/subscriptions/test/resourceGroups/rg-test'
        $operation = [ordered]@{
            properties = [ordered]@{
                provisioningOperation = 'Create'
                provisioningState = 'Succeeded'
                statusCode = '201'
                targetResource = [ordered]@{
                    id = $resourceId
                    resourceType = 'Microsoft.Resources/resourceGroups'
                }
            }
        }

        (Test-UdpAzureCreationOperation -Operations @($operation, $operation) -ExpectedResourceId $resourceId).matches | Should -BeFalse
        (Test-UdpAzureCreationOperation -Operations @($operation) -ExpectedResourceId '/subscriptions/test/resourceGroups/other').matches | Should -BeFalse
        $operation.properties.targetResource.resourceType = 'Microsoft.Storage/storageAccounts'
        (Test-UdpAzureCreationOperation -Operations @($operation) -ExpectedResourceId $resourceId).matches | Should -BeFalse
    }

    It 'captures Azure resource scope and resource-group ownership boundaries' {
        $manifest = Join-Path $repositoryRoot 'examples/10-azure-and-entra/udp.yml'
        $facts = Get-UdpManifestFacts -ManifestPath $manifest
        $deployment = $facts.resources | Where-Object name -eq 'keyvault-analytics'
        $storage = $facts.resources | Where-Object name -eq 'saanalyticsdev01'

        $deployment.scope | Should -Be 'group'
        $deployment.resourceGroup | Should -Be 'rg-analytics-dev'
        $storage.resourceGroup | Should -Be 'rg-analytics-dev'
    }

    It 'blocks subscription-scope generic Bicep from harness mutation' {
        $manifest = Join-Path $TestDrive 'subscription-scope.yml'
        @(
            'deployment:'
            '  name: subscription-scope-test'
            '  version: "1.0.0"'
            'resources:'
            '  azure_deployments:'
            '    subscription-policy:'
            '      scope: subscription'
            '      location: eastus'
            '      template_file: ./policy.bicep'
        ) | Set-Content -LiteralPath $manifest

        $facts = Get-UdpManifestFacts -ManifestPath $manifest
        $requirements = @(Get-UdpRequirements -Facts $facts -Mode Deploy)

        $requirements.kind | Should -Contain 'safety'
        ($requirements.reason -join ' ') | Should -Match 'Subscription-scope generic Bicep'
    }

    It 'blocks Azure resources targeting an undeclared resource group' {
        $manifest = Join-Path $TestDrive 'external-resource-group.yml'
        @(
            'deployment:'
            '  name: external-resource-group-test'
            '  version: "1.0.0"'
            'resources:'
            '  azure_storage_accounts:'
            '    isolatedstorage:'
            '      resource_group: shared-production-rg'
        ) | Set-Content -LiteralPath $manifest

        $facts = Get-UdpManifestFacts -ManifestPath $manifest
        $requirements = @(Get-UdpRequirements -Facts $facts -Mode Deploy)

        $requirements.kind | Should -Contain 'safety'
        ($requirements.reason -join ' ') | Should -Match 'not bounded by a resource group declared'
    }

    It 'verifies Purview through its read-only account data plane' {
        $fakeCli = Join-Path $TestDrive 'fake-purview-cli.ps1'
        @'
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Remaining)
Write-Output '{"name":"purview-test","provisioningState":"Succeeded"}'
'@ | Set-Content -LiteralPath $fakeCli -Encoding utf8

        $phase = Invoke-UdpPurviewVerification `
            -Resource ([ordered]@{ type = 'azure_purview_accounts'; name = 'purview-test' }) `
            -WorkingDirectory $TestDrive `
            -LogDirectory (Join-Path $TestDrive 'purview-logs') `
            -CliFilePath (Get-Command pwsh).Source `
            -CliPrefixArguments @('-NoLogo', '-NoProfile', '-File', $fakeCli)

        $phase.status | Should -Be 'Passed'
        $phase.command | Should -Match 'purview-test\.purview\.azure\.com/account/'
        $phase.command | Should -Match 'https://purview\.azure\.net'

        @'
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Remaining)
Write-Output '{"name":"purview-test","provisioningState":"Creating"}'
'@ | Set-Content -LiteralPath $fakeCli -Encoding utf8
        $notReady = Invoke-UdpPurviewVerification `
            -Resource ([ordered]@{ type = 'azure_purview_accounts'; name = 'purview-test' }) `
            -WorkingDirectory $TestDrive `
            -LogDirectory (Join-Path $TestDrive 'purview-not-ready-logs') `
            -CliFilePath (Get-Command pwsh).Source `
            -CliPrefixArguments @('-NoLogo', '-NoProfile', '-File', $fakeCli)
        $notReady.status | Should -Be 'Failed'

        @'
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Remaining)
Write-Output '{"name":"another-account","provisioningState":"Succeeded"}'
'@ | Set-Content -LiteralPath $fakeCli -Encoding utf8
        $wrongAccount = Invoke-UdpPurviewVerification `
            -Resource ([ordered]@{ type = 'azure_purview_accounts'; name = 'purview-test' }) `
            -WorkingDirectory $TestDrive `
            -LogDirectory (Join-Path $TestDrive 'purview-wrong-account-logs') `
            -CliFilePath (Get-Command pwsh).Source `
            -CliPrefixArguments @('-NoLogo', '-NoProfile', '-File', $fakeCli)
        $wrongAccount.status | Should -Be 'Failed'

        $unsafeName = Invoke-UdpPurviewVerification `
            -Resource ([ordered]@{ type = 'azure_purview_accounts'; name = 'safe.example.com/path' }) `
            -WorkingDirectory $TestDrive `
            -LogDirectory (Join-Path $TestDrive 'purview-unsafe-name-logs')
        $unsafeName.status | Should -Be 'Failed'
        $unsafeName.command | Should -BeNullOrEmpty
    }

    It 'classifies a generic Purview permission failure from its phase' {
        $finding = Get-UdpPermissionFinding `
            -Text 'Request failed with status code 403.' `
            -ExampleId '12' `
            -Phase 'purview-account-ready-contoso'

        $finding.platform | Should -Be 'Purview'
        $finding.recommendation | Should -Match 'Do not auto-grant'
    }
}

Describe 'Privileged pipeline and cleanup gates' {
    It 'restricts authenticated GitHub and Azure DevOps execution to main' {
        $githubWorkflow = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/workflows/deployment-examples.yml') -Raw
        $azurePipeline = Get-Content -LiteralPath (Join-Path $repositoryRoot 'cicd/deployment-examples.azure-devops.yml') -Raw

        $githubWorkflow | Should -Match "github\.ref == 'refs/heads/main'"
        $azurePipeline | Should -Match "BUILD_SOURCEBRANCH -ne 'refs/heads/main'"
        $azurePipeline | Should -Match "eq\(variables\['Build\.SourceBranch'\], 'refs/heads/main'\)"
    }

    It 'keeps deployment secrets out of validation-only branch jobs' {
        $githubWorkflow = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/workflows/deployment-examples.yml') -Raw
        $azurePipeline = Get-Content -LiteralPath (Join-Path $repositoryRoot 'cicd/deployment-examples.azure-devops.yml') -Raw
        $githubValidation = ($githubWorkflow -split '(?m)^  deploy-examples:\s*$')[0]
        $azureValidation = ($azurePipeline -split "(?m)^  - \$\{\{ if or\(eq\(parameters\.mode, 'Plan'\)")[0]

        $githubValidation | Should -Not -Match 'environment:\s*deployment-tests'
        $githubValidation | Should -Not -Match '\$\{\{\s*secrets\.'
        $azureValidation | Should -Not -Match 'UDP_TEST_SLACK_WEBHOOK|PROD_DB_HOST|UDP_TEST_ENTRA_TENANT_ID'
        $azurePipeline | Should -Match 'group:\s*udp-deployment-tests'
    }

    It 'does not resolve Entra cleanup targets by display name' {
        $harness = Get-Content -LiteralPath (Join-Path $harnessRoot 'Invoke-DeploymentTestSuite.ps1') -Raw

        $harness | Should -Not -Match 'cleanup-lookup-'
        $harness | Should -Match 'cleanup-delete-entra-'
        $harness | Should -Match '\$graphResource/v1\.0/\$\(\$entraObject\.kind\)/\$\(\$entraObject\.id\)'
    }

    It 'requires CLI creation evidence before issuing an ownership receipt' {
        $harness = Get-Content -LiteralPath (Join-Path $harnessRoot 'Invoke-DeploymentTestSuite.ps1') -Raw

        $harness | Should -Match 'Creating workspace:'
        $harness | Should -Match 'Created Entra \$\(\$entraKind\):'
        $harness | Should -Match 'deployment.+operation.+sub.+list'
        $harness | Should -Match 'Test-UdpAzureCreationOperation'
        $harness | Should -Match 'ownership-created-by-run'
    }

    It 'captures and restores complete tenant state without publishing values' {
        $harness = Get-Content -LiteralPath (Join-Path $harnessRoot 'Invoke-DeploymentTestSuite.ps1') -Raw

        $harness | Should -Match 'ownership-admin-live-snapshot'
        $harness | Should -Match 'New-UdpTenantSettingSnapshot'
        $harness | Should -Match 'Get-UdpTenantSettingSnapshotFingerprint'
        $harness | Should -Match 'Wait-UdpTenantSettingSnapshotConvergence'
        $harness | Should -Match 'tenantsettings/\$escapedSettingName/update'
        $harness | Should -Match "Host -ne 'api\.fabric\.microsoft\.com'"
        $harness | Should -Match 'SetUnixFileMode'
        $harness.Contains("executionMode -eq 'admin' -or -not `$KeepResources") | Should -BeTrue
        $harness | Should -Match 'raw response suppressed'
        $harness | Should -Match 'Remove-UdpPrivateDirectory'
        $harness | Should -Match 'restoreAttemptFailed'
        $harness | Should -Not -Match 'if \(\$restoreSucceeded\)'
        $harness | Should -Not -Match "cleanup-admin-restore' -Arguments @\('admin', 'apply'"
    }
}

Describe 'Staged example isolation' {
    It 'resolves a repository-relative input manifest within its containment root' {
        $resolved = Resolve-UdpContainedPath -Root $repositoryRoot -Candidate 'deployment-tests/config/inputs.example.json' -RequireRelative

        $resolved | Should -Be (Join-Path $repositoryRoot 'deployment-tests/config/inputs.example.json')
    }

    It 'rejects absolute input manifest paths when repository-relative input is required' {
        $absolute = Join-Path $repositoryRoot 'deployment-tests/config/inputs.example.json'

        { Resolve-UdpContainedPath -Root $repositoryRoot -Candidate $absolute -RequireRelative } |
            Should -Throw '*must be relative*'
    }

    It 'replaces inputs, isolates names, and leaves source unchanged' {
        $example = $inventory.examples | Where-Object id -eq '01'
        $sourceManifest = Join-Path $repositoryRoot 'examples/01-minimal/udp.yml'
        $sourceHashBefore = (Get-FileHash -LiteralPath $sourceManifest -Algorithm SHA256).Hash
        $inputs = Get-UdpDefaultInputs
        $inputs.values.capacity_id = '11111111-1111-1111-1111-111111111111'

        $staged = New-UdpStagedExample -Example $example -RepositoryRoot $repositoryRoot -WorkRoot $TestDrive -Inputs $inputs -RunSuffix 'testrun1' -InputBasePath $repositoryRoot
        $stagedContent = Get-Content -LiteralPath $staged.manifest -Raw -Encoding utf8

        $stagedContent | Should -Not -Match 'REPLACE-WITH-YOUR-CAPACITY-GUID'
        $stagedContent | Should -Match '11111111-1111-1111-1111-111111111111'
        $stagedContent | Should -Match 'minimal-example-dev-testrun1'
        (Get-FileHash -LiteralPath $sourceManifest -Algorithm SHA256).Hash | Should -Be $sourceHashBefore
    }

    It 'never materializes secret environment values in staged YAML' {
        $example = $inventory.examples | Where-Object id -eq '05'
        $secretName = 'UDP_TEST_SLACK_WEBHOOK'
        $secretValue = 'https://hooks.example.invalid/sentinel-secret'
        $previousValue = [Environment]::GetEnvironmentVariable($secretName)

        try {
            [Environment]::SetEnvironmentVariable($secretName, $secretValue)
            $inputs = Get-UdpDefaultInputs
            $inputs.values.Contains('slack_webhook') | Should -BeFalse

            $staged = New-UdpStagedExample -Example $example -RepositoryRoot $repositoryRoot -WorkRoot $TestDrive -Inputs $inputs -RunSuffix 'secret01' -InputBasePath $repositoryRoot
            $stagedContent = Get-Content -LiteralPath $staged.manifest -Raw -Encoding utf8

            $stagedContent | Should -Not -Match ([regex]::Escape($secretValue))
            $stagedContent | Should -Match '\$\{secret\.UDP_TEST_SLACK_WEBHOOK\}'
        }
        finally {
            [Environment]::SetEnvironmentVariable($secretName, $previousValue)
        }
    }

    It 'derives distinct isolated names from long run IDs with the same prefix' {
        $example = $inventory.examples | Where-Object id -eq '01'
        $inputs = Get-UdpDefaultInputs
        $firstRoot = Join-Path $TestDrive 'first'
        $secondRoot = Join-Path $TestDrive 'second'

        $first = New-UdpStagedExample -Example $example -RepositoryRoot $repositoryRoot -WorkRoot $firstRoot -Inputs $inputs -RunSuffix 'local20260731201440-aaaaaaaaaaaa' -InputBasePath $repositoryRoot
        $second = New-UdpStagedExample -Example $example -RepositoryRoot $repositoryRoot -WorkRoot $secondRoot -Inputs $inputs -RunSuffix 'local20260731201440-bbbbbbbbbbbb' -InputBasePath $repositoryRoot
        $firstContent = Get-Content -LiteralPath $first.manifest -Raw -Encoding utf8
        $secondContent = Get-Content -LiteralPath $second.manifest -Raw -Encoding utf8

        $firstContent | Should -Not -Be $secondContent
        $firstContent | Should -Not -Match 'minimal-example-dev-local202'
        $secondContent | Should -Not -Match 'minimal-example-dev-local202'
    }

    It 'preserves the complete hashed run token in length-limited Azure names' {
        $example = $inventory.examples | Where-Object id -eq '10'
        $inputs = Get-UdpDefaultInputs
        $runSuffix = 'local20260731201440-cccccccccccc'
        $tokenBytes = [Text.Encoding]::UTF8.GetBytes($runSuffix)
        $expectedToken = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($tokenBytes)).ToLowerInvariant().Substring(0, 12)

        $staged = New-UdpStagedExample -Example $example -RepositoryRoot $repositoryRoot -WorkRoot $TestDrive -Inputs $inputs -RunSuffix $runSuffix -InputBasePath $repositoryRoot
        $stagedContent = Get-Content -LiteralPath $staged.manifest -Raw -Encoding utf8

        $stagedContent | Should -Match "vaultName: 'kv-analytic-$expectedToken'"
        $stagedContent | Should -Match "rg-analytics-dev-$expectedToken"
    }

    It 'rejects secret-bearing ordinary input keys' {
        $example = $inventory.examples | Where-Object id -eq '05'
        $inputs = Get-UdpDefaultInputs
        $inputs.values.slack_webhook = 'sentinel-value'

        { New-UdpStagedExample -Example $example -RepositoryRoot $repositoryRoot -WorkRoot $TestDrive -Inputs $inputs -RunSuffix 'guard01' -InputBasePath $repositoryRoot } |
            Should -Throw '*secret-bearing*'
    }

    It 'rejects overlay destinations outside the staged example' {
        $example = $inventory.examples | Where-Object id -eq '08'
        $overlaySource = Join-Path $TestDrive 'overlay-source'
        New-Item -ItemType Directory -Path $overlaySource | Out-Null
        Set-Content -LiteralPath (Join-Path $overlaySource 'asset.txt') -Value 'asset'
        $inputs = Get-UdpDefaultInputs
        $inputs.assetOverlays['08'] = @(@{ source = 'overlay-source'; destination = '../escape' })

        { New-UdpStagedExample -Example $example -RepositoryRoot $repositoryRoot -WorkRoot (Join-Path $TestDrive 'work') -Inputs $inputs -RunSuffix 'guard02' -InputBasePath $TestDrive } |
            Should -Throw '*escapes its containment root*'
    }

    It 'rejects overlay sources outside the input root' {
        $example = $inventory.examples | Where-Object id -eq '08'
        $inputRoot = Join-Path $TestDrive 'inputs'
        $outsideSource = Join-Path $TestDrive 'outside-source'
        New-Item -ItemType Directory -Path $inputRoot, $outsideSource | Out-Null
        Set-Content -LiteralPath (Join-Path $outsideSource 'asset.txt') -Value 'asset'
        $inputs = Get-UdpDefaultInputs
        $inputs.assetOverlays['08'] = @(@{ source = '../outside-source'; destination = '.' })

        { New-UdpStagedExample -Example $example -RepositoryRoot $repositoryRoot -WorkRoot (Join-Path $TestDrive 'work') -Inputs $inputs -RunSuffix 'guard03' -InputBasePath $inputRoot } |
            Should -Throw '*escapes its containment root*'
    }

    It 'rejects arbitrary text replacements in manifest structure' {
        $example = $inventory.examples | Where-Object id -eq '01'
        $inputs = Get-UdpDefaultInputs
        $inputs.textReplacements['deployment:'] = 'security:'

        { New-UdpStagedExample -Example $example -RepositoryRoot $repositoryRoot -WorkRoot $TestDrive -Inputs $inputs -RunSuffix 'guard04' -InputBasePath $repositoryRoot } |
            Should -Throw '*not a discovered illustrative external binding*'
    }

    It 'requires principal mappings to use Entra object IDs' {
        $example = $inventory.examples | Where-Object id -eq '02'
        $inputs = Get-UdpDefaultInputs
        $inputs.principalMappings['sg-data-engineering'] = 'replacement-display-name'

        { New-UdpStagedExample -Example $example -RepositoryRoot $repositoryRoot -WorkRoot $TestDrive -Inputs $inputs -RunSuffix 'guard05' -InputBasePath $repositoryRoot } |
            Should -Throw '*must resolve to an Entra object ID*'
    }
}

Describe 'HTML report generation' {
    It 'creates a self-contained report with example status' {
        $reportPath = Join-Path $TestDrive 'report.html'
        $summary = [ordered]@{
            runId = 'unit-test'
            mode = 'Validate'
            completedAt = [DateTimeOffset]::UtcNow.ToString('o')
            counts = [ordered]@{ Passed = 1; Failed = 0; Blocked = 0 }
            results = @(
                [ordered]@{
                    id = '01'
                    name = 'minimal'
                    status = 'Passed'
                    supportLevel = 'deployable'
                    phases = @([ordered]@{ name = 'validate'; status = 'Passed' })
                    requirements = @()
                    planes = @('Fabric')
                }
            )
            permissionFindings = @()
        }

        New-UdpHtmlReport -Summary $summary -OutputPath $reportPath

        $reportPath | Should -Exist
        (Get-Content -LiteralPath $reportPath -Raw) | Should -Match 'UDP deployment test report'
        (Get-Content -LiteralPath $reportPath -Raw) | Should -Match 'minimal'
    }
}
