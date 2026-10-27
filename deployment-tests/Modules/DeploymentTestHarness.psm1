#Requires -Version 7.2

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-UdpJson {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "JSON file not found: $Path"
    }

    return Get-Content -LiteralPath $Path -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable
}

function Test-UdpPlaceholder {
    [CmdletBinding()]
    param([AllowNull()][AllowEmptyString()][string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $true
    }

    return $Value -match '(?i)REPLACE[-_/ ]WITH|replace[/\\]|^0{8}-0{4}-0{4}-0{4}-0{12}$|/subscriptions/\.\.\.|^\$\(.+\)$'
}

function ConvertTo-UdpYamlScalar {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Value)

    return "'$(($Value -replace "'", "''"))'"
}

function Write-UdpEvent {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][ValidateSet('Debug', 'Information', 'Warning', 'Error')][string]$Level,
        [Parameter(Mandatory)][string]$Phase,
        [Parameter(Mandatory)][string]$Message,
        [System.Collections.IDictionary]$Data = @{}
    )

    $parent = Split-Path -Parent $Path
    if ($parent) {
        [void](New-Item -ItemType Directory -Path $parent -Force)
    }

    $entry = [ordered]@{
        timestamp = [DateTimeOffset]::UtcNow.ToString('o')
        level = $Level
        phase = $Phase
        message = $Message
        data = $Data
    }
    Add-Content -LiteralPath $Path -Value ($entry | ConvertTo-Json -Compress -Depth 12) -Encoding utf8
}

function Get-UdpDisplayCommand {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList = @()
    )

    $displayArguments = foreach ($argument in $ArgumentList) {
        if ($argument -match '^[A-Za-z0-9_./:=@+-]+$') {
            $argument
        }
        else {
            "'$(($argument -replace "'", "''"))'"
        }
    }
    return (@($FilePath) + $displayArguments) -join ' '
}

function Protect-UdpSensitiveText {
    [CmdletBinding()]
    param([AllowNull()][string]$Text)

    if ([string]::IsNullOrEmpty($Text)) { return $Text }
    foreach ($entry in [Environment]::GetEnvironmentVariables().GetEnumerator()) {
        if ([string]$entry.Key -match '(?i)SECRET|TOKEN|PASSWORD|PASSWD|API_KEY|WEBHOOK|CONNECTION_STRING|SAS' -and
            -not [string]::IsNullOrWhiteSpace([string]$entry.Value) -and
            ([string]$entry.Value).Length -ge 4) {
            $Text = $Text.Replace([string]$entry.Value, '[REDACTED]')
        }
    }
    $Text = $Text -replace '(?i)(sig|token|password|secret|client_secret|code)=([^&\s;]+)', '$1=[REDACTED]'
    $Text = $Text -replace '(?i)(AccountKey|SharedAccessKey|Password)=([^;\r\n]+)', '$1=[REDACTED]'
    return $Text
}

function Invoke-UdpProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$StdOutPath,
        [Parameter(Mandatory)][string]$StdErrPath,
        [ValidateRange(1, 86400)][int]$TimeoutSeconds = 1800,
        [System.Collections.IDictionary]$Environment = @{}
    )

    $start = [DateTimeOffset]::UtcNow
    $processInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $processInfo.FileName = $FilePath
    $processInfo.WorkingDirectory = $WorkingDirectory
    $processInfo.UseShellExecute = $false
    $processInfo.RedirectStandardOutput = $true
    $processInfo.RedirectStandardError = $true
    $processInfo.CreateNoWindow = $true
    foreach ($argument in $ArgumentList) {
        [void]$processInfo.ArgumentList.Add($argument)
    }
    foreach ($entry in $Environment.GetEnumerator()) {
        $processInfo.Environment[$entry.Key] = [string]$entry.Value
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $processInfo
    try {
        if (-not $process.Start()) {
            throw "Failed to start process: $FilePath"
        }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            try { $process.Kill($true) } catch { Write-Verbose "Could not terminate timed-out process: $_" }
            $timedOut = $true
            $exitCode = 124
        }
        else {
            $timedOut = $false
            $exitCode = $process.ExitCode
        }
        $stdout = Protect-UdpSensitiveText -Text $stdoutTask.GetAwaiter().GetResult()
        $stderr = Protect-UdpSensitiveText -Text $stderrTask.GetAwaiter().GetResult()
    }
    finally {
        $process.Dispose()
    }

    $stdout | Set-Content -LiteralPath $StdOutPath -Encoding utf8
    $stderr | Set-Content -LiteralPath $StdErrPath -Encoding utf8
    $end = [DateTimeOffset]::UtcNow

    return [ordered]@{
        command = Get-UdpDisplayCommand -FilePath $FilePath -ArgumentList $ArgumentList
        exitCode = $exitCode
        timedOut = $timedOut
        startedAt = $start.ToString('o')
        completedAt = $end.ToString('o')
        durationSeconds = [Math]::Round(($end - $start).TotalSeconds, 3)
        stdout = $stdout
        stderr = $stderr
        stdoutPath = $StdOutPath
        stderrPath = $StdErrPath
        succeeded = ($exitCode -eq 0 -and -not $timedOut)
    }
}

function Get-UdpCleanValue {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Value)

    $clean = $Value.Trim()
    if (($clean.StartsWith('"') -and $clean.EndsWith('"')) -or
        ($clean.StartsWith("'") -and $clean.EndsWith("'"))) {
        return $clean.Substring(1, $clean.Length - 2)
    }
    return ($clean -split '\s+#', 2)[0].Trim()
}

function Compare-UdpTenantSettingCoverage {
    [CmdletBinding()]
    param(
        [AllowEmptyCollection()][string[]]$MutationSettingNames = @(),
        [AllowEmptyCollection()][string[]]$RestoreSettingNames = @(),
        [System.Collections.IDictionary]$MutationFieldsBySetting = @{},
        [System.Collections.IDictionary]$RestoreFieldsBySetting = @{}
    )

    $mutationSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $restoreSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($name in $MutationSettingNames) { [void]$mutationSet.Add($name) }
    foreach ($name in $RestoreSettingNames) { [void]$restoreSet.Add($name) }

    $missingFromRestore = @($mutationSet | Where-Object { -not $restoreSet.Contains($_) } | Sort-Object)
    $extraInRestore = @($restoreSet | Where-Object { -not $mutationSet.Contains($_) } | Sort-Object)
    $missingFieldsFromRestore = [System.Collections.Generic.List[string]]::new()
    $extraFieldsInRestore = [System.Collections.Generic.List[string]]::new()
    foreach ($settingName in @($mutationSet | Where-Object { $restoreSet.Contains($_) } | Sort-Object)) {
        $mutationFields = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $restoreFields = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        if ($MutationFieldsBySetting.Contains($settingName)) {
            foreach ($field in @($MutationFieldsBySetting[$settingName])) { [void]$mutationFields.Add([string]$field) }
        }
        if ($RestoreFieldsBySetting.Contains($settingName)) {
            foreach ($field in @($RestoreFieldsBySetting[$settingName])) { [void]$restoreFields.Add([string]$field) }
        }
        foreach ($field in @($mutationFields | Where-Object { -not $restoreFields.Contains($_) } | Sort-Object)) {
            $missingFieldsFromRestore.Add("$settingName.$field")
        }
        foreach ($field in @($restoreFields | Where-Object { -not $mutationFields.Contains($_) } | Sort-Object)) {
            $extraFieldsInRestore.Add("$settingName.$field")
        }
    }
    return [ordered]@{
        matches = $missingFromRestore.Count -eq 0 -and
            $extraInRestore.Count -eq 0 -and
            $missingFieldsFromRestore.Count -eq 0 -and
            $extraFieldsInRestore.Count -eq 0
        missingFromRestore = $missingFromRestore
        extraInRestore = $extraInRestore
        missingFieldsFromRestore = @($missingFieldsFromRestore)
        extraFieldsInRestore = @($extraFieldsInRestore)
    }
}

