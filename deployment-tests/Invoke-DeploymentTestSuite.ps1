#Requires -Version 7.2

<#
.SYNOPSIS
    Builds, validates, deploys, verifies, and reports on UDP examples.
.DESCRIPTION
    Treats each example as a black-box deployment unit. The harness reads and
    stages example files, invokes only documented udpi30 entry points, and
    validates observable outputs. It does not import udpi30 assemblies.
.EXAMPLE
    ./Invoke-DeploymentTestSuite.ps1 -Mode Preflight -Verbose
.EXAMPLE
    ./Invoke-DeploymentTestSuite.ps1 -Mode Deploy -InputsPath ./config/inputs.local.json
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('Preflight', 'Validate', 'Plan', 'Deploy')]
    [string]$Mode = 'Preflight',

    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),

    [string]$InventoryPath = (Join-Path $PSScriptRoot 'config/examples.json'),

    [string]$InputsPath,

    [ValidatePattern('^(0[1-9]|1[0-4])$')]
    [string[]]$ExampleId = @(),

    [string]$OutputDirectory,

    [string]$CliFilePath = 'udpi30',

    [string[]]$CliPrefixArguments = @(),

    [ValidateRange(30, 86400)]
    [int]$CommandTimeoutSeconds = 1800,

    [ValidateRange(1, 1800)]
    [int]$AdminPropagationTimeoutSeconds = 900,

    [ValidateRange(1, 300)]
    [int]$AdminPropagationPollSeconds = 30,

    [switch]$KeepResources,

    [switch]$AllowTenantAdminChanges,

    [string]$RunId
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Modules/DeploymentTestHarness.psm1') -Force
$harnessWhatIf = [bool]$WhatIfPreference
$WhatIfPreference = $false

function Add-PhaseResult {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$Target,
        [Parameter(Mandatory)][System.Collections.IDictionary]$Phase
    )

    $entry = [ordered]@{
        name = $Phase.name
        status = $Phase.status
        command = $Phase.command
        exitCode = $Phase.exitCode
        timedOut = $Phase.timedOut
        durationSeconds = $Phase.durationSeconds
        stdoutPath = $Phase.stdoutPath
        stderrPath = $Phase.stderrPath
    }
    foreach ($optionalKey in @('attempts', 'message')) {
        if ($Phase.Contains($optionalKey)) {
            $entry[$optionalKey] = $Phase[$optionalKey]
        }
    }
    $Target.Add($entry)
}

function Invoke-TrackedPhase {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$LogDirectory,
        [Parameter(Mandatory)][string]$EventLog,
        [Parameter(Mandatory)][string]$CurrentExampleId,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$PermissionFindings,
        [string]$CommandFilePath,
        [string[]]$CommandPrefixArguments
    )

    Write-Verbose "[$CurrentExampleId] $Name"
    Write-UdpEvent -Path $EventLog -Level Information -Phase $Name -Message 'Starting command.' -Data @{ exampleId = $CurrentExampleId }
    $phaseFilePath = if ($CommandFilePath) { $CommandFilePath } else { $CliFilePath }
    $phasePrefixArguments = if ($PSBoundParameters.ContainsKey('CommandPrefixArguments')) { $CommandPrefixArguments } else { $CliPrefixArguments }
    $phase = Invoke-UdpCliPhase -Name $Name -CliFilePath $phaseFilePath -CliPrefixArguments $phasePrefixArguments -Arguments $Arguments -WorkingDirectory $WorkingDirectory -LogDirectory $LogDirectory -TimeoutSeconds $CommandTimeoutSeconds
    $level = if ($phase.status -eq 'Passed') { 'Information' } else { 'Error' }
    Write-UdpEvent -Path $EventLog -Level $level -Phase $Name -Message "Command $($phase.status.ToLowerInvariant())." -Data @{ exampleId = $CurrentExampleId; exitCode = $phase.exitCode; durationSeconds = $phase.durationSeconds }
    $finding = Get-UdpPermissionFinding -Text "$($phase.stdout)`n$($phase.stderr)" -ExampleId $CurrentExampleId -Phase $Name
    if ($finding) { $PermissionFindings.Add($finding) }
    return $phase
}

function Get-UdpLocalPhase {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][ValidateSet('Passed', 'Failed')][string]$Status,
        [Parameter(Mandatory)][string]$Message
    )

    return [ordered]@{
        name = $Name
        status = $Status
        command = $null
        exitCode = if ($Status -eq 'Passed') { 0 } else { 3 }
        timedOut = $false
        durationSeconds = 0
        stdoutPath = $null
        stderrPath = $null
        message = $Message
    }
}

function Get-UdpPhaseMessage {
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$Phase,
        [Parameter(Mandatory)][string]$Fallback
    )

    if ($Phase.Contains('message') -and -not [string]::IsNullOrWhiteSpace([string]$Phase['message'])) {
        return [string]$Phase['message']
    }
    return $Fallback
}

function Save-UdpOwnershipReceipt {
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$Receipt,
        [Parameter(Mandatory)][string]$Path
    )

    [void](New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force)
    $Receipt | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $Path -Encoding utf8
}

function Get-UdpNonEmptyLine {
    param([AllowNull()][string]$Text)

    return @($Text -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

function Get-UdpAzureDeploymentName {
    param([Parameter(Mandatory)][string]$ResourceName)

    $cleaned = -join @($ResourceName.ToCharArray() | ForEach-Object {
        if ([char]::IsLetterOrDigit($_) -or $_ -in @('-', '_')) { $_ } else { '-' }
    })
    if ([string]::IsNullOrEmpty($cleaned)) { $cleaned = 'deployment' }
    return "udp-$cleaned"
}

function New-UdpPrivateDirectory {
    $path = Join-Path ([IO.Path]::GetTempPath()) "udp-private-$([Guid]::NewGuid().ToString('N'))"
    [void][IO.Directory]::CreateDirectory($path)
    if (-not $IsWindows) {
        [IO.File]::SetUnixFileMode($path, [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute)
    }
    return $path
}

function Remove-UdpPrivateDirectory {
    param([Parameter(Mandatory)][string]$Path)

    if (Test-Path -LiteralPath $Path) {
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
        }
        catch {
            throw "Could not delete private temporary data at '$Path': $($_.Exception.Message)"
        }
    }
    if (Test-Path -LiteralPath $Path) {
        throw "Private temporary data still exists at '$Path' after deletion."
    }
}

function Invoke-UdpPrivateTrackedPhase {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$EventLog,
        [Parameter(Mandatory)][string]$CurrentExampleId,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$PermissionFindings,
        [string]$CommandFilePath = 'az'
    )

    $privateLogDirectory = New-UdpPrivateDirectory
    $privatePermissionFindings = [System.Collections.Generic.List[object]]::new()
    $phase = $null
    try {
        $phase = Invoke-TrackedPhase -Name $Name -Arguments $Arguments -WorkingDirectory $WorkingDirectory -LogDirectory $privateLogDirectory -EventLog $EventLog -CurrentExampleId $CurrentExampleId -PermissionFindings $privatePermissionFindings -CommandFilePath $CommandFilePath -CommandPrefixArguments @()
        foreach ($finding in $privatePermissionFindings) {
            $PermissionFindings.Add([ordered]@{
                exampleId = $CurrentExampleId
                phase = $Name
                platform = [string]$finding.platform
                action = $null
                scope = $null
                evidence = 'Authorization failure in a private tenant-setting operation; raw response suppressed.'
                recommendation = 'Use PSAutoRBAC discover/evaluate/generate at the narrowest scope. Do not auto-grant from this harness.'
            })
        }
    }
    finally {
        if ($phase) {
            $phase.stdoutPath = $null
            $phase.stderrPath = $null
        }
        Remove-UdpPrivateDirectory -Path $privateLogDirectory
    }
    return $phase
}

function Invoke-UdpTenantSettingsRead {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$EventLog,
        [Parameter(Mandatory)][string]$CurrentExampleId,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$PermissionFindings
    )

    $fabricResource = 'https://api.fabric.microsoft.com'
    $url = "$fabricResource/v1/admin/tenantsettings"
    $visited = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $settings = [System.Collections.Generic.List[object]]::new()
    $pageNumber = 0
    while ($url) {
        $pageNumber++
        if ($pageNumber -gt 100 -or -not $visited.Add($url)) {
            $failed = Get-UdpLocalPhase -Name $Name -Status Failed -Message 'Tenant-setting pagination exceeded 100 pages or repeated a continuation URL.'
            $failed['tenantSettings'] = @()
            return $failed
        }

        $page = Invoke-UdpPrivateTrackedPhase -Name "$Name-page-$pageNumber" -Arguments @('rest', '--method', 'get', '--url', $url, '--resource', $fabricResource, '--output', 'json', '--only-show-errors') -WorkingDirectory $WorkingDirectory -EventLog $EventLog -CurrentExampleId $CurrentExampleId -PermissionFindings $PermissionFindings
        if ($page.status -ne 'Passed') {
            $failed = Get-UdpLocalPhase -Name $Name -Status Failed -Message 'Could not read the complete live Fabric tenant-setting state.'
            $failed['tenantSettings'] = @()
            return $failed
        }

        try {
            $payload = $page.stdout | ConvertFrom-Json -AsHashtable
            $items = if ($payload -is [System.Collections.IDictionary] -and $payload.Contains('value')) {
                @($payload.value)
            }
            elseif ($payload -is [System.Collections.IDictionary] -and $payload.Contains('tenantSettings')) {
                @($payload.tenantSettings)
            }
            else {
                throw 'Response did not contain value or tenantSettings.'
            }
            foreach ($item in $items) {
                if ($item -is [System.Collections.IDictionary]) { $settings.Add($item) }
            }

            if ($payload.Contains('continuationUri') -and -not [string]::IsNullOrWhiteSpace([string]$payload.continuationUri)) {
                $continuationUri = [Uri]::new([Uri]"$fabricResource/", [string]$payload.continuationUri)
                if ($continuationUri.Scheme -ne 'https' -or $continuationUri.Host -ne 'api.fabric.microsoft.com' -or $continuationUri.Port -ne 443) {
                    throw 'Continuation URI escaped the HTTPS Fabric API origin.'
                }
                $url = $continuationUri.AbsoluteUri
            }
            elseif ($payload.Contains('continuationToken') -and -not [string]::IsNullOrWhiteSpace([string]$payload.continuationToken)) {
                $url = "$fabricResource/v1/admin/tenantsettings?continuationToken=$([Uri]::EscapeDataString([string]$payload.continuationToken))"
            }
            else {
                $url = $null
            }
        }
        catch {
            $failed = Get-UdpLocalPhase -Name $Name -Status Failed -Message "Fabric tenant-setting response was invalid: $($_.Exception.Message)"
            $failed['tenantSettings'] = @()
            return $failed
        }
        finally {
            $page.stdout = ''
            $page.stderr = ''
        }
    }

    $result = Get-UdpLocalPhase -Name $Name -Status Passed -Message "Read $($settings.Count) tenant settings; response values were retained only in memory."
    $result['tenantSettings'] = @($settings)
    return $result
}

