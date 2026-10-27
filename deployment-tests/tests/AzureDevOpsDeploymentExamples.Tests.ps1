#Requires -Version 7.2

BeforeAll {
    $repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $modulePath = Join-Path $repositoryRoot 'cicd/AzureDevOpsDeploymentExamples.psm1'
    Get-Module AzureDevOpsDeploymentExamples | Remove-Module -Force
    Import-Module $modulePath -Force
}

Describe 'Azure DevOps deployment example bootstrap contracts' {
    It 'normalizes organization names and GitHub clone URLs' {
        ConvertTo-AdoOrganizationUrl 'contoso' | Should -Be 'https://dev.azure.com/contoso'
        ConvertTo-AdoOrganizationUrl 'https://dev.azure.com/contoso/' | Should -Be 'https://dev.azure.com/contoso'
        ConvertTo-GitHubRepositorySlug 'git@github.com:microsoft/UDPi30.git' | Should -Be 'microsoft/UDPi30'
        ConvertTo-GitHubRepositorySlug 'https://github.com/microsoft/UDPi30.git' | Should -Be 'microsoft/UDPi30'
    }

    It 'rejects non-Azure-DevOps organization URLs' {
        { ConvertTo-AdoOrganizationUrl 'https://example.com/contoso' } | Should -Throw
    }

    It 'builds a secret-free workload identity endpoint payload' {
        $configuration = New-AdoWifServiceEndpointConfiguration `
            -Name 'udp-tests-wif' `
            -ProjectId '11111111-1111-1111-1111-111111111111' `
            -ProjectName 'Data Platform' `
            -SubscriptionId '22222222-2222-2222-2222-222222222222' `
            -SubscriptionName 'Test Subscription' `
            -TenantId '33333333-3333-3333-3333-333333333333' `
            -ServicePrincipalAppId '44444444-4444-4444-4444-444444444444'

        $configuration.authorization.scheme | Should -Be 'WorkloadIdentityFederation'
        $configuration.data.creationMode | Should -Be 'Manual'
        $configuration.isShared | Should -BeFalse
        ($configuration | ConvertTo-Json -Depth 20) | Should -Not -Match '(?i)serviceprincipalkey|clientsecret|password'
    }

    It 'builds strict main-branch control for protected resources' {
        $configuration = New-AdoBranchControlCheckConfiguration `
            -ResourceType variablegroup `
            -ResourceId '17'

        $configuration.type.id | Should -Be 'fe1de3ee-a436-41b4-bb20-f6eb4cb879a7'
        $configuration.settings.definitionRef.id | Should -Be '86b05a0c-73e6-4f7d-b3cf-e38f3b39a75b'
        $configuration.settings.inputs.allowedBranches | Should -Be 'refs/heads/main'
        $configuration.settings.inputs.ensureProtectionOfBranch | Should -Be 'true'
        $configuration.settings.inputs.allowUnknownStatusBranch | Should -Be 'false'
    }

    It 'authorizes one pipeline without opening protected resources globally' {
        $payload = New-AdoPipelinePermissionsPayload -PipelineId 42 -Resources @(
            [pscustomobject]@{ type = 'endpoint'; id = 'endpoint-id'; name = 'wif' }
            [pscustomobject]@{ type = 'variablegroup'; id = '17'; name = 'udp-deployment-tests' }
        )

        $payload.Count | Should -Be 2
        $payload[0].pipelines.Count | Should -Be 1
        $payload[0].pipelines[0].id | Should -Be 42
        $payload[0].pipelines[0].authorized | Should -BeTrue
        $payload[0].Contains('allPipelines') | Should -BeFalse
    }

    It 'keeps sensitive values out of non-secret variable input' {
        $values = Get-UdpVariableGroupValues `
            -AzureServiceConnectionName 'udp-tests-wif' `
            -SubscriptionId '22222222-2222-2222-2222-222222222222' `
            -TenantId '33333333-3333-3333-3333-333333333333'

        $values.UDP_AZURE_SERVICE_CONNECTION | Should -Be 'udp-tests-wif'
        $values.Contains('UDP_TEST_SLACK_WEBHOOK') | Should -BeFalse
        { Get-UdpVariableGroupValues `
            -AzureServiceConnectionName 'udp-tests-wif' `
            -SubscriptionId '22222222-2222-2222-2222-222222222222' `
            -TenantId '33333333-3333-3333-3333-333333333333' `
            -Variable @{ password = 'unsafe' } } | Should -Throw
    }

    It 'exposes WhatIf on both the module command and entry script' {
        $scriptPath = Join-Path $repositoryRoot 'cicd/Initialize-AzureDevOpsDeploymentExamples.ps1'
        (Get-Command Initialize-UdpAzureDevOpsExamples).Parameters.ContainsKey('WhatIf') | Should -BeTrue
        (Get-Command $scriptPath).Parameters.ContainsKey('WhatIf') | Should -BeTrue
    }

    It 'does not queue the pipeline during WhatIf orchestration' {
        InModuleScope AzureDevOpsDeploymentExamples {
            Mock Get-Command { [pscustomobject]@{ Name = 'mock' } }
            Mock Resolve-UdpRepositoryPath { 'C:\repo\cicd\deployment-examples.azure-devops.yml' }
            Mock Get-UdpGitHubRepositorySlug { 'owner/repository' }
            Mock Ensure-UdpAzureDevOpsExtension { $true }
            Mock Invoke-UdpAzJson {
                if ($Arguments[0] -eq 'account') {
                    return [ordered]@{
                        id = '22222222-2222-2222-2222-222222222222'
                        name = 'Test Subscription'
                        tenantId = '33333333-3333-3333-3333-333333333333'
                    }
                }
                return [ordered]@{
                    id = '11111111-1111-1111-1111-111111111111'
                    name = 'Data Platform'
                    state = 'wellFormed'
                    visibility = 'private'
                }
            }
            Mock Get-AdoServiceEndpoints { ,@() }
            Mock Resolve-AdoGitHubEndpoint {
                [ordered]@{ id = 'github-endpoint'; name = 'owner'; type = 'github'; isReady = $true }
            }
            Mock Ensure-AdoWifEndpoint {
                [ordered]@{ id = 'azure-endpoint'; name = 'udp-deployment-tests-wif'; authorization = [ordered]@{ scheme = 'WorkloadIdentityFederation' } }
            }
            Mock Ensure-AdoPipeline {
                [ordered]@{ id = 42; name = 'udp-deployment-examples' }
            }
            Mock Get-UdpVariableGroupValues {
                [ordered]@{ UDP_AZURE_SERVICE_CONNECTION = 'udp-deployment-tests-wif' }
            }
            Mock Ensure-AdoVariableGroup {
                [ordered]@{ id = 17; name = 'udp-deployment-tests' }
            }
            Mock Ensure-AdoPipelinePermissions
            Mock Ensure-AdoBranchControlCheck
            Mock Save-UdpBootstrapResult { 'C:\output\bootstrap-summary.json' }

            $result = Initialize-UdpAzureDevOpsExamples `
                -Organization contoso `
                -Project 'Data Platform' `
                -GitHubRepository owner/repository `
                -RepositoryRoot 'C:\repo' `
                -WhatIf

            $result.status | Should -Be 'Planned'
            Should -Invoke Invoke-UdpAzJson -Times 0 -ParameterFilter {
                $Arguments[0] -eq 'pipelines' -and $Arguments[1] -eq 'run'
            }
        }
    }
}