function ConvertTo-UdpTenantSettingGroups {
    param([AllowNull()][object]$Value)

    $groups = @(
        foreach ($group in @($Value)) {
            if ($group -is [System.Collections.IDictionary]) {
                [ordered]@{
                    graphId = [string]$group.graphId
                    name = [string]$group.name
                }
            }
        }
    )
    return @($groups | Sort-Object -Property @{ Expression = { $_.graphId } }, @{ Expression = { $_.name } })
}

function ConvertTo-UdpTenantSettingProperties {
    param([AllowNull()][object]$Value)

    $properties = @(
        foreach ($property in @($Value)) {
            if ($property -is [System.Collections.IDictionary]) {
                [ordered]@{
                    name = [string]$property.name
                    value = [string]$property.value
                    type = [string]$property.type
                }
            }
        }
    )
    return @($properties | Sort-Object -Property @{ Expression = { $_.name } }, @{ Expression = { $_.type } }, @{ Expression = { $_.value } })
}

function New-UdpTenantSettingSnapshot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$LiveSettings,
        [Parameter(Mandatory)][System.Collections.IDictionary]$FieldsBySetting
    )

    $supportedFields = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($supportedField in @('enabled', 'delegate_to_capacity', 'delegate_to_domain', 'delegate_to_workspace', 'enabled_security_groups', 'excluded_security_groups', 'properties')) {
        [void]$supportedFields.Add($supportedField)
    }
    $snapshot = [ordered]@{}
    foreach ($settingName in @($FieldsBySetting.Keys | Sort-Object)) {
        $matches = @($LiveSettings | Where-Object {
            $_ -is [System.Collections.IDictionary] -and
            [string]::Equals([string]$_.settingName, [string]$settingName, [StringComparison]::Ordinal)
        })
        if ($matches.Count -ne 1) {
            throw "Expected exactly one live tenant setting '$settingName'; found $($matches.Count)."
        }

        $live = $matches[0]
        if (-not $live.Contains('enabled')) {
            throw "Live tenant setting '$settingName' did not include enabled."
        }
        $fields = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($field in @($FieldsBySetting[$settingName])) {
            if (-not $supportedFields.Contains([string]$field)) {
                throw "Tenant setting '$settingName' contains unsupported writable field '$field'."
            }
            [void]$fields.Add([string]$field)
        }

        $body = [ordered]@{ enabled = [bool]$live.enabled }
        foreach ($field in @(
            @{ Manifest = 'delegate_to_capacity'; Api = 'delegateToCapacity' }
            @{ Manifest = 'delegate_to_domain'; Api = 'delegateToDomain' }
            @{ Manifest = 'delegate_to_workspace'; Api = 'delegateToWorkspace' }
        )) {
            if ($fields.Contains($field.Manifest)) {
                $body[$field.Api] = if ($live.Contains($field.Api)) { [bool]$live[$field.Api] } else { $false }
            }
        }
        if ($fields.Contains('enabled_security_groups')) {
            $body['enabledSecurityGroups'] = @(ConvertTo-UdpTenantSettingGroups -Value $(if ($live.Contains('enabledSecurityGroups')) { $live.enabledSecurityGroups } else { @() }))
        }
        if ($fields.Contains('excluded_security_groups')) {
            $body['excludedSecurityGroups'] = @(ConvertTo-UdpTenantSettingGroups -Value $(if ($live.Contains('excludedSecurityGroups')) { $live.excludedSecurityGroups } else { @() }))
        }
        if ($fields.Contains('properties')) {
            $body['properties'] = @(ConvertTo-UdpTenantSettingProperties -Value $(if ($live.Contains('properties')) { $live.properties } else { @() }))
        }
        $snapshot[[string]$settingName] = $body
    }
    return $snapshot
}

function Compare-UdpTenantSettingSnapshot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$ExpectedSnapshot,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$LiveSettings
    )

    $apiToManifest = @{
        enabled = 'enabled'
        delegateToCapacity = 'delegate_to_capacity'
        delegateToDomain = 'delegate_to_domain'
        delegateToWorkspace = 'delegate_to_workspace'
        enabledSecurityGroups = 'enabled_security_groups'
        excludedSecurityGroups = 'excluded_security_groups'
        properties = 'properties'
    }
    $fieldsBySetting = [ordered]@{}
    foreach ($settingName in $ExpectedSnapshot.Keys) {
        $fields = [System.Collections.Generic.List[string]]::new()
        foreach ($apiField in $ExpectedSnapshot[$settingName].Keys) {
            if (-not $apiToManifest.Contains($apiField)) {
                throw "Snapshot for '$settingName' contains unsupported API field '$apiField'."
            }
            $fields.Add($apiToManifest[$apiField])
        }
        $fieldsBySetting[$settingName] = $fields
    }

    $actualSnapshot = New-UdpTenantSettingSnapshot -LiveSettings $LiveSettings -FieldsBySetting $fieldsBySetting
    $mismatchedSettingNames = [System.Collections.Generic.List[string]]::new()
    foreach ($settingName in $ExpectedSnapshot.Keys) {
        $expectedJson = $ExpectedSnapshot[$settingName] | ConvertTo-Json -Depth 20 -Compress
        $actualJson = $actualSnapshot[$settingName] | ConvertTo-Json -Depth 20 -Compress
        if (-not [string]::Equals($expectedJson, $actualJson, [StringComparison]::Ordinal)) {
            $mismatchedSettingNames.Add([string]$settingName)
        }
    }
    return [ordered]@{
        matches = $mismatchedSettingNames.Count -eq 0
        mismatchedSettingNames = @($mismatchedSettingNames)
    }
}

function Get-UdpTenantSettingSnapshotFingerprint {
    [CmdletBinding()]
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Snapshot)

    $json = $Snapshot | ConvertTo-Json -Depth 20 -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($json)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Wait-UdpTenantSettingSnapshotConvergence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$ExpectedSnapshot,
        [Parameter(Mandatory)][scriptblock]$InvokeRead,
        [Parameter(Mandatory)][string]$Name,
        [ValidateRange(1, 1800)][int]$TimeoutSeconds = 900,
        [ValidateRange(0, 300)][int]$PollIntervalSeconds = 30,
        [scriptblock]$GetUtcNow = { [DateTimeOffset]::UtcNow },
        [scriptblock]$Delay = { param([int]$Seconds) if ($Seconds -gt 0) { Start-Sleep -Seconds $Seconds } }
    )

    $deadline = ([DateTimeOffset](& $GetUtcNow)).AddSeconds($TimeoutSeconds)
    $attempt = 0
    while ($true) {
        $attempt++
        $phase = & $InvokeRead $attempt
        if ($phase -isnot [System.Collections.IDictionary] -or -not $phase.Contains('tenantSettings')) {
            throw "Tenant-setting callback for '$Name' did not return a phase with tenantSettings."
        }
        $phase['name'] = $Name
        $phase['attempts'] = $attempt
        if ($phase.status -ne 'Passed') { return $phase }

        try {
            $comparison = Compare-UdpTenantSettingSnapshot -ExpectedSnapshot $ExpectedSnapshot -LiveSettings @($phase.tenantSettings)
        }
        catch {
            $phase['status'] = 'Failed'
            $phase['message'] = "Tenant-setting snapshot response was invalid: $($_.Exception.Message)"
            return $phase
        }
        if ($comparison.matches) { return $phase }
        if ([DateTimeOffset](& $GetUtcNow) -ge $deadline) {
            $phase['status'] = 'Failed'
            $phase['message'] = "Tenant settings did not return to the captured snapshot within $TimeoutSeconds seconds; mismatched: $($comparison.mismatchedSettingNames -join ', ')."
            return $phase
        }
        & $Delay $PollIntervalSeconds
    }
}