function Set-UdpPrivateJsonFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Json
    )

    [IO.File]::WriteAllText($Path, $Json, [Text.UTF8Encoding]::new($false))
    if (-not $IsWindows) {
        [IO.File]::SetUnixFileMode($Path, [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite)
    }
}

function Set-UdpDeploymentOwnershipReceipt {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$Staged,
        [Parameter(Mandatory)][string]$Target,
        [Parameter(Mandatory)][System.Collections.IDictionary]$Receipt,
        [Parameter(Mandatory)][string]$ReceiptPath,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$LogDirectory,
        [Parameter(Mandatory)][string]$EventLog,
        [Parameter(Mandatory)][string]$CurrentExampleId,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$PermissionFindings,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$Ownership,
        [string]$SubscriptionId
    )

    if (-not $PSCmdlet.ShouldProcess($CurrentExampleId, 'Capture and mark deployment ownership')) {
        return
    }

    $marker = [string]$Receipt.marker
    $fabricResource = 'https://api.fabric.microsoft.com'
    $graphResource = 'https://graph.microsoft.com'
    $managementEndpoint = 'https://management.azure.com'

    $workspaceCapture = Invoke-TrackedPhase -Name 'ownership-capture-fabric-workspace' -Arguments @('status', '--file', $Staged.manifest, '--target', $Target) -WorkingDirectory $WorkingDirectory -LogDirectory $LogDirectory -EventLog $EventLog -CurrentExampleId $CurrentExampleId -PermissionFindings $PermissionFindings
    $workspaceMatch = [regex]::Match($workspaceCapture.stdout, '(?im)^\s*Workspace:\s*(?<name>.+?)\s+\((?<id>[0-9a-fA-F-]{36})\)\s*$')
    $workspaceId = [Guid]::Empty
    if ($workspaceCapture.status -ne 'Passed' -or
        -not $workspaceMatch.Success -or
        -not [Guid]::TryParse($workspaceMatch.Groups['id'].Value, [ref]$workspaceId)) {
        $workspaceCapture.status = 'Failed'
        $workspaceCapture['message'] = 'Could not capture the deployed Fabric workspace ID.'
    }
    Add-PhaseResult -Target $Ownership -Phase $workspaceCapture
    if ($workspaceCapture.status -ne 'Passed') {
        throw (Get-UdpPhaseMessage -Phase $workspaceCapture -Fallback 'Could not capture the deployed Fabric workspace ID.')
    }

    $workspaceName = $workspaceMatch.Groups['name'].Value.Trim()
    $workspaceUrl = "$fabricResource/v1/workspaces/$workspaceId"
    $workspaceBody = [ordered]@{ displayName = $workspaceName; description = $marker } | ConvertTo-Json -Compress
    $workspaceMark = Invoke-TrackedPhase -Name 'ownership-mark-fabric-workspace' -Arguments @('rest', '--method', 'patch', '--url', $workspaceUrl, '--resource', $fabricResource, '--body', $workspaceBody, '--output', 'none', '--only-show-errors') -WorkingDirectory $WorkingDirectory -LogDirectory $LogDirectory -EventLog $EventLog -CurrentExampleId $CurrentExampleId -PermissionFindings $PermissionFindings -CommandFilePath 'az' -CommandPrefixArguments @()
    Add-PhaseResult -Target $Ownership -Phase $workspaceMark
    if ($workspaceMark.status -ne 'Passed') {
        throw 'Could not mark the deployed Fabric workspace with this run ID.'
    }
    $Receipt['workspace'] = [ordered]@{ id = $workspaceId.ToString(); name = $workspaceName; marker = $marker; itemIds = @() }
    Save-UdpOwnershipReceipt -Receipt $Receipt -Path $ReceiptPath

    $workspaceVerify = Invoke-TrackedPhase -Name 'ownership-verify-fabric-workspace' -Arguments @('rest', '--method', 'get', '--url', $workspaceUrl, '--resource', $fabricResource, '--query', '{id:id,displayName:displayName,description:description}', '--output', 'json', '--only-show-errors') -WorkingDirectory $WorkingDirectory -LogDirectory $LogDirectory -EventLog $EventLog -CurrentExampleId $CurrentExampleId -PermissionFindings $PermissionFindings -CommandFilePath 'az' -CommandPrefixArguments @()
    if ($workspaceVerify.status -eq 'Passed') {
        try {
            $workspaceInfo = $workspaceVerify.stdout | ConvertFrom-Json -AsHashtable
            $workspaceProof = Test-UdpOwnershipSnapshot -ExpectedId $workspaceId.ToString() -ActualId ([string]$workspaceInfo.id) -ExpectedMarker $marker -ActualMarker ([string]$workspaceInfo.description)
            if (-not $workspaceProof.matches) {
                $workspaceVerify.status = 'Failed'
                $workspaceVerify['message'] = 'Fabric workspace ID or ownership marker did not match the capture receipt.'
            }
        }
        catch {
            $workspaceVerify.status = 'Failed'
            $workspaceVerify['message'] = "Fabric workspace ownership response was not valid JSON: $($_.Exception.Message)"
        }
    }
    Add-PhaseResult -Target $Ownership -Phase $workspaceVerify
    if ($workspaceVerify.status -ne 'Passed') {
        throw (Get-UdpPhaseMessage -Phase $workspaceVerify -Fallback 'Could not verify the Fabric workspace ownership marker.')
    }

    $workspaceItemsUrl = "$workspaceUrl/items"
    $workspaceItems = Invoke-TrackedPhase -Name 'ownership-capture-fabric-items' -Arguments @('rest', '--method', 'get', '--url', $workspaceItemsUrl, '--resource', $fabricResource, '--query', 'value[].id', '--output', 'tsv', '--only-show-errors') -WorkingDirectory $WorkingDirectory -LogDirectory $LogDirectory -EventLog $EventLog -CurrentExampleId $CurrentExampleId -PermissionFindings $PermissionFindings -CommandFilePath 'az' -CommandPrefixArguments @()
    Add-PhaseResult -Target $Ownership -Phase $workspaceItems
    if ($workspaceItems.status -ne 'Passed') {
        throw 'Could not capture Fabric item IDs for the ownership receipt.'
    }
    $Receipt.workspace.itemIds = @(Get-UdpNonEmptyLine -Text $workspaceItems.stdout | Sort-Object -Unique)
    Save-UdpOwnershipReceipt -Receipt $Receipt -Path $ReceiptPath

    foreach ($resourceGroup in @($Staged.facts.resources | Where-Object type -eq 'azure_resource_groups')) {
        $groupArguments = @('group', 'show', '--name', $resourceGroup.name, '--query', '{id:id,name:name,tags:tags}', '--output', 'json', '--only-show-errors')
        if ($SubscriptionId) { $groupArguments += @('--subscription', $SubscriptionId) }
        $groupCapture = Invoke-TrackedPhase -Name "ownership-capture-azure-$($resourceGroup.name)" -Arguments $groupArguments -WorkingDirectory $WorkingDirectory -LogDirectory $LogDirectory -EventLog $EventLog -CurrentExampleId $CurrentExampleId -PermissionFindings $PermissionFindings -CommandFilePath 'az' -CommandPrefixArguments @()
        if ($groupCapture.status -eq 'Passed') {
            try {
                $groupInfo = $groupCapture.stdout | ConvertFrom-Json -AsHashtable
                if ([string]::IsNullOrWhiteSpace([string]$groupInfo.id)) { throw 'Resource group ID was empty.' }
            }
            catch {
                $groupCapture.status = 'Failed'
                $groupCapture['message'] = "Azure resource-group ownership response was invalid: $($_.Exception.Message)"
            }
        }
        Add-PhaseResult -Target $Ownership -Phase $groupCapture
        if ($groupCapture.status -ne 'Passed') {
            throw (Get-UdpPhaseMessage -Phase $groupCapture -Fallback "Could not capture Azure resource group '$($resourceGroup.name)'.")
        }

        $groupId = [string]$groupInfo.id
        $groupUrl = "$managementEndpoint$($groupId)?api-version=2021-04-01"
        $deploymentName = Get-UdpAzureDeploymentName -ResourceName $resourceGroup.name
        $deploymentOperationArguments = @(
            'deployment', 'operation', 'sub', 'list',
            '--name', $deploymentName,
            '--query', '[].properties.{provisioningOperation:provisioningOperation,provisioningState:provisioningState,statusCode:statusCode,targetResource:targetResource}',
            '--output', 'json',
            '--only-show-errors'
        )
        if ($SubscriptionId) { $deploymentOperationArguments += @('--subscription', $SubscriptionId) }
        $creationOperation = Invoke-TrackedPhase -Name "ownership-prove-azure-create-$($resourceGroup.name)" -Arguments $deploymentOperationArguments -WorkingDirectory $WorkingDirectory -LogDirectory $LogDirectory -EventLog $EventLog -CurrentExampleId $CurrentExampleId -PermissionFindings $PermissionFindings -CommandFilePath 'az' -CommandPrefixArguments @()
        if ($creationOperation.status -eq 'Passed') {
            try {
                $deploymentOperations = @($creationOperation.stdout | ConvertFrom-Json -AsHashtable)
                $creationProof = Test-UdpAzureCreationOperation -Operations $deploymentOperations -ExpectedResourceId $groupId
                if (-not $creationProof.matches) {
                    $creationOperation.status = 'Failed'
                    $creationOperation['message'] = "Azure deployment '$deploymentName' did not prove a new resource-group creation (matches=$($creationProof.matchingOperationCount), operation=$($creationProof.provisioningOperation), state=$($creationProof.provisioningState), status=$($creationProof.statusCode))."
                }
            }
            catch {
                $creationOperation.status = 'Failed'
                $creationOperation['message'] = "Azure deployment-operation response was invalid: $($_.Exception.Message)"
            }
        }
        Add-PhaseResult -Target $Ownership -Phase $creationOperation
        if ($creationOperation.status -ne 'Passed') {
            throw (Get-UdpPhaseMessage -Phase $creationOperation -Fallback "Could not prove Azure resource group '$($resourceGroup.name)' was created by this run.")
        }

        $groupTags = [ordered]@{}
        if ($groupInfo.tags -is [System.Collections.IDictionary]) {
            foreach ($entry in $groupInfo.tags.GetEnumerator()) { $groupTags[[string]$entry.Key] = [string]$entry.Value }
        }
        $groupTags['udpi30TestRun'] = $marker
        $groupBody = [ordered]@{ tags = $groupTags } | ConvertTo-Json -Compress -Depth 5
        $groupMark = Invoke-TrackedPhase -Name "ownership-mark-azure-$($resourceGroup.name)" -Arguments @('rest', '--method', 'patch', '--url', $groupUrl, '--body', $groupBody, '--output', 'none', '--only-show-errors') -WorkingDirectory $WorkingDirectory -LogDirectory $LogDirectory -EventLog $EventLog -CurrentExampleId $CurrentExampleId -PermissionFindings $PermissionFindings -CommandFilePath 'az' -CommandPrefixArguments @()
        Add-PhaseResult -Target $Ownership -Phase $groupMark
        if ($groupMark.status -ne 'Passed') {
            throw "Could not mark Azure resource group '$($resourceGroup.name)' with this run ID."
        }

        $groupReceipt = [ordered]@{
            id = $groupId
            name = [string]$groupInfo.name
            marker = $marker
            deploymentName = $deploymentName
            creationStatusCode = [string]$creationProof.statusCode
            resourceIds = @()
        }
        $Receipt.azureResourceGroups.Add($groupReceipt)
        Save-UdpOwnershipReceipt -Receipt $Receipt -Path $ReceiptPath

        $groupVerify = Invoke-TrackedPhase -Name "ownership-verify-azure-$($resourceGroup.name)" -Arguments @('rest', '--method', 'get', '--url', $groupUrl, '--query', '{id:id,marker:tags.udpi30TestRun}', '--output', 'json', '--only-show-errors') -WorkingDirectory $WorkingDirectory -LogDirectory $LogDirectory -EventLog $EventLog -CurrentExampleId $CurrentExampleId -PermissionFindings $PermissionFindings -CommandFilePath 'az' -CommandPrefixArguments @()
        if ($groupVerify.status -eq 'Passed') {
            try {
                $verifiedGroup = $groupVerify.stdout | ConvertFrom-Json -AsHashtable
                $groupProof = Test-UdpOwnershipSnapshot -ExpectedId $groupId -ActualId ([string]$verifiedGroup.id) -ExpectedMarker $marker -ActualMarker ([string]$verifiedGroup.marker)
                if (-not $groupProof.matches) {
                    $groupVerify.status = 'Failed'
                    $groupVerify['message'] = 'Azure resource-group ID or ownership marker did not match the capture receipt.'
                }
            }
            catch {
                $groupVerify.status = 'Failed'
                $groupVerify['message'] = "Azure resource-group marker response was not valid JSON: $($_.Exception.Message)"
            }
        }
        Add-PhaseResult -Target $Ownership -Phase $groupVerify
        if ($groupVerify.status -ne 'Passed') {
            throw (Get-UdpPhaseMessage -Phase $groupVerify -Fallback "Could not verify Azure resource group '$($resourceGroup.name)'.")
        }

        $resourceArguments = @('resource', 'list', '--resource-group', $resourceGroup.name, '--query', '[].id', '--output', 'tsv', '--only-show-errors')
        if ($SubscriptionId) { $resourceArguments += @('--subscription', $SubscriptionId) }
        $resourceCapture = Invoke-TrackedPhase -Name "ownership-capture-resources-$($resourceGroup.name)" -Arguments $resourceArguments -WorkingDirectory $WorkingDirectory -LogDirectory $LogDirectory -EventLog $EventLog -CurrentExampleId $CurrentExampleId -PermissionFindings $PermissionFindings -CommandFilePath 'az' -CommandPrefixArguments @()
        Add-PhaseResult -Target $Ownership -Phase $resourceCapture
        if ($resourceCapture.status -ne 'Passed') {
            throw "Could not capture resources in Azure resource group '$($resourceGroup.name)'."
        }
        $groupReceipt.resourceIds = @(Get-UdpNonEmptyLine -Text $resourceCapture.stdout | Sort-Object -Unique)
        Save-UdpOwnershipReceipt -Receipt $Receipt -Path $ReceiptPath
    }

    foreach ($entraResource in @($Staged.facts.resources | Where-Object platform -eq 'Entra')) {
        $noun = if ($entraResource.type -eq 'entra_groups') { 'group' } else { 'app' }
        $escapedName = $entraResource.name.Replace("'", "''")
        $entraCapture = Invoke-TrackedPhase -Name "ownership-capture-entra-$($entraResource.name)" -Arguments @('ad', $noun, 'list', '--filter', "displayName eq '$escapedName'", '--query', '[].{id:id}', '--output', 'json', '--only-show-errors') -WorkingDirectory $WorkingDirectory -LogDirectory $LogDirectory -EventLog $EventLog -CurrentExampleId $CurrentExampleId -PermissionFindings $PermissionFindings -CommandFilePath 'az' -CommandPrefixArguments @()
        $entraObjects = @()
        if ($entraCapture.status -eq 'Passed') {
            try {
                $entraObjects = @($entraCapture.stdout | ConvertFrom-Json -AsHashtable)
                if ($entraObjects.Count -ne 1 -or [string]::IsNullOrWhiteSpace([string]$entraObjects[0].id)) {
                    throw "Expected exactly one object; found $($entraObjects.Count)."
                }
            }
            catch {
                $entraCapture.status = 'Failed'
                $entraCapture['message'] = "Entra ownership response was invalid: $($_.Exception.Message)"
            }
        }
        Add-PhaseResult -Target $Ownership -Phase $entraCapture
        if ($entraCapture.status -ne 'Passed') {
            throw (Get-UdpPhaseMessage -Phase $entraCapture -Fallback "Could not capture Entra object '$($entraResource.name)'.")
        }

        $objectId = [string]$entraObjects[0].id
        $graphKind = if ($entraResource.type -eq 'entra_groups') { 'groups' } else { 'applications' }
        $markerProperty = if ($entraResource.type -eq 'entra_groups') { 'description' } else { 'notes' }
        $objectUrl = "$graphResource/v1.0/$graphKind/$objectId"
        $objectBody = @{ $markerProperty = $marker } | ConvertTo-Json -Compress
        $entraMark = Invoke-TrackedPhase -Name "ownership-mark-entra-$($entraResource.name)" -Arguments @('rest', '--method', 'patch', '--url', $objectUrl, '--body', $objectBody, '--output', 'none', '--only-show-errors') -WorkingDirectory $WorkingDirectory -LogDirectory $LogDirectory -EventLog $EventLog -CurrentExampleId $CurrentExampleId -PermissionFindings $PermissionFindings -CommandFilePath 'az' -CommandPrefixArguments @()
        Add-PhaseResult -Target $Ownership -Phase $entraMark
        if ($entraMark.status -ne 'Passed') {
            throw "Could not mark Entra object '$($entraResource.name)' with this run ID."
        }

        $entraReceipt = [ordered]@{ id = $objectId; name = [string]$entraResource.name; kind = $graphKind; markerProperty = $markerProperty; marker = $marker }
        $Receipt.entraObjects.Add($entraReceipt)
        Save-UdpOwnershipReceipt -Receipt $Receipt -Path $ReceiptPath

        $markerQuery = if ($markerProperty -eq 'description') { '{id:id,displayName:displayName,marker:description}' } else { '{id:id,displayName:displayName,marker:notes}' }
        $entraVerify = Invoke-TrackedPhase -Name "ownership-verify-entra-$($entraResource.name)" -Arguments @('rest', '--method', 'get', '--url', $objectUrl, '--query', $markerQuery, '--output', 'json', '--only-show-errors') -WorkingDirectory $WorkingDirectory -LogDirectory $LogDirectory -EventLog $EventLog -CurrentExampleId $CurrentExampleId -PermissionFindings $PermissionFindings -CommandFilePath 'az' -CommandPrefixArguments @()
        if ($entraVerify.status -eq 'Passed') {
            try {
                $verifiedObject = $entraVerify.stdout | ConvertFrom-Json -AsHashtable
                $entraProof = Test-UdpOwnershipSnapshot -ExpectedId $objectId -ActualId ([string]$verifiedObject.id) -ExpectedMarker $marker -ActualMarker ([string]$verifiedObject.marker)
                if (-not $entraProof.matches) {
                    $entraVerify.status = 'Failed'
                    $entraVerify['message'] = 'Entra object ID or ownership marker did not match the capture receipt.'
                }
            }
            catch {
                $entraVerify.status = 'Failed'
                $entraVerify['message'] = "Entra marker response was not valid JSON: $($_.Exception.Message)"
            }
        }
        Add-PhaseResult -Target $Ownership -Phase $entraVerify
        if ($entraVerify.status -ne 'Passed') {
            throw (Get-UdpPhaseMessage -Phase $entraVerify -Fallback "Could not verify Entra object '$($entraResource.name)'.")
        }
    }

    Save-UdpOwnershipReceipt -Receipt $Receipt -Path $ReceiptPath
}

