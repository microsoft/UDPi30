#Requires -Version 7.2

<#
.SYNOPSIS
    Provisions and runs the Azure DevOps pipeline for all UDP deployment examples.
.DESCRIPTION
    Discovers Azure, Azure DevOps, and GitHub metadata; creates or reuses a
    secret-free AzureRM workload identity service connection; creates the
    protected variable group and YAML pipeline; applies pipeline-scoped
    permissions and branch control; then optionally queues and monitors the
    existing deployment-example harness.

    A ready GitHub service connection backed by the Azure Pipelines GitHub App
    must already exist because GitHub consent cannot be created safely in a
    headless script. Secret example inputs are read only from environment
    variables and are never written to the bootstrap report.
.EXAMPLE
    ./cicd/Initialize-AzureDevOpsDeploymentExamples.ps1 `
      -Organization contoso `
      -Project 'Data Platform' `
      -GitHubRepository microsoft/UDPi30 `
      -GitHubServiceConnectionName PatrickGallucci `
      -Mode Preflight `
      -WhatIf
.EXAMPLE
    $env:UDP_TEST_FABRIC_CAPACITY_ID = '<capacity-guid>'
    ./cicd/Initialize-AzureDevOpsDeploymentExamples.ps1 `
      -Organization contoso `
      -Project 'Data Platform' `
      -GitHubRepository microsoft/UDPi30 `
      -GitHubServiceConnectionName PatrickGallucci `
      -AzureServiceConnectionName udp-deployment-tests-wif `
      -Mode Deploy
#>

[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Organization,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Project,
    [string]$GitHubRepository,
    [string]$GitHubServiceConnectionName,
    [string]$PipelineName = 'udp-deployment-examples',
    [string]$PipelineFolder = '\',
    [string]$PipelineYamlPath = 'cicd/deployment-examples.azure-devops.yml',
    [string]$Branch = 'main',
    [string]$VariableGroupName = 'udp-deployment-tests',
    [string]$AzureServiceConnectionName = 'udp-deployment-tests-wif',
    [guid]$AzureSubscriptionId = [guid]::Empty,
    [guid]$TenantId = [guid]::Empty,
    [guid]$ServicePrincipalAppId = [guid]::Empty,
    [string]$ServicePrincipalDisplayName,
    [string]$AzureRoleDefinitionName,
    [string]$AzureRoleScope,
    [guid]$FabricCapacityId = [guid]::Empty,
    [hashtable]$Variable = @{},
    [switch]$UpdateSecrets,
    [ValidateSet('Preflight', 'Validate', 'Plan', 'Deploy')][string]$Mode = 'Preflight',
    [string[]]$ExampleId = @(),
    [string]$InputsPath,
    [switch]$KeepResources,
    [switch]$AllowTenantAdminChanges,
    [switch]$SkipBranchControl,
    [switch]$SkipRun,
    [bool]$WaitForCompletion = $true,
    [bool]$DownloadEvidence = $true,
    [ValidateRange(1, 1440)][int]$RunTimeoutMinutes = 360,
    [ValidateRange(5, 300)][int]$PollSeconds = 15,
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'AzureDevOpsDeploymentExamples.psm1') -Force

try {
    $result = Initialize-UdpAzureDevOpsExamples @PSBoundParameters
    $result
    if ([string]$result.status -eq 'Failed') { exit 1 }
}
catch {
    Write-Error $_
    exit 1
}