function Test-UdpAdminPlanConverged {
    [CmdletBinding()]
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Phase)

    return $Phase.status -eq 'Passed' -and
        $Phase.stdout -match '(?i)Summary:\s*0\s+to update\b' -and
        $Phase.stdout -notmatch '(?i)unknown setting'
}

function Wait-UdpAdminPlanConvergence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][scriptblock]$InvokePlan,
        [Parameter(Mandatory)][string]$Name,
        [ValidateRange(1, 1800)][int]$TimeoutSeconds = 900,
        [ValidateRange(0, 300)][int]$PollIntervalSeconds = 30,
        [scriptblock]$GetUtcNow = { [DateTimeOffset]::UtcNow },
        [scriptblock]$Delay = { param([int]$Seconds) if ($Seconds -gt 0) { Start-Sleep -Seconds $Seconds } }
    )

    $startedAt = [DateTimeOffset](& $GetUtcNow)
    $deadline = $startedAt.AddSeconds($TimeoutSeconds)
    $attempt = 0
    while ($true) {
        $attempt++
        $phase = & $InvokePlan $attempt
        if ($phase -isnot [System.Collections.IDictionary]) {
            throw "Admin plan callback for '$Name' did not return a phase result."
        }

        $phase['name'] = $Name
        $phase['attempts'] = $attempt
        if ($phase.status -ne 'Passed') {
            return $phase
        }
        if ($phase.stdout -match '(?i)unknown setting') {
            $phase['status'] = 'Failed'
            $phase['message'] = 'Admin plan reported an unknown tenant setting.'
            return $phase
        }
        if (Test-UdpAdminPlanConverged -Phase $phase) {
            return $phase
        }
        if ([DateTimeOffset](& $GetUtcNow) -ge $deadline) {
            $phase['status'] = 'Failed'
            $phase['message'] = "Tenant settings did not converge within $TimeoutSeconds seconds."
            return $phase
        }

        & $Delay $PollIntervalSeconds
    }
}

function Test-UdpAzureCreationOperation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Operations,
        [Parameter(Mandatory)][string]$ExpectedResourceId,
        [string]$ExpectedResourceType = 'Microsoft.Resources/resourceGroups'
    )

    $matchingOperations = [System.Collections.Generic.List[object]]::new()
    foreach ($operation in $Operations) {
        if ($operation -isnot [System.Collections.IDictionary] -or
            $operation.properties -isnot [System.Collections.IDictionary] -or
            $operation.properties.targetResource -isnot [System.Collections.IDictionary]) {
            continue
        }

        $target = $operation.properties.targetResource
        if ([string]::Equals([string]$target.id, $ExpectedResourceId, [StringComparison]::OrdinalIgnoreCase) -and
            [string]::Equals([string]$target.resourceType, $ExpectedResourceType, [StringComparison]::OrdinalIgnoreCase)) {
            $matchingOperations.Add($operation.properties)
        }
    }

    $operation = if ($matchingOperations.Count -eq 1) { $matchingOperations[0] } else { $null }
    $statusCode = if ($operation) { ([string]$operation.statusCode).Trim() } else { '' }
    $operationMatches = $operation -and [string]::Equals([string]$operation.provisioningOperation, 'Create', [StringComparison]::OrdinalIgnoreCase)
    $stateMatches = $operation -and [string]::Equals([string]$operation.provisioningState, 'Succeeded', [StringComparison]::OrdinalIgnoreCase)
    $statusMatches = $statusCode -match '(?i)^(?:201(?:\s+Created)?|Created)$'

    return [ordered]@{
        matches = $matchingOperations.Count -eq 1 -and $operationMatches -and $stateMatches -and $statusMatches
        matchingOperationCount = $matchingOperations.Count
        provisioningOperation = if ($operation) { [string]$operation.provisioningOperation } else { $null }
        provisioningState = if ($operation) { [string]$operation.provisioningState } else { $null }
        statusCode = if ($operation) { $statusCode } else { $null }
    }
}

function Test-UdpOwnershipSnapshot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ExpectedId,
        [Parameter(Mandatory)][string]$ActualId,
        [Parameter(Mandatory)][string]$ExpectedMarker,
        [AllowNull()][string]$ActualMarker,
        [AllowEmptyCollection()][string[]]$ExpectedChildIds = @(),
        [AllowEmptyCollection()][string[]]$ActualChildIds = @()
    )

    $expectedChildren = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $actualChildren = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($id in $ExpectedChildIds) { [void]$expectedChildren.Add($id) }
    foreach ($id in $ActualChildIds) { [void]$actualChildren.Add($id) }

    $missingChildIds = @($expectedChildren | Where-Object { -not $actualChildren.Contains($_) } | Sort-Object)
    $unexpectedChildIds = @($actualChildren | Where-Object { -not $expectedChildren.Contains($_) } | Sort-Object)
    $idMatches = [string]::Equals($ExpectedId, $ActualId, [StringComparison]::OrdinalIgnoreCase)
    $markerMatches = [string]::Equals($ExpectedMarker, $ActualMarker, [StringComparison]::Ordinal)
    return [ordered]@{
        matches = $idMatches -and $markerMatches -and $missingChildIds.Count -eq 0 -and $unexpectedChildIds.Count -eq 0
        idMatches = $idMatches
        markerMatches = $markerMatches
        missingChildIds = $missingChildIds
        unexpectedChildIds = $unexpectedChildIds
    }
}