function Invoke-UdpDeploymentOwnershipCleanup {
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$Receipt,
        [Parameter(Mandatory)][string]$RunId,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$LogDirectory,
        [Parameter(Mandatory)][string]$EventLog,
        [Parameter(Mandatory)][string]$CurrentExampleId,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$PermissionFindings,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$Cleanup,
        [string]$SubscriptionId
    )

    $expectedMarker = "udpi30-test-run:$RunId"
    if ($Receipt.runId -ne $RunId -or $Receipt.marker -ne $expectedMarker) {
        Add-PhaseResult -Target $Cleanup -Phase (Get-UdpLocalPhase -Name 'cleanup-ownership-receipt' -Status Failed -Message 'Ownership receipt does not belong to this run.')
        return $false
    }

    $success = $true
    $fabricResource = 'https://api.fabric.microsoft.com'
    $graphResource = 'https://graph.microsoft.com'
    $managementEndpoint = 'https://management.azure.com'

    if ($Receipt.workspace) {
        $workspaceUrl = "$fabricResource/v1/workspaces/$($Receipt.workspace.id)"
        $workspaceVerify = Invoke-TrackedPhase -Name 'cleanup-verify-fabric-workspace' -Arguments @('rest', '--method', 'get', '--url', $workspaceUrl, '--resource', $fabricResource, '--query', '{id:id,description:description}', '--output', 'json', '--only-show-errors') -WorkingDirectory $WorkingDirectory -LogDirectory $LogDirectory -EventLog $EventLog -CurrentExampleId $CurrentExampleId -PermissionFindings $PermissionFindings -CommandFilePath 'az' -CommandPrefixArguments @()
        $workspaceItems = $null
        if ($workspaceVerify.status -eq 'Passed') {
            $workspaceItems = Invoke-TrackedPhase -Name 'cleanup-verify-fabric-items' -Arguments @('rest', '--method', 'get', '--url', "$workspaceUrl/items", '--resource', $fabricResource, '--query', 'value[].id', '--output', 'tsv', '--only-show-errors') -WorkingDirectory $WorkingDirectory -LogDirectory $LogDirectory -EventLog $EventLog -CurrentExampleId $CurrentExampleId -PermissionFindings $PermissionFindings -CommandFilePath 'az' -CommandPrefixArguments @()
        }
        if ($workspaceVerify.status -eq 'Passed' -and $workspaceItems.status -eq 'Passed') {
            try {
                $workspaceInfo = $workspaceVerify.stdout | ConvertFrom-Json -AsHashtable
                $workspaceProof = Test-UdpOwnershipSnapshot -ExpectedId ([string]$Receipt.workspace.id) -ActualId ([string]$workspaceInfo.id) -ExpectedMarker ([string]$Receipt.workspace.marker) -ActualMarker ([string]$workspaceInfo.description) -ExpectedChildIds @($Receipt.workspace.itemIds) -ActualChildIds @(Get-UdpNonEmptyLine -Text $workspaceItems.stdout)
                if (-not $workspaceProof.matches) {
                    $workspaceVerify.status = 'Failed'
                    $workspaceVerify['message'] = "Fabric cleanup ownership drift detected; unexpected items: $($workspaceProof.unexpectedChildIds -join ', ')."
                }
            }
            catch {
                $workspaceVerify.status = 'Failed'
                $workspaceVerify['message'] = "Fabric cleanup ownership response was invalid: $($_.Exception.Message)"
            }
        }
        Add-PhaseResult -Target $Cleanup -Phase $workspaceVerify
        if ($workspaceItems) { Add-PhaseResult -Target $Cleanup -Phase $workspaceItems }
        if ($workspaceVerify.status -eq 'Passed' -and $workspaceItems.status -eq 'Passed') {
            $workspaceDelete = Invoke-TrackedPhase -Name 'cleanup-delete-fabric-workspace' -Arguments @('rest', '--method', 'delete', '--url', $workspaceUrl, '--resource', $fabricResource, '--output', 'none', '--only-show-errors') -WorkingDirectory $WorkingDirectory -LogDirectory $LogDirectory -EventLog $EventLog -CurrentExampleId $CurrentExampleId -PermissionFindings $PermissionFindings -CommandFilePath 'az' -CommandPrefixArguments @()
            Add-PhaseResult -Target $Cleanup -Phase $workspaceDelete
            if ($workspaceDelete.status -ne 'Passed') { $success = $false }
        }
        else {
            $success = $false
        }
    }

    foreach ($group in @($Receipt.azureResourceGroups)) {
        $groupUrl = "$managementEndpoint$($group.id)?api-version=2021-04-01"
        $groupVerify = Invoke-TrackedPhase -Name "cleanup-verify-azure-$($group.name)" -Arguments @('rest', '--method', 'get', '--url', $groupUrl, '--query', '{id:id,marker:tags.udpi30TestRun}', '--output', 'json', '--only-show-errors') -WorkingDirectory $WorkingDirectory -LogDirectory $LogDirectory -EventLog $EventLog -CurrentExampleId $CurrentExampleId -PermissionFindings $PermissionFindings -CommandFilePath 'az' -CommandPrefixArguments @()
        $resourceVerify = $null
        if ($groupVerify.status -eq 'Passed') {
            $resourceArguments = @('resource', 'list', '--resource-group', $group.name, '--query', '[].id', '--output', 'tsv', '--only-show-errors')
            if ($SubscriptionId) { $resourceArguments += @('--subscription', $SubscriptionId) }
            $resourceVerify = Invoke-TrackedPhase -Name "cleanup-verify-resources-$($group.name)" -Arguments $resourceArguments -WorkingDirectory $WorkingDirectory -LogDirectory $LogDirectory -EventLog $EventLog -CurrentExampleId $CurrentExampleId -PermissionFindings $PermissionFindings -CommandFilePath 'az' -CommandPrefixArguments @()
        }
        if ($groupVerify.status -eq 'Passed' -and $resourceVerify.status -eq 'Passed') {
            try {
                $groupInfo = $groupVerify.stdout | ConvertFrom-Json -AsHashtable
                $groupProof = Test-UdpOwnershipSnapshot -ExpectedId ([string]$group.id) -ActualId ([string]$groupInfo.id) -ExpectedMarker ([string]$group.marker) -ActualMarker ([string]$groupInfo.marker) -ExpectedChildIds @($group.resourceIds) -ActualChildIds @(Get-UdpNonEmptyLine -Text $resourceVerify.stdout)
                if (-not $groupProof.matches) {
                    $groupVerify.status = 'Failed'
                    $groupVerify['message'] = "Azure cleanup ownership drift detected; unexpected resources: $($groupProof.unexpectedChildIds -join ', ')."
                }
            }
            catch {
                $groupVerify.status = 'Failed'
                $groupVerify['message'] = "Azure cleanup ownership response was invalid: $($_.Exception.Message)"
            }
        }
        Add-PhaseResult -Target $Cleanup -Phase $groupVerify
        if ($resourceVerify) { Add-PhaseResult -Target $Cleanup -Phase $resourceVerify }
        if ($groupVerify.status -ne 'Passed' -or $resourceVerify.status -ne 'Passed') {
            $success = $false
            continue
        }

        $groupDelete = Invoke-TrackedPhase -Name "cleanup-delete-azure-$($group.name)" -Arguments @('rest', '--method', 'delete', '--url', $groupUrl, '--output', 'none', '--only-show-errors') -WorkingDirectory $WorkingDirectory -LogDirectory $LogDirectory -EventLog $EventLog -CurrentExampleId $CurrentExampleId -PermissionFindings $PermissionFindings -CommandFilePath 'az' -CommandPrefixArguments @()
        Add-PhaseResult -Target $Cleanup -Phase $groupDelete
        if ($groupDelete.status -ne 'Passed') {
            $success = $false
            continue
        }
        $waitArguments = @('group', 'wait', '--deleted', '--name', $group.name, '--interval', '10', '--timeout', '900')
        if ($SubscriptionId) { $waitArguments += @('--subscription', $SubscriptionId) }
        $waitGroup = Invoke-TrackedPhase -Name "cleanup-wait-$($group.name)" -Arguments $waitArguments -WorkingDirectory $WorkingDirectory -LogDirectory $LogDirectory -EventLog $EventLog -CurrentExampleId $CurrentExampleId -PermissionFindings $PermissionFindings -CommandFilePath 'az' -CommandPrefixArguments @()
        Add-PhaseResult -Target $Cleanup -Phase $waitGroup
        if ($waitGroup.status -ne 'Passed') { $success = $false }
    }

    foreach ($entraObject in @($Receipt.entraObjects)) {
        $objectUrl = "$graphResource/v1.0/$($entraObject.kind)/$($entraObject.id)"
        $markerQuery = if ($entraObject.markerProperty -eq 'description') { '{id:id,marker:description}' } else { '{id:id,marker:notes}' }
        $entraVerify = Invoke-TrackedPhase -Name "cleanup-verify-entra-$($entraObject.name)" -Arguments @('rest', '--method', 'get', '--url', $objectUrl, '--query', $markerQuery, '--output', 'json', '--only-show-errors') -WorkingDirectory $WorkingDirectory -LogDirectory $LogDirectory -EventLog $EventLog -CurrentExampleId $CurrentExampleId -PermissionFindings $PermissionFindings -CommandFilePath 'az' -CommandPrefixArguments @()
        if ($entraVerify.status -eq 'Passed') {
            try {
                $objectInfo = $entraVerify.stdout | ConvertFrom-Json -AsHashtable
                $entraProof = Test-UdpOwnershipSnapshot -ExpectedId ([string]$entraObject.id) -ActualId ([string]$objectInfo.id) -ExpectedMarker ([string]$entraObject.marker) -ActualMarker ([string]$objectInfo.marker)
                if (-not $entraProof.matches) {
                    $entraVerify.status = 'Failed'
                    $entraVerify['message'] = 'Entra cleanup ID or ownership marker did not match the capture receipt.'
                }
            }
            catch {
                $entraVerify.status = 'Failed'
                $entraVerify['message'] = "Entra cleanup ownership response was invalid: $($_.Exception.Message)"
            }
        }
        Add-PhaseResult -Target $Cleanup -Phase $entraVerify
        if ($entraVerify.status -ne 'Passed') {
            $success = $false
            continue
        }

        $entraDelete = Invoke-TrackedPhase -Name "cleanup-delete-entra-$($entraObject.name)" -Arguments @('rest', '--method', 'delete', '--url', $objectUrl, '--output', 'none', '--only-show-errors') -WorkingDirectory $WorkingDirectory -LogDirectory $LogDirectory -EventLog $EventLog -CurrentExampleId $CurrentExampleId -PermissionFindings $PermissionFindings -CommandFilePath 'az' -CommandPrefixArguments @()
        Add-PhaseResult -Target $Cleanup -Phase $entraDelete
        if ($entraDelete.status -ne 'Passed') { $success = $false }
    }

    return $success
}