function Get-UdpManifestFacts {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ManifestPath)

    $lines = @(Get-Content -LiteralPath $ManifestPath -Encoding utf8)
    $content = $lines -join "`n"
    $root = Split-Path -Parent $ManifestPath
    $resources = [System.Collections.Generic.List[object]]::new()
    $fileReferences = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $variableReferences = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $secretReferences = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $principalReferences = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $workspaceNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $tenantSettingNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $tenantSettingFields = [ordered]@{}
    $externalMarkers = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $topLevelCounts = @{}
    $variableDefaults = @{}
    $deploymentName = $null
    $currentResourceType = $null
    $currentResource = $null
    $currentVariable = $null
    $inResources = $false
    $inVariables = $false
    $inTargets = $false
    $inWorkspace = $false
    $inDeployment = $false
    $inAdmin = $false
    $inTenantSettings = $false
    $currentTenantSetting = $null

    foreach ($line in $lines) {
        if ($line -match '^([A-Za-z_][A-Za-z0-9_-]*):\s*(?:#.*)?$') {
            $key = $Matches[1]
            $topLevelCounts[$key] = 1 + [int]($topLevelCounts[$key])
            $inResources = $key -eq 'resources'
            $inVariables = $key -eq 'variables'
            $inTargets = $key -eq 'targets'
            $inDeployment = $key -eq 'deployment'
            $inAdmin = $key -eq 'admin'
            $inTenantSettings = $false
            $currentTenantSetting = $null
            $inWorkspace = $false
            $currentResourceType = $null
            $currentResource = $null
            $currentVariable = $null
            continue
        }

        if ($inAdmin -and $line -match '^  tenant_settings:\s*(?:#.*)?$') {
            $inTenantSettings = $true
            continue
        }
        if ($inAdmin -and $line -match '^  [A-Za-z_][A-Za-z0-9_-]*:\s*(?:#.*)?$') {
            $inTenantSettings = $false
            $currentTenantSetting = $null
        }
        if ($inAdmin -and $inTenantSettings -and $line -match '^    ([^\s:#][^:]*):(?:\s|$)') {
            $currentTenantSetting = Get-UdpCleanValue -Value $Matches[1]
            [void]$tenantSettingNames.Add($currentTenantSetting)
            if (-not $tenantSettingFields.Contains($currentTenantSetting)) {
                $tenantSettingFields[$currentTenantSetting] = [System.Collections.Generic.List[string]]::new()
            }
            continue
        }
        if ($inAdmin -and $inTenantSettings -and $currentTenantSetting -and $line -match '^      ([A-Za-z_][A-Za-z0-9_]*):(?:\s|$)') {
            $field = $Matches[1]
            if (-not $tenantSettingFields[$currentTenantSetting].Contains($field)) {
                $tenantSettingFields[$currentTenantSetting].Add($field)
            }
            continue
        }

        if ($inDeployment -and $line -match '^  name:\s*(.+?)\s*$') {
            $deploymentName = Get-UdpCleanValue -Value $Matches[1]
        }
        if ($inResources -and $line -match '^  ([A-Za-z_][A-Za-z0-9_]*):\s*(?:#.*)?$') {
            $currentResourceType = $Matches[1]
            $currentResource = $null
            continue
        }
        if ($inResources -and $currentResourceType -and $line -match '^    ([^\s:#][^:]*):(?:\s|$)') {
            $resourceName = Get-UdpCleanValue -Value $Matches[1]
            $platform = if ($currentResourceType.StartsWith('azure_', [StringComparison]::Ordinal)) {
                'Azure'
            }
            elseif ($currentResourceType -in @('entra_groups', 'entra_apps')) {
                'Entra'
            }
            else {
                'Fabric'
            }
            $currentResource = [ordered]@{
                type = $currentResourceType
                name = $resourceName
                platform = $platform
                scope = $null
                resourceGroup = $null
            }
            $resources.Add($currentResource)
            continue
        }
        if ($inResources -and $currentResource -and $line -match '^      scope:\s*(.+?)\s*$') {
            $currentResource.scope = Get-UdpCleanValue -Value $Matches[1]
        }
        if ($inResources -and $currentResource -and $line -match '^      resource_group:\s*(.+?)\s*$') {
            $currentResource.resourceGroup = Get-UdpCleanValue -Value $Matches[1]
        }
        if ($inVariables -and $line -match '^  ([A-Za-z_][A-Za-z0-9_]*):\s*(?:#.*)?$') {
            $currentVariable = $Matches[1]
            continue
        }
        if ($inVariables -and $currentVariable -and $line -match '^    default:\s*(.*?)\s*$') {
            $variableDefaults[$currentVariable] = Get-UdpCleanValue -Value $Matches[1]
        }
        if ($inTargets -and $line -match '^    workspace:\s*(?:#.*)?$') {
            $inWorkspace = $true
            continue
        }
        if ($inTargets -and $inWorkspace -and $line -match '^      name:\s*(.+?)\s*$') {
            [void]$workspaceNames.Add((Get-UdpCleanValue -Value $Matches[1]))
            $inWorkspace = $false
        }

        if ($line -match '^\s+(?:path|instructions|few_shot_examples|template_file):\s*(\./[^#]+?)\s*$') {
            [void]$fileReferences.Add((Get-UdpCleanValue -Value $Matches[1]))
        }
        if ($line -match '^\s*-\s*(\./[^#]+?)\s*$') {
            [void]$fileReferences.Add((Get-UdpCleanValue -Value $Matches[1]))
        }
        if ($line -match '^\s+entra_group:\s*(.+?)\s*$') {
            [void]$principalReferences.Add((Get-UdpCleanValue -Value $Matches[1]))
        }
        if ($line -match '(?i)(contoso|partner-bucket|partner-data-bucket|shared-workspace-id|adls://datalake|/subscriptions/\.\.\.)') {
            [void]$externalMarkers.Add($line.Trim())
        }
    }

    foreach ($match in [regex]::Matches($content, '\$\{var\.([A-Za-z_][A-Za-z0-9_]*)\}')) {
        [void]$variableReferences.Add($match.Groups[1].Value)
    }
    foreach ($match in [regex]::Matches($content, '\$\{secret\.([A-Za-z_][A-Za-z0-9_]*)\}')) {
        [void]$secretReferences.Add($match.Groups[1].Value)
    }

    $missingAssets = foreach ($reference in $fileReferences) {
        $candidate = Join-Path -Path $root -ChildPath $reference
        if (-not (Test-Path -LiteralPath $candidate)) {
            $reference
        }
    }
    $duplicateTopLevelKeys = foreach ($entry in $topLevelCounts.GetEnumerator()) {
        if ($entry.Value -gt 1) { $entry.Key }
    }

    return [ordered]@{
        deploymentName = $deploymentName
        resources = @($resources)
        fileReferences = @($fileReferences)
        missingAssets = @($missingAssets)
        variableReferences = @($variableReferences)
        secretReferences = @($secretReferences)
        variableDefaults = $variableDefaults
        principalReferences = @($principalReferences)
        workspaceNames = @($workspaceNames)
        tenantSettingNames = @($tenantSettingNames)
        tenantSettingFields = $tenantSettingFields
        externalMarkers = @($externalMarkers)
        duplicateTopLevelKeys = @($duplicateTopLevelKeys)
    }
}

function Get-UdpDefaultInputs {
    [CmdletBinding()]
    param()

    $values = @{}
    $environmentMappings = @{
        capacity_id = 'UDP_TEST_FABRIC_CAPACITY_ID'
        subscription_id = 'UDP_TEST_AZURE_SUBSCRIPTION_ID'
        tenant_id = 'UDP_TEST_ENTRA_TENANT_ID'
        adls_connection_id = 'UDP_TEST_ADLS_CONNECTION_ID'
        data_eng_group_id = 'UDP_TEST_DATA_ENG_GROUP_ID'
        contractors_group_id = 'UDP_TEST_CONTRACTORS_GROUP_ID'
        bi_admins_group_id = 'UDP_TEST_BI_ADMINS_GROUP_ID'
    }
    foreach ($mapping in $environmentMappings.GetEnumerator()) {
        $value = [Environment]::GetEnvironmentVariable($mapping.Value)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            $values[$mapping.Key] = $value
        }
    }
    if (-not $values.ContainsKey('subscription_id') -and $env:AZURE_SUBSCRIPTION_ID) {
        $values.subscription_id = $env:AZURE_SUBSCRIPTION_ID
    }
    if (-not $values.ContainsKey('tenant_id') -and $env:AZURE_TENANT_ID) {
        $values.tenant_id = $env:AZURE_TENANT_ID
    }

    $principalMappings = if (-not (Test-UdpPlaceholder -Value $env:UDP_TEST_PRINCIPAL_MAPPINGS_JSON)) {
        $env:UDP_TEST_PRINCIPAL_MAPPINGS_JSON | ConvertFrom-Json -AsHashtable
    }
    else { @{} }
    $textReplacements = if (-not (Test-UdpPlaceholder -Value $env:UDP_TEST_TEXT_REPLACEMENTS_JSON)) {
        $env:UDP_TEST_TEXT_REPLACEMENTS_JSON | ConvertFrom-Json -AsHashtable
    }
    else { @{} }

    return [ordered]@{
        schemaVersion = '1.0'
        values = $values
        principalMappings = $principalMappings
        textReplacements = $textReplacements
        assetOverlays = @{}
        exampleOverrides = @{}
    }
}