function Get-EffectiveMode {
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$Example,
        [Parameter(Mandatory)][System.Collections.IDictionary]$Inputs
    )

    if ($Mode -eq 'Preflight') { return 'Preflight' }
    $allowMutation = $false
    if ($Inputs.exampleOverrides.Contains($Example.id) -and
        $Inputs.exampleOverrides[$Example.id].Contains('allowMutation')) {
        $allowMutation = [bool]$Inputs.exampleOverrides[$Example.id].allowMutation
    }
    if ($Example.supportLevel -in @('validation-only', 'contract-only') -and -not $allowMutation) {
        return 'Validate'
    }
    if ($Example.supportLevel -eq 'admin-plan-only') {
        if ($Mode -eq 'Validate') {
            return 'Validate'
        }
        if ($Mode -eq 'Deploy' -and $allowMutation -and $AllowTenantAdminChanges) {
            return 'Deploy'
        }
        return 'Plan'
    }
    return $Mode
}

function Test-PhasePassed {
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Phase)
    return $Phase.status -eq 'Passed'
}

if (-not $RunId) {
    $sourceId = if ($env:GITHUB_RUN_ID) { "gh$($env:GITHUB_RUN_ID)" }
        elseif ($env:BUILD_BUILDID) { "ado$($env:BUILD_BUILDID)" }
        else { "local$([DateTimeOffset]::UtcNow.ToString('yyyyMMddHHmmss'))" }
    $RunId = "$sourceId-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"
}
$RunId = $RunId -replace '[^A-Za-z0-9-]', '-'
if ($RunId -notmatch '[A-Za-z0-9]' -or $RunId.Length -gt 80) {
    throw 'RunId must contain an alphanumeric character and be no longer than 80 characters after normalization.'
}
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $RepositoryRoot 'deployment-test-artifacts'
}
$artifactRoot = Join-Path $OutputDirectory $RunId
$workRoot = Join-Path (Join-Path $PSScriptRoot '.work') $RunId
$eventLog = Join-Path $artifactRoot 'events.jsonl'
$summaryPath = Join-Path $artifactRoot 'summary.json'
$htmlPath = Join-Path $artifactRoot 'deployment-test-report.html'
$requirementsPath = Join-Path $artifactRoot 'preflight-inputs.json'
$permissionPath = Join-Path $artifactRoot 'permission-findings.json'
[void](New-Item -ItemType Directory -Path $artifactRoot -Force)
[void](New-Item -ItemType Directory -Path $workRoot -Force)