function Merge-UdpInputs {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$Base,
        [Parameter(Mandatory)][System.Collections.IDictionary]$Overlay
    )

    $allowedKeys = @('$schema', 'schemaVersion', 'values', 'principalMappings', 'textReplacements', 'assetOverlays', 'exampleOverrides')
    foreach ($key in $Overlay.Keys) {
        if ($key -notin $allowedKeys) {
            throw "Unsupported input manifest property: $key"
        }
    }
    if ($Overlay.Contains('schemaVersion') -and [string]$Overlay.schemaVersion -ne '1.0') {
        throw "Unsupported input manifest schemaVersion: $($Overlay.schemaVersion)"
    }

    foreach ($section in @('values', 'principalMappings', 'textReplacements', 'assetOverlays', 'exampleOverrides')) {
        if (-not $Base.Contains($section)) { $Base[$section] = @{} }
        if ($Overlay.Contains($section)) {
            if ($Overlay[$section] -isnot [System.Collections.IDictionary]) {
                throw "Input manifest property '$section' must be a JSON object."
            }
            foreach ($entry in $Overlay[$section].GetEnumerator()) {
                $Base[$section][$entry.Key] = $entry.Value
            }
        }
    }
    return $Base
}

function Set-UdpVariableDefaults {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string[]]$Lines,
        [Parameter(Mandatory)][System.Collections.IDictionary]$Values
    )

    $inVariables = $false
    $currentVariable = $null
    for ($index = 0; $index -lt $Lines.Count; $index++) {
        $line = $Lines[$index]
        if ($line -match '^([A-Za-z_][A-Za-z0-9_-]*):\s*(?:#.*)?$') {
            $inVariables = $Matches[1] -eq 'variables'
            $currentVariable = $null
            continue
        }
        if ($inVariables -and $line -match '^  ([A-Za-z_][A-Za-z0-9_]*):\s*(?:#.*)?$') {
            $currentVariable = $Matches[1]
            continue
        }
        if ($inVariables -and $currentVariable -and $line -match '^(\s{4}default:)\s*.*$' -and $Values.Contains($currentVariable)) {
            $value = [string]$Values[$currentVariable]
            if (-not (Test-UdpPlaceholder -Value $value)) {
                $Lines[$index] = "$($Matches[1]) $(ConvertTo-UdpYamlScalar -Value $value)"
            }
        }
    }
    return $Lines
}

function Get-UdpRunToken {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Suffix)

    $normalized = $Suffix.ToLowerInvariant() -replace '[^a-z0-9]', ''
    if ([string]::IsNullOrEmpty($normalized)) { return 'test' }
    if ($normalized.Length -le 12) { return $normalized }

    $bytes = [Text.Encoding]::UTF8.GetBytes($Suffix)
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
    return $hash.Substring(0, 12)
}

function Resolve-UdpContainedPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Candidate,
        [switch]$RequireRelative
    )

    if ($RequireRelative -and [IO.Path]::IsPathRooted($Candidate)) {
        throw "Path must be relative to its containment root: $Candidate"
    }
    $resolvedRoot = [IO.Path]::GetFullPath($Root)
    $resolvedCandidate = if ([IO.Path]::IsPathRooted($Candidate)) {
        [IO.Path]::GetFullPath($Candidate)
    }
    else {
        [IO.Path]::GetFullPath($Candidate, $resolvedRoot)
    }
    $relative = [IO.Path]::GetRelativePath($resolvedRoot, $resolvedCandidate)
    if ([IO.Path]::IsPathRooted($relative) -or
        $relative -eq '..' -or
        $relative.StartsWith("..$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::Ordinal) -or
        $relative.StartsWith("..$([IO.Path]::AltDirectorySeparatorChar)", [StringComparison]::Ordinal)) {
        throw "Path escapes its containment root '$resolvedRoot': $Candidate"
    }
    return $resolvedCandidate
}

function Get-UdpIsolatedName {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Type,
        [Parameter(Mandatory)][string]$Suffix
    )

    $token = Get-UdpRunToken -Suffix $Suffix

    $storageTypes = @('azure_storage_accounts', 'azure_blob_storage', 'azure_data_lake_storage', 'azure_files', 'azure_queue_storage', 'azure_table_storage')
    if ($Type -in $storageTypes) {
        $base = $Name.ToLowerInvariant() -replace '[^a-z0-9]', ''
        $maxBase = [Math]::Max(3, 24 - $token.Length)
        if ($base.Length -gt $maxBase) { $base = $base.Substring(0, $maxBase) }
        return "$base$token"
    }
    if ($Type -in @('azure_dns_zones', 'azure_private_dns_zones')) {
        return "$token.$Name"
    }
    if ($Name.Contains('/')) {
        $parts = $Name.Split('/', 2)
        return "$($parts[0])/$($parts[1])-$token"
    }

    $maxLength = if ($Type -eq 'azure_key_vaults') { 24 } else { 80 }
    $maxBaseLength = [Math]::Max(1, $maxLength - $token.Length - 1)
    $baseName = if ($Name.Length -gt $maxBaseLength) { $Name.Substring(0, $maxBaseLength) } else { $Name }
    $candidate = "$baseName-$token"
    return $candidate.TrimEnd('-')
}

function Protect-UdpTestNames {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Content,
        [Parameter(Mandatory)][System.Collections.IDictionary]$Facts,
        [Parameter(Mandatory)][string]$Suffix
    )

    $replacements = @{}
    if ($Facts.deploymentName) {
        $replacements[$Facts.deploymentName] = Get-UdpIsolatedName -Name $Facts.deploymentName -Type 'deployment' -Suffix $Suffix
    }
    foreach ($workspaceName in $Facts.workspaceNames) {
        $replacements[$workspaceName] = Get-UdpIsolatedName -Name $workspaceName -Type 'workspace' -Suffix $Suffix
    }
    foreach ($resource in $Facts.resources) {
        if ($resource.platform -in @('Azure', 'Entra')) {
            $replacements[$resource.name] = Get-UdpIsolatedName -Name $resource.name -Type $resource.type -Suffix $Suffix
        }
    }

    $temporaryReplacements = [ordered]@{}
    $replacementIndex = 0
    foreach ($entry in $replacements.GetEnumerator() | Sort-Object { $_.Key.Length } -Descending) {
        $token = "__UDP_TEST_NAME_$replacementIndex`__"
        $Content = $Content.Replace([string]$entry.Key, $token)
        $temporaryReplacements[$token] = [string]$entry.Value
        $replacementIndex++
    }
    foreach ($entry in $temporaryReplacements.GetEnumerator()) {
        $Content = $Content.Replace([string]$entry.Key, [string]$entry.Value)
    }

    $vaultPattern = '(?m)^(\s+vaultName:\s*)["'']?([^"''#\r\n]+?)["'']?\s*$'
    $Content = [regex]::Replace($Content, $vaultPattern, {
        param($match)
        $isolatedVaultName = Get-UdpIsolatedName -Name $match.Groups[2].Value.Trim() -Type 'azure_key_vaults' -Suffix $Suffix
        return "$($match.Groups[1].Value)'$isolatedVaultName'"
    })
    $suffixToken = Get-UdpRunToken -Suffix $Suffix
    $managedGroupPattern = '(?<=/resourceGroups/)([A-Za-z0-9._()-]+)'
    $Content = [regex]::Replace($Content, $managedGroupPattern, {
        param($match)
        if ($match.Value.EndsWith("-$suffixToken", [StringComparison]::OrdinalIgnoreCase)) {
            return $match.Value
        }
        return Get-UdpIsolatedName -Name $match.Value -Type 'azure_resource_groups' -Suffix $Suffix
    })
    return [ordered]@{ content = $Content; replacements = $replacements }
}