$startedAt = [DateTimeOffset]::UtcNow
$results = [System.Collections.Generic.List[object]]::new()
$permissionFindings = [System.Collections.Generic.List[object]]::new()
$allRequirements = [System.Collections.Generic.List[object]]::new()
$fatalError = $null

try {
    Write-UdpEvent -Path $eventLog -Level Information -Phase 'Suite' -Message 'Deployment test suite started.' -Data @{ runId = $RunId; mode = $Mode }
    $inventory = Read-UdpJson -Path $InventoryPath
    if ($inventory.examples.Count -ne 14) {
        throw "Inventory must contain exactly 14 examples; found $($inventory.examples.Count)."
    }
    $duplicateIds = @($inventory.examples | Group-Object id | Where-Object Count -ne 1)
    if ($duplicateIds.Count -gt 0) {
        throw "Inventory contains duplicate example IDs: $(($duplicateIds.Name) -join ', ')"
    }

    $inputs = Get-UdpDefaultInputs
    $inputBasePath = $RepositoryRoot
    if ($InputsPath) {
        $resolvedInputsPath = Resolve-UdpContainedPath -Root $RepositoryRoot -Candidate $InputsPath -RequireRelative
        $inputs = Merge-UdpInputs -Base $inputs -Overlay (Read-UdpJson -Path $resolvedInputsPath)
        $inputBasePath = Split-Path -Parent $resolvedInputsPath
    }

    $globalBlockers = [System.Collections.Generic.List[object]]::new()
    if (-not $harnessWhatIf) {
        if (-not (Get-Command $CliFilePath -ErrorAction SilentlyContinue)) {
            $globalBlockers.Add([ordered]@{ kind = 'tool'; name = $CliFilePath; reason = 'The udpi30 command entry point is unavailable.'; canGenerate = $true; supply = 'Install the udpi30 .NET global tool or pass -CliFilePath and -CliPrefixArguments.'; blocksMutation = $true })
        }
        if ($Mode -in @('Preflight', 'Plan', 'Deploy') -and -not (Get-Command az -ErrorAction SilentlyContinue)) {
            $globalBlockers.Add([ordered]@{ kind = 'tool'; name = 'az'; reason = 'Azure CLI is required for federated authentication and Azure provider operations.'; canGenerate = $true; supply = 'Install Azure CLI and authenticate through OIDC/workload identity.'; blocksMutation = $true })
        }
    }

    $selectedExamples = @($inventory.examples | Where-Object { $ExampleId.Count -eq 0 -or $_.id -in $ExampleId } | Sort-Object id)
    foreach ($example in $selectedExamples) {
        $exampleStarted = [DateTimeOffset]::UtcNow
        $phases = [System.Collections.Generic.List[object]]::new()
        $verification = [System.Collections.Generic.List[object]]::new()
        $cleanup = [System.Collections.Generic.List[object]]::new()
        $ownership = [System.Collections.Generic.List[object]]::new()
        $effectiveMode = Get-EffectiveMode -Example $example -Inputs $inputs
        $target = [string]$example.defaultTarget
        $enabled = $true
        $adminRestoreManifest = $null
        $ownershipReceipt = $null
        $ownershipReceiptPath = $null
        if ($inputs.exampleOverrides.Contains($example.id)) {
            $override = $inputs.exampleOverrides[$example.id]
            if ($override.Contains('enabled')) { $enabled = [bool]$override.enabled }
            if ($override.Contains('target')) { $target = [string]$override.target }
            if ($override.Contains('adminRestoreManifest')) { $adminRestoreManifest = [string]$override.adminRestoreManifest }
        }
        if ($target -notmatch '^[A-Za-z][A-Za-z0-9_-]{0,63}$') {
            throw "Invalid deployment target '$target' for example $($example.id)."
        }

        $result = [ordered]@{
            id = [string]$example.id
            name = [string]$example.name
            sourcePath = [string]$example.path
            supportLevel = [string]$example.supportLevel
            requestedMode = $Mode
            effectiveMode = $effectiveMode
            target = $target
            planes = @($example.planes)
            status = 'NotRun'
            stagedManifest = $null
            nameReplacements = @{}
            resources = @()
            requirements = @()
            phases = $phases
            verification = $verification
            cleanup = $cleanup
            ownership = $ownership
            ownershipReceiptPath = $null
            cleanupAuthorized = $false
            startedAt = $exampleStarted.ToString('o')
            completedAt = $null
            durationSeconds = 0
        }

        if (-not $enabled) {
            $result.status = 'Skipped'
            $results.Add($result)
            continue
        }

        $mutationStarted = $false
        $cleanupAuthorized = $false
        $adminRestoreSnapshot = $null
        $subscriptionId = if ($inputs.values.Contains('subscription_id')) { [string]$inputs.values.subscription_id } else { '' }
        try {
            $staged = New-UdpStagedExample -Example $example -RepositoryRoot $RepositoryRoot -WorkRoot $workRoot -Inputs $inputs -RunSuffix $RunId -InputBasePath $inputBasePath
            $result.stagedManifest = $staged.manifest
            $result.nameReplacements = $staged.nameReplacements
            $result.resources = @($staged.facts.resources)
            $requirements = [System.Collections.Generic.List[object]]::new()
            $requirementsMode = if ($effectiveMode -eq 'Preflight') {
                if ($example.supportLevel -in @('validation-only', 'contract-only')) { 'Validate' }
                elseif ($example.supportLevel -eq 'admin-plan-only') { 'Plan' }
                else { 'Deploy' }
            }
            else { $effectiveMode }
            foreach ($requirement in (Get-UdpRequirements -Facts $staged.facts -Mode $requirementsMode)) {
                $requirements.Add($requirement)
                $allRequirements.Add([ordered]@{ exampleId = $example.id; example = $example.name; requirement = $requirement })
            }
            foreach ($blocker in $globalBlockers) {
                $requirements.Add($blocker)
                $allRequirements.Add([ordered]@{ exampleId = $example.id; example = $example.name; requirement = $blocker })
            }
            if ($example.executionMode -eq 'admin' -and $effectiveMode -eq 'Deploy') {
                $restorePath = if ($adminRestoreManifest) {
                    Resolve-UdpContainedPath -Root $inputBasePath -Candidate $adminRestoreManifest
                }
                else {
                    $null
                }
                if (-not $restorePath -or -not (Test-Path -LiteralPath $restorePath)) {
                    $restoreRequirement = [ordered]@{ kind = 'safety'; name = 'adminRestoreManifest'; reason = 'Tenant-wide changes require a tested restoration manifest.'; canGenerate = $false; supply = 'Set exampleOverrides.09.adminRestoreManifest and use -AllowTenantAdminChanges.'; blocksMutation = $true }
                    $requirements.Add($restoreRequirement)
                    $allRequirements.Add([ordered]@{ exampleId = $example.id; example = $example.name; requirement = $restoreRequirement })
                }
                else {
                    $restoreFacts = Get-UdpManifestFacts -ManifestPath $restorePath
                    $restoreCoverage = Compare-UdpTenantSettingCoverage `
                        -MutationSettingNames $staged.facts.tenantSettingNames `
                        -RestoreSettingNames $restoreFacts.tenantSettingNames `
                        -MutationFieldsBySetting $staged.facts.tenantSettingFields `
                        -RestoreFieldsBySetting $restoreFacts.tenantSettingFields
                    if (-not $restoreCoverage.matches) {
                        $coverageDetails = @(
                            if ($restoreCoverage.missingFromRestore.Count -gt 0) { "missing: $($restoreCoverage.missingFromRestore -join ', ')" }
                            if ($restoreCoverage.extraInRestore.Count -gt 0) { "extra: $($restoreCoverage.extraInRestore -join ', ')" }
                            if ($restoreCoverage.missingFieldsFromRestore.Count -gt 0) { "missing fields: $($restoreCoverage.missingFieldsFromRestore -join ', ')" }
                            if ($restoreCoverage.extraFieldsInRestore.Count -gt 0) { "extra fields: $($restoreCoverage.extraFieldsInRestore -join ', ')" }
                        ) -join '; '
                        $coverageRequirement = [ordered]@{
                            kind = 'safety'
                            name = 'adminRestoreCoverage'
                            reason = "The restore manifest must declare exactly the tenant settings and writable fields changed by this example ($coverageDetails)."
                            canGenerate = $false
                            supply = 'Update the restoration manifest so each admin.tenant_settings key and writable field exactly matches the staged mutation manifest.'
                            blocksMutation = $true
                        }
                        $requirements.Add($coverageRequirement)
                        $allRequirements.Add([ordered]@{ exampleId = $example.id; example = $example.name; requirement = $coverageRequirement })
                    }
                }
            }
            $result.requirements = @($requirements)
            $blockingRequirements = @($requirements | Where-Object blocksMutation)

            if ($effectiveMode -eq 'Preflight') {
                $result.status = if ($blockingRequirements.Count -eq 0) { 'Ready' } else { 'Blocked' }
                continue
            }
            if ($effectiveMode -in @('Plan', 'Deploy') -and $blockingRequirements.Count -gt 0) {
                $result.status = 'Blocked'
                continue
            }
            if ($harnessWhatIf) {
                $phases.Add([ordered]@{ name = $effectiveMode; status = 'Planned'; command = 'Suppressed by -WhatIf'; exitCode = $null; timedOut = $false; durationSeconds = 0; stdoutPath = $null; stderrPath = $null })
                $result.status = 'NotRun'
                continue
            }

            $exampleLogDirectory = Join-Path $artifactRoot "examples/$($example.id)-$($example.name)"
            $validateArguments = @('validate', '--file', $staged.manifest, '--target', $target, '--strict', '--skip-connection-check')
            $validate = Invoke-TrackedPhase -Name 'validate' -Arguments $validateArguments -WorkingDirectory $staged.directory -LogDirectory $exampleLogDirectory -EventLog $eventLog -CurrentExampleId $example.id -PermissionFindings $permissionFindings
            Add-PhaseResult -Target $phases -Phase $validate
            if (-not (Test-PhasePassed $validate)) {
                $result.status = 'Failed'
                continue
            }
            if ($effectiveMode -eq 'Validate') {
                $result.status = 'Passed'
                continue
            }

            $planArguments = if ($example.executionMode -eq 'admin') {
                @('admin', 'plan', '--file', $staged.manifest)
            }
            else {
                @('plan', '--file', $staged.manifest, '--target', $target)
            }
            $plan = Invoke-TrackedPhase -Name 'plan' -Arguments $planArguments -WorkingDirectory $staged.directory -LogDirectory $exampleLogDirectory -EventLog $eventLog -CurrentExampleId $example.id -PermissionFindings $permissionFindings
            Add-PhaseResult -Target $phases -Phase $plan
            if (-not (Test-PhasePassed $plan)) {
                $result.status = 'Failed'
                continue
            }
            if ($effectiveMode -eq 'Plan') {
                $result.status = 'Passed'
                continue
            }

            $ownershipFailed = $false
            if ($example.executionMode -eq 'admin') {
                $restorePath = Resolve-UdpContainedPath -Root $inputBasePath -Candidate $adminRestoreManifest
                $restoreBaseline = Invoke-TrackedPhase -Name 'ownership-admin-restore-baseline' -Arguments @('admin', 'plan', '--file', $restorePath) -WorkingDirectory (Split-Path -Parent $restorePath) -LogDirectory $exampleLogDirectory -EventLog $eventLog -CurrentExampleId $example.id -PermissionFindings $permissionFindings
                if (-not (Test-UdpAdminPlanConverged -Phase $restoreBaseline)) {
                    $restoreBaseline.status = 'Failed'
                    $ownershipFailed = $true
                }
                Add-PhaseResult -Target $ownership -Phase $restoreBaseline
                if (-not $ownershipFailed) {
                    $snapshotRead = Invoke-UdpTenantSettingsRead -Name 'ownership-admin-live-snapshot' -WorkingDirectory $staged.directory -EventLog $eventLog -CurrentExampleId $example.id -PermissionFindings $permissionFindings
                    Add-PhaseResult -Target $ownership -Phase $snapshotRead
                    if ($snapshotRead.status -ne 'Passed') {
                        $ownershipFailed = $true
                    }
                    else {
                        try {
                            $adminRestoreSnapshot = New-UdpTenantSettingSnapshot -LiveSettings @($snapshotRead.tenantSettings) -FieldsBySetting $staged.facts.tenantSettingFields
                            $snapshotFingerprint = Get-UdpTenantSettingSnapshotFingerprint -Snapshot $adminRestoreSnapshot
                            Add-PhaseResult -Target $ownership -Phase (Get-UdpLocalPhase -Name 'ownership-admin-snapshot-proof' -Status Passed -Message "Captured complete live values for $($adminRestoreSnapshot.Count) settings (SHA-256: $snapshotFingerprint); values are not published.")
                        }
                        catch {
                            $ownershipFailed = $true
                            Add-PhaseResult -Target $ownership -Phase (Get-UdpLocalPhase -Name 'ownership-admin-snapshot-proof' -Status Failed -Message $_.Exception.Message)
                        }
                    }
                }
            }
            else {
                $workspaceCheck = Invoke-TrackedPhase -Name 'ownership-fabric-workspace-absent' -Arguments @('status', '--file', $staged.manifest, '--target', $target) -WorkingDirectory $staged.directory -LogDirectory $exampleLogDirectory -EventLog $eventLog -CurrentExampleId $example.id -PermissionFindings $permissionFindings
                if ($workspaceCheck.status -ne 'Passed' -or $workspaceCheck.stdout -notmatch '(?i)Workspace not found') {
                    $workspaceCheck.status = 'Failed'
                    $ownershipFailed = $true
                }
                Add-PhaseResult -Target $ownership -Phase $workspaceCheck

                foreach ($resourceGroup in @($staged.facts.resources | Where-Object type -eq 'azure_resource_groups')) {
                    $groupArguments = @('group', 'exists', '--name', $resourceGroup.name)
                    if ($subscriptionId) { $groupArguments += @('--subscription', $subscriptionId) }
                    $groupCheck = Invoke-TrackedPhase -Name "ownership-azure-$($resourceGroup.name)-absent" -Arguments $groupArguments -WorkingDirectory $staged.directory -LogDirectory $exampleLogDirectory -EventLog $eventLog -CurrentExampleId $example.id -PermissionFindings $permissionFindings -CommandFilePath 'az' -CommandPrefixArguments @()
                    if ($groupCheck.status -ne 'Passed' -or $groupCheck.stdout.Trim() -ne 'false') {
                        $groupCheck.status = 'Failed'
                        $ownershipFailed = $true
                    }
                    Add-PhaseResult -Target $ownership -Phase $groupCheck
                }

                foreach ($entraResource in @($staged.facts.resources | Where-Object platform -eq 'Entra')) {
                    $noun = if ($entraResource.type -eq 'entra_groups') { 'group' } else { 'app' }
                    $escapedName = $entraResource.name.Replace("'", "''")
                    $entraCheck = Invoke-TrackedPhase -Name "ownership-entra-$($entraResource.name)-absent" -Arguments @('ad', $noun, 'list', '--filter', "displayName eq '$escapedName'", '--query', 'length(@)', '--output', 'tsv') -WorkingDirectory $staged.directory -LogDirectory $exampleLogDirectory -EventLog $eventLog -CurrentExampleId $example.id -PermissionFindings $permissionFindings -CommandFilePath 'az' -CommandPrefixArguments @()
                    if ($entraCheck.status -ne 'Passed' -or $entraCheck.stdout.Trim() -ne '0') {
                        $entraCheck.status = 'Failed'
                        $ownershipFailed = $true
                    }
                    Add-PhaseResult -Target $ownership -Phase $entraCheck
                }
            }
            if ($ownershipFailed) {
                $ownershipRequirement = [ordered]@{
                    kind = 'safety'
                    name = 'resourceOwnership'
                    reason = 'The harness could not prove that every mutation target is absent and owned by this run.'
                    canGenerate = $false
                    supply = 'Choose a new run ID or remove the colliding test resources after independently verifying ownership.'
                    blocksMutation = $true
                }
                $requirements.Add($ownershipRequirement)
                $allRequirements.Add([ordered]@{ exampleId = $example.id; example = $example.name; requirement = $ownershipRequirement })
                $result.requirements = @($requirements)
                $result.status = 'Blocked'
                continue
            }
            $cleanupAuthorized = $true
            $result.cleanupAuthorized = $true

            $deployArguments = if ($example.executionMode -eq 'admin') {
                @('admin', 'apply', '--file', $staged.manifest, '--auto-approve')
            }
            else {
                @('deploy', '--file', $staged.manifest, '--target', $target, '--auto-approve')
            }
            $mutationStarted = $true
            $deploy = Invoke-TrackedPhase -Name 'deploy' -Arguments $deployArguments -WorkingDirectory $staged.directory -LogDirectory $exampleLogDirectory -EventLog $eventLog -CurrentExampleId $example.id -PermissionFindings $permissionFindings
            Add-PhaseResult -Target $phases -Phase $deploy
            if (-not (Test-PhasePassed $deploy)) {
                $result.status = 'Failed'
                continue
            }

            if ($example.executionMode -eq 'deployment') {
                $creationEvidenceMissing = [System.Collections.Generic.List[string]]::new()
                $workspaceCreationMatches = [regex]::Matches($deploy.stdout, '(?im)^\s*Creating workspace:\s*(?<name>.+?)\s*$')
                if ($workspaceCreationMatches.Count -ne 1) {
                    $creationEvidenceMissing.Add("Fabric workspace for target '$target'")
                }
                foreach ($entraResource in @($staged.facts.resources | Where-Object platform -eq 'Entra')) {
                    $entraKind = if ($entraResource.type -eq 'entra_groups') { 'group' } else { 'app' }
                    if ($deploy.stdout -notmatch "(?im)^\s*\+\s*Created Entra $($entraKind):\s*$([regex]::Escape($entraResource.name))\s*$") {
                        $creationEvidenceMissing.Add("Entra $entraKind '$($entraResource.name)'")
                    }
                }
                if ($creationEvidenceMissing.Count -gt 0) {
                    $creationProof = Get-UdpLocalPhase -Name 'ownership-created-by-run' -Status Failed -Message "Deploy did not prove these absent targets were created by this run: $($creationEvidenceMissing -join ', '). Cleanup was refused."
                    Add-PhaseResult -Target $ownership -Phase $creationProof
                    throw $creationProof.message
                }
                Add-PhaseResult -Target $ownership -Phase (Get-UdpLocalPhase -Name 'ownership-created-by-run' -Status Passed -Message 'Deploy output confirms all Fabric workspaces and Entra objects were created by this run; Azure resource groups still require provider-operation proof.')

                $ownershipReceiptPath = Join-Path $exampleLogDirectory 'ownership-receipt.json'
                $ownershipReceipt = [ordered]@{
                    schemaVersion = '1.0'
                    runId = $RunId
                    marker = "udpi30-test-run:$RunId"
                    workspace = $null
                    azureResourceGroups = [System.Collections.Generic.List[object]]::new()
                    entraObjects = [System.Collections.Generic.List[object]]::new()
                }
                $result.ownershipReceiptPath = $ownershipReceiptPath
                Save-UdpOwnershipReceipt -Receipt $ownershipReceipt -Path $ownershipReceiptPath
                Set-UdpDeploymentOwnershipReceipt -Staged $staged -Target $target -Receipt $ownershipReceipt -ReceiptPath $ownershipReceiptPath -WorkingDirectory $staged.directory -LogDirectory $exampleLogDirectory -EventLog $eventLog -CurrentExampleId $example.id -PermissionFindings $permissionFindings -Ownership $ownership -SubscriptionId $subscriptionId
            }

            $verificationFailed = $false
            if ($example.executionMode -eq 'deployment') {
                $fabricResources = @($staged.facts.resources | Where-Object platform -eq 'Fabric')
                if ($fabricResources.Count -gt 0) {
                    $statusPhase = Invoke-TrackedPhase -Name 'verify-status' -Arguments @('status', '--file', $staged.manifest, '--target', $target) -WorkingDirectory $staged.directory -LogDirectory $exampleLogDirectory -EventLog $eventLog -CurrentExampleId $example.id -PermissionFindings $permissionFindings
                    $fabricMissing = @($fabricResources | Where-Object { $statusPhase.stdout -notmatch "(?m)$([regex]::Escape($_.name)).*deployed" })
                    if ($statusPhase.status -ne 'Passed' -or $statusPhase.stdout -match '(?i)workspace not found' -or $fabricMissing.Count -gt 0) {
                        $statusPhase.status = 'Failed'
                        $verificationFailed = $true
                    }
                    Add-PhaseResult -Target $verification -Phase $statusPhase

                    $driftPhase = Invoke-TrackedPhase -Name 'verify-drift' -Arguments @('drift', '--file', $staged.manifest, '--target', $target) -WorkingDirectory $staged.directory -LogDirectory $exampleLogDirectory -EventLog $eventLog -CurrentExampleId $example.id -PermissionFindings $permissionFindings
                    if ($driftPhase.status -ne 'Passed' -or $driftPhase.stdout -notmatch '(?i)No drift detected') {
                        $driftPhase.status = 'Failed'
                        $verificationFailed = $true
                    }
                    Add-PhaseResult -Target $verification -Phase $driftPhase
                }

                foreach ($azureResource in @($staged.facts.resources | Where-Object platform -eq 'Azure')) {
                    $azureVerification = Invoke-UdpAzureVerification -Resource $azureResource -WorkingDirectory $staged.directory -LogDirectory $exampleLogDirectory -SubscriptionId $subscriptionId
                    Add-PhaseResult -Target $verification -Phase $azureVerification
                    if ($azureVerification.status -ne 'Passed') { $verificationFailed = $true }
                    if ($azureResource.type -eq 'azure_purview_accounts') {
                        $purviewVerification = Invoke-UdpPurviewVerification -Resource $azureResource -WorkingDirectory $staged.directory -LogDirectory $exampleLogDirectory
                        Add-PhaseResult -Target $verification -Phase $purviewVerification
                        $purviewFinding = Get-UdpPermissionFinding -Text "$($purviewVerification.stdout)`n$($purviewVerification.stderr)" -ExampleId $example.id -Phase $purviewVerification.name
                        if ($purviewFinding) { $permissionFindings.Add($purviewFinding) }
                        if ($purviewVerification.status -ne 'Passed') { $verificationFailed = $true }
                    }
                }
                foreach ($entraResource in @($staged.facts.resources | Where-Object platform -eq 'Entra')) {
                    $entraVerification = Invoke-UdpEntraVerification -Resource $entraResource -WorkingDirectory $staged.directory -LogDirectory $exampleLogDirectory
                    Add-PhaseResult -Target $verification -Phase $entraVerification
                    if ($entraVerification.status -ne 'Passed') { $verificationFailed = $true }
                }
            }
            else {
                $adminVerify = Wait-UdpAdminPlanConvergence -Name 'verify-admin-plan-converged' -TimeoutSeconds $AdminPropagationTimeoutSeconds -PollIntervalSeconds $AdminPropagationPollSeconds -InvokePlan {
                    param($attempt)
                    Invoke-TrackedPhase -Name "verify-admin-plan-attempt-$attempt" -Arguments @('admin', 'plan', '--file', $staged.manifest) -WorkingDirectory $staged.directory -LogDirectory $exampleLogDirectory -EventLog $eventLog -CurrentExampleId $example.id -PermissionFindings $permissionFindings
                }
                Add-PhaseResult -Target $verification -Phase $adminVerify
                if ($adminVerify.status -ne 'Passed') { $verificationFailed = $true }
            }
            $result.status = if ($verificationFailed) { 'Failed' } else { 'Passed' }
        }
        catch {
            $result.status = 'Failed'
            $phases.Add([ordered]@{ name = 'harness'; status = 'Failed'; command = $null; exitCode = 3; timedOut = $false; durationSeconds = 0; stdoutPath = $null; stderrPath = $null; message = $_.Exception.Message })
            Write-UdpEvent -Path $eventLog -Level Error -Phase 'Harness' -Message $_.Exception.Message -Data @{ exampleId = $example.id }
        }
        finally {
            $mustRestoreOrCleanup = $example.executionMode -eq 'admin' -or -not $KeepResources
            if ($mutationStarted -and $cleanupAuthorized -and $mustRestoreOrCleanup -and $result.stagedManifest) {
                $exampleLogDirectory = Join-Path $artifactRoot "examples/$($example.id)-$($example.name)"
                try {
                    if ($example.executionMode -eq 'admin') {
                        if (-not $adminRestoreSnapshot) {
                            Add-PhaseResult -Target $cleanup -Phase (Get-UdpLocalPhase -Name 'cleanup-admin-restore-snapshot' -Status Failed -Message 'No complete live tenant-setting snapshot was captured; automated restoration is unavailable.')
                            $result.status = 'Failed'
                        }
                        else {
                            $restoreAttemptFailed = $false
                            $settingNames = @($adminRestoreSnapshot.Keys)
                            for ($index = 0; $index -lt $settingNames.Count; $index++) {
                                $settingName = [string]$settingNames[$index]
                                $restoreBodyDirectory = $null
                                try {
                                    $restoreBodyDirectory = New-UdpPrivateDirectory
                                    $restoreBodyPath = Join-Path $restoreBodyDirectory 'body.json'
                                    $restoreJson = $adminRestoreSnapshot[$settingName] | ConvertTo-Json -Depth 20 -Compress
                                    Set-UdpPrivateJsonFile -Path $restoreBodyPath -Json $restoreJson
                                    $escapedSettingName = [Uri]::EscapeDataString($settingName)
                                    $restore = Invoke-UdpPrivateTrackedPhase -Name "cleanup-admin-restore-$settingName" -Arguments @('rest', '--method', 'post', '--url', "https://api.fabric.microsoft.com/v1/admin/tenantsettings/$escapedSettingName/update", '--resource', 'https://api.fabric.microsoft.com', '--body', "@$restoreBodyPath", '--output', 'none', '--only-show-errors') -WorkingDirectory $staged.directory -EventLog $eventLog -CurrentExampleId $example.id -PermissionFindings $permissionFindings
                                    $restore.stdout = ''
                                    $restore.stderr = ''
                                    Add-PhaseResult -Target $cleanup -Phase $restore
                                    if ($restore.status -ne 'Passed') { $restoreAttemptFailed = $true }
                                }
                                catch {
                                    $restoreAttemptFailed = $true
                                    Add-PhaseResult -Target $cleanup -Phase (Get-UdpLocalPhase -Name "cleanup-admin-restore-$settingName" -Status Failed -Message $_.Exception.Message)
                                }
                                finally {
                                    if ($restoreBodyDirectory) {
                                        try {
                                            Remove-UdpPrivateDirectory -Path $restoreBodyDirectory
                                        }
                                        catch {
                                            $restoreAttemptFailed = $true
                                            Add-PhaseResult -Target $cleanup -Phase (Get-UdpLocalPhase -Name "cleanup-admin-private-data-$settingName" -Status Failed -Message $_.Exception.Message)
                                        }
                                    }
                                }
                                if ($index -lt $settingNames.Count - 1) { [Threading.Thread]::Sleep(2500) }
                            }

                            $restoreVerify = Wait-UdpTenantSettingSnapshotConvergence -Name 'cleanup-admin-restore-converged' -ExpectedSnapshot $adminRestoreSnapshot -TimeoutSeconds $AdminPropagationTimeoutSeconds -PollIntervalSeconds $AdminPropagationPollSeconds -InvokeRead {
                                param($attempt)
                                Invoke-UdpTenantSettingsRead -Name "cleanup-admin-restore-read-$attempt" -WorkingDirectory $staged.directory -EventLog $eventLog -CurrentExampleId $example.id -PermissionFindings $permissionFindings
                            }
                            Add-PhaseResult -Target $cleanup -Phase $restoreVerify
                            if ($restoreAttemptFailed -or $restoreVerify.status -ne 'Passed') {
                                $result.status = 'Failed'
                            }
                        }
                    }
                    else {
                        if (-not $ownershipReceipt) {
                            Add-PhaseResult -Target $cleanup -Phase (Get-UdpLocalPhase -Name 'cleanup-ownership-receipt' -Status Failed -Message 'No immutable ownership receipt was captured; cleanup was refused.')
                            $result.status = 'Failed'
                        }
                        elseif (-not (Invoke-UdpDeploymentOwnershipCleanup -Receipt $ownershipReceipt -RunId $RunId -WorkingDirectory (Split-Path -Parent $result.stagedManifest) -LogDirectory $exampleLogDirectory -EventLog $eventLog -CurrentExampleId $example.id -PermissionFindings $permissionFindings -Cleanup $cleanup -SubscriptionId $subscriptionId)) {
                            $result.status = 'Failed'
                        }
                    }
                }
                catch {
                    $result.status = 'Failed'
                    $cleanup.Add([ordered]@{ name = 'cleanup'; status = 'Failed'; command = $null; exitCode = 3; timedOut = $false; durationSeconds = 0; stdoutPath = $null; stderrPath = $null; message = $_.Exception.Message })
                }
            }
            $exampleCompleted = [DateTimeOffset]::UtcNow
            $result.completedAt = $exampleCompleted.ToString('o')
            $result.durationSeconds = [Math]::Round(($exampleCompleted - $exampleStarted).TotalSeconds, 3)
            $resultPath = Join-Path $artifactRoot "examples/$($example.id)-$($example.name)/result.json"
            [void](New-Item -ItemType Directory -Path (Split-Path -Parent $resultPath) -Force)
            if ($result.stagedManifest -and (Test-Path -LiteralPath $result.stagedManifest)) {
                Copy-Item -LiteralPath $result.stagedManifest -Destination (Join-Path (Split-Path -Parent $resultPath) 'staged-udp.yml') -Force
            }
            $result | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $resultPath -Encoding utf8
            $results.Add($result)
        }
    }
}
catch {
    $fatalError = $_.Exception.Message
    Write-UdpEvent -Path $eventLog -Level Error -Phase 'Suite' -Message $fatalError
}
finally {
    $completedAt = [DateTimeOffset]::UtcNow
    $counts = [ordered]@{}
    foreach ($status in @('Ready', 'Passed', 'Failed', 'Blocked', 'NotRun', 'Skipped')) {
        $counts[$status] = @($results | Where-Object status -eq $status).Count
    }
    $summary = [ordered]@{
        schemaVersion = '1.0'
        runId = $RunId
        mode = $Mode
        requestedExamples = @($ExampleId)
        startedAt = $startedAt.ToString('o')
        completedAt = $completedAt.ToString('o')
        durationSeconds = [Math]::Round(($completedAt - $startedAt).TotalSeconds, 3)
        fatalError = $fatalError
        counts = $counts
        results = @($results)
        permissionFindings = @($permissionFindings)
    }
    $summary | ConvertTo-Json -Depth 24 | Set-Content -LiteralPath $summaryPath -Encoding utf8
    @($allRequirements) | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $requirementsPath -Encoding utf8
    @($permissionFindings) | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $permissionPath -Encoding utf8
    New-UdpHtmlReport -Summary $summary -OutputPath $htmlPath
    Write-UdpEvent -Path $eventLog -Level Information -Phase 'Suite' -Message 'Deployment test suite completed.' -Data @{ report = $htmlPath; counts = $counts }
    if (-not $KeepResources -and (Test-Path -LiteralPath $workRoot)) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    Write-Host "HTML report: $htmlPath"
    Write-Host "JSON summary: $summaryPath"
}

if ($fatalError -or $counts.Failed -gt 0) { exit 1 }
if ($harnessWhatIf) { exit 0 }
if ($counts.Blocked -gt 0) { exit 2 }
exit 0