function Copy-UdpOverlay {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination,
        [Parameter(Mandatory)][string]$InputBasePath
    )

    $resolvedSource = Resolve-UdpContainedPath -Root $InputBasePath -Candidate $Source
    if (-not (Test-Path -LiteralPath $resolvedSource)) {
        throw "Asset overlay source not found: $resolvedSource"
    }
    $reparsePoints = @(Get-Item -LiteralPath $resolvedSource -Force; if (Test-Path -LiteralPath $resolvedSource -PathType Container) { Get-ChildItem -LiteralPath $resolvedSource -Recurse -Force }) |
        Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }
    if ($reparsePoints.Count -gt 0) {
        throw "Asset overlays cannot contain symbolic links or reparse points: $resolvedSource"
    }
    [void](New-Item -ItemType Directory -Path $Destination -Force)
    if (Test-Path -LiteralPath $resolvedSource -PathType Container) {
        Get-ChildItem -LiteralPath $resolvedSource -Force | Copy-Item -Destination $Destination -Recurse -Force
    }
    else {
        Copy-Item -LiteralPath $resolvedSource -Destination $Destination -Force
    }
}

function New-UdpStagedExample {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$Example,
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$WorkRoot,
        [Parameter(Mandatory)][System.Collections.IDictionary]$Inputs,
        [Parameter(Mandatory)][string]$RunSuffix,
        [Parameter(Mandatory)][string]$InputBasePath
    )

    $source = Join-Path $RepositoryRoot $Example.path
    if (-not (Test-Path -LiteralPath $source -PathType Container)) {
        throw "Example directory not found: $source"
    }
    $destination = Join-Path $WorkRoot "$($Example.id)-$($Example.name)"
    if (Test-Path -LiteralPath $destination) {
        Remove-Item -LiteralPath $destination -Recurse -Force
    }
    [void](New-Item -ItemType Directory -Path $destination -Force)
    Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $destination -Recurse -Force

    $manifest = Join-Path $destination 'udp.yml'
    $manifestHashBeforeOverlays = (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash

    if ($Inputs.assetOverlays.Contains($Example.id)) {
        foreach ($overlay in $Inputs.assetOverlays[$Example.id]) {
            $overlayDestination = Resolve-UdpContainedPath -Root $destination -Candidate ([string]$overlay.destination) -RequireRelative
            Copy-UdpOverlay -Source ([string]$overlay.source) -Destination $overlayDestination -InputBasePath $InputBasePath
        }
    }
    if ((Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash -ne $manifestHashBeforeOverlays) {
        throw 'Asset overlays cannot replace the staged udp.yml manifest.'
    }

    $sourceFacts = Get-UdpManifestFacts -ManifestPath $manifest
    $lines = @(Get-Content -LiteralPath $manifest -Encoding utf8)
    $values = @{}
    foreach ($entry in $Inputs.values.GetEnumerator()) {
        if ([string]$entry.Key -match '(?i)secret|token|password|passwd|api_?key|webhook|connection_string|sas') {
            throw "Input '$($entry.Key)' is secret-bearing. Use a `${secret.NAME} reference and supply only the named environment variable."
        }
        if ([string]$entry.Value -match "`r|`n") {
            throw "Input '$($entry.Key)' must be a single-line scalar."
        }
        $values[$entry.Key] = $entry.Value
    }
    $lines = Set-UdpVariableDefaults -Lines $lines -Values $values
    $content = $lines -join "`n"
    foreach ($entry in $Inputs.principalMappings.GetEnumerator()) {
        $sourceName = [string]$entry.Key
        $objectId = [string]$entry.Value
        if ($sourceName -in $sourceFacts.principalReferences -and -not (Test-UdpPlaceholder -Value $objectId)) {
            $parsedId = [Guid]::Empty
            if (-not [Guid]::TryParse($objectId, [ref]$parsedId)) {
                throw "Principal mapping '$sourceName' must resolve to an Entra object ID."
            }
            $content = $content.Replace($sourceName, $objectId)
        }
    }
    foreach ($entry in $Inputs.textReplacements.GetEnumerator()) {
        $sourceText = [string]$entry.Key
        $replacementText = [string]$entry.Value
        if (-not $content.Contains($sourceText, [StringComparison]::Ordinal) -or (Test-UdpPlaceholder -Value $replacementText)) {
            continue
        }
        $approved = @($sourceFacts.externalMarkers | Where-Object { $_.Contains($sourceText, [StringComparison]::Ordinal) }).Count -gt 0
        if (-not $approved) {
            throw "Text replacement source is not a discovered illustrative external binding: $sourceText"
        }
        if ($replacementText -match "`r|`n" -or (Protect-UdpSensitiveText -Text $replacementText) -ne $replacementText) {
            throw "Text replacement for '$sourceText' must be a single-line, non-secret value."
        }
        $content = $content.Replace($sourceText, $replacementText)
    }

    $content | Set-Content -LiteralPath $manifest -Encoding utf8
    $preIsolationFacts = Get-UdpManifestFacts -ManifestPath $manifest
    $isolated = Protect-UdpTestNames -Content $content -Facts $preIsolationFacts -Suffix $RunSuffix
    $isolated.content | Set-Content -LiteralPath $manifest -Encoding utf8

    return [ordered]@{
        directory = $destination
        manifest = $manifest
        nameReplacements = $isolated.replacements
        facts = Get-UdpManifestFacts -ManifestPath $manifest
    }
}

function Get-UdpRequirements {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$Facts,
        [Parameter(Mandatory)][ValidateSet('Preflight', 'Validate', 'Plan', 'Deploy')][string]$Mode
    )

    $missing = [System.Collections.Generic.List[object]]::new()
    foreach ($variable in $Facts.variableReferences) {
        $default = if ($Facts.variableDefaults.Contains($variable)) { [string]$Facts.variableDefaults[$variable] } else { '' }
        if (Test-UdpPlaceholder -Value $default) {
            $missing.Add([ordered]@{
                kind = 'input'
                name = $variable
                reason = "Variable '$variable' has no deployable value."
                canGenerate = $false
                supply = "Set values.$variable in the input manifest or bind it to an environment variable."
                blocksMutation = $true
            })
        }
    }
    foreach ($secret in $Facts.secretReferences) {
        $value = [Environment]::GetEnvironmentVariable($secret)
        if (Test-UdpPlaceholder -Value $value) {
            $missing.Add([ordered]@{
                kind = 'secret'
                name = $secret
                reason = "Secret '$secret' is not available in the process environment."
                canGenerate = $false
                supply = "Set the $secret environment variable in the manually triggered pipeline."
                blocksMutation = $true
            })
        }
    }
    foreach ($asset in $Facts.missingAssets) {
        $missing.Add([ordered]@{
            kind = 'asset'
            name = $asset
            reason = 'A referenced definition file or directory is absent from the staged example.'
            canGenerate = $false
            supply = 'Provide it through assetOverlays for this example.'
            blocksMutation = $true
        })
    }
    foreach ($principal in $Facts.principalReferences) {
        if ($principal -notmatch '^[0-9a-fA-F]{8}-[0-9a-fA-F-]{27,}$') {
            $missing.Add([ordered]@{
                kind = 'principal'
                name = $principal
                reason = 'The example references an environment-specific Entra display name.'
                canGenerate = $false
                supply = 'Map it to an existing object ID through principalMappings.'
                blocksMutation = $true
            })
        }
    }
    foreach ($marker in $Facts.externalMarkers) {
        $missing.Add([ordered]@{
            kind = 'external-binding'
            name = $marker
            reason = 'The example contains an illustrative external resource identifier.'
            canGenerate = $false
            supply = 'Replace the exact value through textReplacements.'
            blocksMutation = $true
        })
    }
    foreach ($key in $Facts.duplicateTopLevelKeys) {
        $missing.Add([ordered]@{
            kind = 'manifest'
            name = $key
            reason = "The top-level YAML key '$key' appears more than once."
            canGenerate = $true
            supply = 'Merge the duplicate sections in the source example.'
            blocksMutation = $true
        })
    }

    if ($Mode -eq 'Deploy') {
        $declaredGroups = @($Facts.resources | Where-Object type -eq 'azure_resource_groups' | ForEach-Object name)
        foreach ($resource in @($Facts.resources | Where-Object { $_.platform -eq 'Azure' -and $_.type -ne 'azure_resource_groups' })) {
            if ($resource.type -eq 'azure_deployments' -and $resource.scope -eq 'subscription') {
                $missing.Add([ordered]@{
                    kind = 'safety'
                    name = "$($resource.type).$($resource.name)"
                    reason = 'Subscription-scope generic Bicep has no bounded automated cleanup path.'
                    canGenerate = $false
                    supply = 'Validate or plan this deployment separately; the example harness does not mutate subscription-scope generic Bicep.'
                    blocksMutation = $true
                })
                continue
            }
            if ([string]::IsNullOrWhiteSpace([string]$resource.resourceGroup) -or $resource.resourceGroup -notin $declaredGroups) {
                $missing.Add([ordered]@{
                    kind = 'safety'
                    name = "$($resource.type).$($resource.name)"
                    reason = 'Azure mutation is not bounded by a resource group declared in the same staged manifest.'
                    canGenerate = $false
                    supply = 'Set resource_group to an azure_resource_groups entry in the same example so ownership and cleanup can be proven.'
                    blocksMutation = $true
                })
            }
        }
    }

    if ($Mode -in @('Preflight', 'Validate')) {
        foreach ($requirement in $missing) { $requirement.blocksMutation = $false }
    }
    return @($missing)
}

function Get-UdpPermissionFinding {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][string]$ExampleId,
        [Parameter(Mandatory)][string]$Phase
    )

    if ($Text -notmatch '(?i)AuthorizationFailed|Forbidden|insufficient privileges|does not have authorization|status\s*code\s*403|\b403\b') {
        return $null
    }
    $action = $null
    $scope = $null
    if ($Text -match "(?i)perform action '([^']+)' over scope '([^']+)'" ) {
        $action = $Matches[1]
        $scope = $Matches[2]
    }
    $platform = if ($Text -match '(?i)graph|entra|group\.read|application\.read') { 'Entra' }
        elseif ($Text -match '(?i)fabric|workspace|onelake') { 'Fabric' }
        elseif ($Phase -match '(?i)purview' -or $Text -match '(?i)purview') { 'Purview' }
        else { 'Azure' }

    return [ordered]@{
        exampleId = $ExampleId
        phase = $Phase
        platform = $platform
        action = $action
        scope = $scope
        evidence = ($Text -split "`r?`n" | Where-Object { $_ -match '(?i)AuthorizationFailed|Forbidden|insufficient privileges|403' } | Select-Object -First 1)
        recommendation = 'Use PSAutoRBAC discover/evaluate/generate at the narrowest scope. Do not auto-grant from this harness.'
    }
}

function Invoke-UdpCliPhase {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$CliFilePath,
        [string[]]$CliPrefixArguments = @(),
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$LogDirectory,
        [ValidateRange(1, 86400)][int]$TimeoutSeconds = 1800
    )

    [void](New-Item -ItemType Directory -Path $LogDirectory -Force)
    $safeName = $Name.ToLowerInvariant() -replace '[^a-z0-9-]', '-'
    $stdout = Join-Path $LogDirectory "$safeName.stdout.log"
    $stderr = Join-Path $LogDirectory "$safeName.stderr.log"
    $allArguments = @($CliPrefixArguments) + @($Arguments)
    $process = Invoke-UdpProcess -FilePath $CliFilePath -ArgumentList $allArguments -WorkingDirectory $WorkingDirectory -StdOutPath $stdout -StdErrPath $stderr -TimeoutSeconds $TimeoutSeconds -Environment @{ NO_COLOR = '1'; DOTNET_NOLOGO = '1' }
    return [ordered]@{
        name = $Name
        status = if ($process.succeeded) { 'Passed' } else { 'Failed' }
        command = $process.command
        exitCode = $process.exitCode
        timedOut = $process.timedOut
        durationSeconds = $process.durationSeconds
        stdoutPath = $process.stdoutPath
        stderrPath = $process.stderrPath
        stdout = $process.stdout
        stderr = $process.stderr
    }
}

function Invoke-UdpAzureVerification {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$Resource,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$LogDirectory,
        [string]$SubscriptionId,
        [int]$TimeoutSeconds = 300
    )

    $name = [string]$Resource.name
    $type = [string]$Resource.type
    $arguments = if ($type -eq 'azure_resource_groups') {
        @('group', 'exists', '--name', $name)
    }
    else {
        @('resource', 'list', '--name', $name, '--query', 'length(@)', '--output', 'tsv')
    }
    if ($SubscriptionId) { $arguments += @('--subscription', $SubscriptionId) }
    $phase = Invoke-UdpCliPhase -Name "verify-azure-$name" -CliFilePath 'az' -Arguments $arguments -WorkingDirectory $WorkingDirectory -LogDirectory $LogDirectory -TimeoutSeconds $TimeoutSeconds
    $value = $phase.stdout.Trim().ToLowerInvariant()
    $exists = $phase.status -eq 'Passed' -and (($type -eq 'azure_resource_groups' -and $value -eq 'true') -or ($type -ne 'azure_resource_groups' -and [int]$value -gt 0))
    $phase.status = if ($exists) { 'Passed' } else { 'Failed' }
    return $phase
}

function Invoke-UdpPurviewVerification {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$Resource,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$LogDirectory,
        [int]$TimeoutSeconds = 300,
        [string]$CliFilePath = 'az',
        [string[]]$CliPrefixArguments = @()
    )

    $name = [string]$Resource.name
    if ($Resource.type -ne 'azure_purview_accounts' -or $name -notmatch '^[a-z0-9](?:[a-z0-9-]{1,61}[a-z0-9])$') {
        return [ordered]@{
            name = "verify-purview-$name"
            status = 'Failed'
            command = $null
            exitCode = 3
            timedOut = $false
            durationSeconds = 0
            stdoutPath = $null
            stderrPath = $null
            stdout = ''
            stderr = 'Purview account name is not a safe 3-63 character DNS label.'
        }
    }

    $endpoint = "https://$name.purview.azure.com/account/?api-version=2019-11-01-preview"
    $phase = Invoke-UdpCliPhase -Name "verify-purview-$name" -CliFilePath $CliFilePath -CliPrefixArguments $CliPrefixArguments -Arguments @(
        'rest', '--method', 'get',
        '--url', $endpoint,
        '--resource', 'https://purview.azure.net',
        '--query', '{name:name,provisioningState:properties.provisioningState}',
        '--output', 'json',
        '--only-show-errors'
    ) -WorkingDirectory $WorkingDirectory -LogDirectory $LogDirectory -TimeoutSeconds $TimeoutSeconds

    if ($phase.status -eq 'Passed') {
        try {
            $account = $phase.stdout | ConvertFrom-Json -AsHashtable
            if (-not [string]::Equals([string]$account.name, $name, [StringComparison]::Ordinal) -or
                -not [string]::Equals([string]$account.provisioningState, 'Succeeded', [StringComparison]::OrdinalIgnoreCase)) {
                $phase.status = 'Failed'
            }
        }
        catch {
            $phase.status = 'Failed'
            $phase.stderr = "Purview data-plane response was invalid: $($_.Exception.Message)"
        }
    }
    return $phase
}

function Invoke-UdpEntraVerification {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$Resource,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$LogDirectory,
        [int]$TimeoutSeconds = 300
    )

    $name = [string]$Resource.name
    $isGroup = $Resource.type -eq 'entra_groups'
    $noun = if ($isGroup) { 'group' } else { 'app' }
    $arguments = @('ad', $noun, 'list', '--filter', "displayName eq '$($name.Replace("'", "''"))'", '--query', 'length(@)', '--output', 'tsv')
    $phase = Invoke-UdpCliPhase -Name "verify-entra-$name" -CliFilePath 'az' -Arguments $arguments -WorkingDirectory $WorkingDirectory -LogDirectory $LogDirectory -TimeoutSeconds $TimeoutSeconds
    $exists = $phase.status -eq 'Passed' -and [int]$phase.stdout.Trim() -gt 0
    $phase.status = if ($exists) { 'Passed' } else { 'Failed' }
    return $phase
}

function ConvertTo-UdpHtml {
    [CmdletBinding()]
    param([AllowNull()][object]$Value)
    return [Net.WebUtility]::HtmlEncode([string]$Value)
}

function New-UdpHtmlReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$Summary,
        [Parameter(Mandatory)][string]$OutputPath
    )

    $rows = foreach ($result in $Summary.results) {
        $phaseText = ($result.phases | ForEach-Object { "$(ConvertTo-UdpHtml $_.name): $(ConvertTo-UdpHtml $_.status)" }) -join '<br>'
        $missingText = ($result.requirements | ForEach-Object { "$(ConvertTo-UdpHtml $_.kind): $(ConvertTo-UdpHtml $_.name)" }) -join '<br>'
        if (-not $phaseText) { $phaseText = '&mdash;' }
        if (-not $missingText) { $missingText = '&mdash;' }
        "<tr><td>$([Net.WebUtility]::HtmlEncode($result.id))</td><td>$([Net.WebUtility]::HtmlEncode($result.name))</td><td><span class='status $($result.status.ToLowerInvariant())'>$([Net.WebUtility]::HtmlEncode($result.status))</span></td><td>$([Net.WebUtility]::HtmlEncode($result.supportLevel))</td><td>$phaseText</td><td>$missingText</td><td>$([Net.WebUtility]::HtmlEncode(($result.planes -join ', ')))</td></tr>"
    }
    $permissionRows = foreach ($finding in $Summary.permissionFindings) {
        "<tr><td>$(ConvertTo-UdpHtml $finding.exampleId)</td><td>$(ConvertTo-UdpHtml $finding.platform)</td><td>$(ConvertTo-UdpHtml $finding.action)</td><td>$(ConvertTo-UdpHtml $finding.scope)</td><td>$(ConvertTo-UdpHtml $finding.evidence)</td></tr>"
    }
    if (-not $permissionRows) { $permissionRows = '<tr><td colspan="5">No permission failures recorded.</td></tr>' }

    $html = @"
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>UDP deployment test report</title>
  <style>
    :root { color-scheme: light; --ink:#17212b; --muted:#5f6b76; --line:#d7dde3; --ok:#137333; --bad:#b3261e; --warn:#8a4b00; --bg:#f5f7f8; }
    body { margin:0; color:var(--ink); background:var(--bg); font:14px/1.5 "Segoe UI", sans-serif; }
    main { max-width:1500px; margin:0 auto; padding:32px; }
    h1 { margin:0 0 8px; font-size:30px; }
    h2 { margin-top:32px; font-size:20px; }
    .meta { color:var(--muted); }
    .summary { display:grid; grid-template-columns:repeat(4,minmax(120px,1fr)); gap:12px; margin:24px 0; }
    .metric { background:white; border:1px solid var(--line); border-radius:6px; padding:16px; }
    .metric strong { display:block; font-size:24px; }
    table { width:100%; border-collapse:collapse; background:white; }
    th,td { padding:10px; border:1px solid var(--line); text-align:left; vertical-align:top; }
    th { background:#eef2f4; }
    .status { font-weight:700; }
    .status.passed,.status.ready { color:var(--ok); }
    .status.failed,.status.blocked { color:var(--bad); }
    .status.notrun,.status.planned { color:var(--warn); }
    code { background:#e9eef1; padding:2px 4px; }
    @media (max-width:800px) { main{padding:16px}.summary{grid-template-columns:1fr 1fr} table{display:block;overflow:auto} }
  </style>
</head>
<body><main>
  <h1>UDP deployment test report</h1>
  <div class="meta">Run $(ConvertTo-UdpHtml $Summary.runId) | Mode $(ConvertTo-UdpHtml $Summary.mode) | Generated $(ConvertTo-UdpHtml $Summary.completedAt)</div>
  <div class="summary">
    <div class="metric"><span>Examples</span><strong>$($Summary.results.Count)</strong></div>
    <div class="metric"><span>Passed</span><strong>$($Summary.counts.Passed)</strong></div>
    <div class="metric"><span>Failed</span><strong>$($Summary.counts.Failed)</strong></div>
    <div class="metric"><span>Blocked</span><strong>$($Summary.counts.Blocked)</strong></div>
  </div>
  <h2>Example results</h2>
  <table><thead><tr><th>ID</th><th>Example</th><th>Result</th><th>Support</th><th>Phases</th><th>Requirements</th><th>Planes</th></tr></thead><tbody>$($rows -join "`n")</tbody></table>
  <h2>Permission evidence</h2>
  <p>The harness follows PSAutoRBAC's discover, evaluate, generate, approved-apply, and revoke model. It never grants roles automatically.</p>
  <table><thead><tr><th>Example</th><th>Platform</th><th>Action</th><th>Scope</th><th>Evidence</th></tr></thead><tbody>$($permissionRows -join "`n")</tbody></table>
  <h2>Well-Architected decisions</h2>
  <ul>
    <li><strong>Security:</strong> OIDC or workload identity, no client secret, least-privilege findings only.</li>
    <li><strong>Reliability:</strong> preflight gates, bounded timeouts, cleanup in finally, and per-phase evidence.</li>
    <li><strong>Performance efficiency:</strong> examples run sequentially by default to avoid Fabric and ARM throttling.</li>
    <li><strong>Cost optimization:</strong> validation-only catalogue scenarios and cleanup of isolated test resources.</li>
    <li><strong>Operational excellence:</strong> one runner for local, GitHub Actions, and Azure DevOps with JSONL and HTML artifacts.</li>
  </ul>
  <h2>Evidence files</h2>
  <p>Machine-readable summary: <code>summary.json</code>. Structured event stream: <code>events.jsonl</code>. Per-command stdout and stderr are under each example log directory.</p>
</main></body></html>
"@
    $html | Set-Content -LiteralPath $OutputPath -Encoding utf8
}

Export-ModuleMember -Function @(
    'Compare-UdpTenantSettingCoverage',
    'Get-UdpDefaultInputs',
    'Get-UdpManifestFacts',
    'Get-UdpPermissionFinding',
    'Get-UdpRequirements',
    'Get-UdpTenantSettingSnapshotFingerprint',
    'Invoke-UdpAzureVerification',
    'Invoke-UdpCliPhase',
    'Invoke-UdpEntraVerification',
    'Invoke-UdpPurviewVerification',
    'Merge-UdpInputs',
    'New-UdpTenantSettingSnapshot',
    'New-UdpHtmlReport',
    'New-UdpStagedExample',
    'Protect-UdpSensitiveText',
    'Read-UdpJson',
    'Resolve-UdpContainedPath',
    'Test-UdpAdminPlanConverged',
    'Test-UdpAzureCreationOperation',
    'Test-UdpOwnershipSnapshot',
    'Test-UdpPlaceholder',
    'Compare-UdpTenantSettingSnapshot',
    'Wait-UdpAdminPlanConvergence',
    'Wait-UdpTenantSettingSnapshotConvergence',
    'Write-UdpEvent'
)
