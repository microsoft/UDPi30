#Requires -Version 7.2

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:AdoResourceId = '499b84ac-1321-427f-aa17-267ca6975798'
$script:BranchControlDefinitionId = '86b05a0c-73e6-4f7d-b3cf-e38f3b39a75b'
$script:TaskCheckTypeId = 'fe1de3ee-a436-41b4-bb20-f6eb4cb879a7'
$script:SecretVariableNames = @('UDP_TEST_SLACK_WEBHOOK', 'PROD_DB_HOST')
$script:OptionalVariableNames = @(
    'UDP_TEST_ADLS_CONNECTION_ID'
    'UDP_TEST_DATA_ENG_GROUP_ID'
    'UDP_TEST_CONTRACTORS_GROUP_ID'
    'UDP_TEST_BI_ADMINS_GROUP_ID'
    'UDP_TEST_PRINCIPAL_MAPPINGS_JSON'
    'UDP_TEST_TEXT_REPLACEMENTS_JSON'
)
$script:AdoAccessToken = $null

function ConvertTo-AdoOrganizationUrl {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$Organization
    )

    $value = $Organization.Trim().TrimEnd('/')
    if ($value -match '^https://dev\.azure\.com/(?<name>[^/]+)$') {
        return "https://dev.azure.com/$($Matches.name)"
    }

    if ($value -match '^[A-Za-z0-9][A-Za-z0-9-]{0,49}$') {
        return "https://dev.azure.com/$value"
    }

    throw "Organization must be an Azure DevOps organization name or an https://dev.azure.com/<organization> URL."
}

function ConvertTo-GitHubRepositorySlug {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$Repository
    )

    $value = $Repository.Trim().TrimEnd('/')
    if ($value -match '^git@github\.com:(?<slug>[^/]+/[^/]+?)(?:\.git)?$') {
        return $Matches.slug
    }

    if ($value -match '^https://github\.com/(?<slug>[^/]+/[^/]+?)(?:\.git)?$') {
        return $Matches.slug
    }

    if ($value -match '^(?<slug>[^/\s]+/[^/\s]+?)(?:\.git)?$') {
        return $Matches.slug
    }

    throw "GitHub repository must be an owner/repository slug or a github.com clone URL."
}

function New-AdoWifServiceEndpointConfiguration {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Name,
        [Parameter(Mandatory)][guid]$ProjectId,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$ProjectName,
        [Parameter(Mandatory)][guid]$SubscriptionId,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$SubscriptionName,
        [Parameter(Mandatory)][guid]$TenantId,
        [Parameter(Mandatory)][guid]$ServicePrincipalAppId
    )

    return [ordered]@{
        data = [ordered]@{
            subscriptionId = $SubscriptionId.ToString()
            subscriptionName = $SubscriptionName
            environment = 'AzureCloud'
            scopeLevel = 'Subscription'
            creationMode = 'Manual'
        }
        name = $Name
        type = 'AzureRM'
        url = 'https://management.azure.com/'
        authorization = [ordered]@{
            parameters = [ordered]@{
                tenantid = $TenantId.ToString()
                serviceprincipalid = $ServicePrincipalAppId.ToString()
            }
            scheme = 'WorkloadIdentityFederation'
        }
        isShared = $false
        isReady = $true
        serviceEndpointProjectReferences = @(
            [ordered]@{
                projectReference = [ordered]@{
                    id = $ProjectId.ToString()
                    name = $ProjectName
                }
                name = $Name
            }
        )
    }
}

function New-AdoBranchControlCheckConfiguration {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('endpoint', 'variablegroup')][string]$ResourceType,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$ResourceId,
        [string]$AllowedBranch = 'refs/heads/main',
        [ValidateRange(1, 2147483647)][int]$TimeoutMinutes = 1440
    )

    return [ordered]@{
        settings = [ordered]@{
            displayName = 'UDP deployment branch control'
            definitionRef = [ordered]@{
                id = $script:BranchControlDefinitionId
                name = 'evaluatebranchProtection'
                version = '0.0.1'
            }
            inputs = [ordered]@{
                allowedBranches = $AllowedBranch
                ensureProtectionOfBranch = 'true'
                allowUnknownStatusBranch = 'false'
            }
        }
        timeout = $TimeoutMinutes
        type = [ordered]@{ id = $script:TaskCheckTypeId }
        resource = [ordered]@{
            type = $ResourceType
            id = $ResourceId
        }
    }
}

function New-AdoPipelinePermissionsPayload {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateRange(1, [int]::MaxValue)][int]$PipelineId,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Resources
    )

    return ,@($Resources | ForEach-Object {
        [ordered]@{
            resource = [ordered]@{
                type = [string]$_.type
                id = [string]$_.id
                name = [string]$_.name
            }
            pipelines = @([ordered]@{ id = $PipelineId; authorized = $true })
        }
    })
}

function Test-UdpConfiguredValue {
    param([AllowNull()][object]$Value)

    if ($null -eq $Value) { return $false }
    $text = [string]$Value
    return -not [string]::IsNullOrWhiteSpace($text) -and
        $text -notmatch '^\$\(.+\)$' -and
        $text -ne '00000000-0000-0000-0000-000000000000'
}

function ConvertTo-UdpRedactedText {
    param([AllowNull()][string]$Text)

    if ([string]::IsNullOrEmpty($Text)) { return $Text }
    $safe = $Text
    foreach ($name in $script:SecretVariableNames) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (Test-UdpConfiguredValue $value) {
            $safe = $safe.Replace($value, "<redacted:$name>", [StringComparison]::Ordinal)
        }
    }
    return $safe
}

function Invoke-UdpNativeCommand {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$FilePath,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Arguments,
        [hashtable]$EnvironmentVariables = @{},
        [ValidateRange(1, 3600)][int]$TimeoutSeconds = 180,
        [switch]$AllowFailure
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    $startInfo.WorkingDirectory = (Get-Location).Path
    foreach ($argument in $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }
    foreach ($entry in $EnvironmentVariables.GetEnumerator()) {
        $startInfo.Environment[[string]$entry.Key] = [string]$entry.Value
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "Could not start '$FilePath'."
        }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            throw "Command '$FilePath' exceeded the $TimeoutSeconds second timeout."
        }
        $process.WaitForExit()
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        $result = [pscustomobject]@{
            ExitCode = $process.ExitCode
            StdOut = $stdout.Trim()
            StdErr = (ConvertTo-UdpRedactedText $stderr.Trim())
        }
        if ($result.ExitCode -ne 0 -and -not $AllowFailure) {
            $operation = @($Arguments | Select-Object -First 3) -join ' '
            throw "Command '$FilePath $operation' failed with exit code $($result.ExitCode): $($result.StdErr)"
        }
        return $result
    }
    finally {
        $process.Dispose()
    }
}

function Invoke-UdpAzJson {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Arguments,
        [hashtable]$EnvironmentVariables = @{},
        [ValidateRange(1, 3600)][int]$TimeoutSeconds = 180
    )

    $result = Invoke-UdpNativeCommand -FilePath 'az' -Arguments @($Arguments + @('--only-show-errors', '--output', 'json')) -EnvironmentVariables $EnvironmentVariables -TimeoutSeconds $TimeoutSeconds
    if ([string]::IsNullOrWhiteSpace($result.StdOut)) { return $null }
    try {
        return $result.StdOut | ConvertFrom-Json -AsHashtable -Depth 100
    }
    catch {
        throw "Azure CLI returned invalid JSON for '$(@($Arguments | Select-Object -First 3) -join ' ')'."
    }
}

function Invoke-UdpAzText {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Arguments,
        [hashtable]$EnvironmentVariables = @{},
        [ValidateRange(1, 3600)][int]$TimeoutSeconds = 180
    )

    return (Invoke-UdpNativeCommand -FilePath 'az' -Arguments @($Arguments + '--only-show-errors') -EnvironmentVariables $EnvironmentVariables -TimeoutSeconds $TimeoutSeconds).StdOut
}

function Invoke-UdpWithTemporaryJson {
    param(
        [Parameter(Mandatory)][object]$Value,
        [Parameter(Mandatory)][scriptblock]$Action
    )

    $path = Join-Path ([IO.Path]::GetTempPath()) "udp-ado-$([Guid]::NewGuid().ToString('N')).json"
    try {
        [IO.File]::WriteAllText($path, ($Value | ConvertTo-Json -Depth 50), [Text.UTF8Encoding]::new($false))
        if (-not $IsWindows) {
            [IO.File]::SetUnixFileMode($path, [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite)
        }
        return & $Action $path
    }
    finally {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Force
        }
    }
}

function Get-UdpAdoAccessToken {
    if (Test-UdpConfiguredValue $script:AdoAccessToken) { return $script:AdoAccessToken }

    $script:AdoAccessToken = Invoke-UdpAzText -Arguments @(
        'account', 'get-access-token',
        '--resource', $script:AdoResourceId,
        '--query', 'accessToken',
        '--output', 'tsv'
    )
    if (-not (Test-UdpConfiguredValue $script:AdoAccessToken)) {
        throw 'Azure CLI did not return an Azure DevOps access token. Run az login with an identity that belongs to the Azure DevOps organization.'
    }
    return $script:AdoAccessToken
}

function Invoke-AdoRest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('GET', 'POST', 'PATCH', 'PUT')][string]$Method,
        [Parameter(Mandatory)][uri]$Uri,
        [AllowNull()][object]$Body,
        [ValidateRange(1, 5)][int]$MaximumAttempts = 4
    )

    $headers = @{ Authorization = "Bearer $(Get-UdpAdoAccessToken)" }
    for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
        try {
            $parameters = @{
                Method = $Method
                Uri = $Uri
                Headers = $headers
                ContentType = 'application/json'
            }
            if ($null -ne $Body) {
                $parameters.Body = $Body | ConvertTo-Json -Depth 50 -Compress
            }
            return Invoke-RestMethod @parameters
        }
        catch {
            $statusCode = 0
            if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
                $statusCode = [int]$_.Exception.Response.StatusCode
            }
            if ($attempt -lt $MaximumAttempts -and $statusCode -in @(429, 502, 503, 504)) {
                Start-Sleep -Seconds ([Math]::Pow(2, $attempt - 1))
                continue
            }
            throw "Azure DevOps REST $Method request to '$($Uri.AbsolutePath)' failed with HTTP status ${statusCode}: $($_.Exception.Message)"
        }
    }
}

function Resolve-UdpRepositoryPath {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$RelativePath,
        [switch]$RequireFile
    )

    if ([IO.Path]::IsPathRooted($RelativePath)) {
        throw "Repository path '$RelativePath' must be relative."
    }
    $root = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $candidate = [IO.Path]::GetFullPath((Join-Path $root $RelativePath))
    if (-not $candidate.StartsWith("$root$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase)) {
        throw "Repository path '$RelativePath' escapes the repository root."
    }
    if ($RequireFile -and -not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "Repository file '$RelativePath' does not exist."
    }
    return $candidate
}

function Get-UdpGitHubRepositorySlug {
    param([string]$Repository)

    if (Test-UdpConfiguredValue $Repository) {
        return ConvertTo-GitHubRepositorySlug $Repository
    }
    $remote = (Invoke-UdpNativeCommand -FilePath 'git' -Arguments @('config', '--get', 'remote.origin.url')).StdOut
    return ConvertTo-GitHubRepositorySlug $remote
}

function Ensure-UdpAzureDevOpsExtension {
    param([Parameter(Mandatory)][System.Management.Automation.PSCmdlet]$Cmdlet)

    $probe = Invoke-UdpNativeCommand -FilePath 'az' -Arguments @('extension', 'show', '--name', 'azure-devops', '--only-show-errors', '--output', 'json') -AllowFailure
    if ($probe.ExitCode -eq 0) { return $true }
    if (-not $Cmdlet.ShouldProcess('Azure CLI', 'Install the azure-devops extension')) { return $false }
    [void](Invoke-UdpAzJson -Arguments @('extension', 'add', '--name', 'azure-devops', '--yes'))
    return $true
}

function Get-AdoServiceEndpoints {
    param(
        [Parameter(Mandatory)][string]$OrganizationUrl,
        [Parameter(Mandatory)][string]$Project
    )

    return ,@(Invoke-UdpAzJson -Arguments @('devops', 'service-endpoint', 'list', '--org', $OrganizationUrl, '--project', $Project))
}

function Resolve-AdoGitHubEndpoint {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Endpoints,
        [Parameter(Mandatory)][string]$RepositorySlug,
        [string]$ConnectionName
    )

    $githubEndpoints = @($Endpoints | Where-Object { [string]$_.type -ieq 'github' -and $_.isReady -ne $false })
    if (Test-UdpConfiguredValue $ConnectionName) {
        $endpointMatches = @($githubEndpoints | Where-Object { [string]$_.name -ieq $ConnectionName })
    }
    else {
        $owner = $RepositorySlug.Split('/')[0]
        $endpointMatches = @($githubEndpoints | Where-Object { [string]$_.name -ieq $owner })
        if ($endpointMatches.Count -eq 0 -and $githubEndpoints.Count -eq 1) {
            $endpointMatches = $githubEndpoints
        }
    }
    if ($endpointMatches.Count -eq 1) { return $endpointMatches[0] }
    if ($endpointMatches.Count -gt 1) {
        throw 'Multiple ready GitHub service connections matched. Specify -GitHubServiceConnectionName.'
    }
    throw "No ready GitHub service connection was found for '$RepositorySlug'. Install and authorize the Azure Pipelines GitHub App, then rerun with -GitHubServiceConnectionName."
}

function Get-UdpServicePrincipal {
    param(
        [Parameter(Mandatory)][string]$DisplayName,
        [guid]$AppId = [guid]::Empty,
        [Parameter(Mandatory)][System.Management.Automation.PSCmdlet]$Cmdlet
    )

    if ($AppId -ne [guid]::Empty) {
        return Invoke-UdpAzJson -Arguments @('ad', 'sp', 'show', '--id', $AppId.ToString())
    }

    $applications = @(Invoke-UdpAzJson -Arguments @('ad', 'app', 'list', '--display-name', $DisplayName))
    $applications = @($applications | Where-Object { [string]$_.displayName -ceq $DisplayName })
    if ($applications.Count -gt 1) {
        throw "Multiple app registrations have the exact display name '$DisplayName'. Specify -ServicePrincipalAppId."
    }
    if ($applications.Count -eq 1) {
        return Invoke-UdpAzJson -Arguments @('ad', 'sp', 'show', '--id', [string]$applications[0].appId)
    }

    if (-not $Cmdlet.ShouldProcess("Microsoft Entra app '$DisplayName'", 'Create app registration and service principal without a password')) {
        return $null
    }
    $application = Invoke-UdpAzJson -Arguments @('ad', 'app', 'create', '--display-name', $DisplayName)
    return Invoke-UdpAzJson -Arguments @('ad', 'sp', 'create', '--id', [string]$application.appId)
}

function Ensure-UdpFederatedCredential {
    param(
        [Parameter(Mandatory)][guid]$AppId,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Issuer,
        [Parameter(Mandatory)][string]$Subject,
        [Parameter(Mandatory)][System.Management.Automation.PSCmdlet]$Cmdlet
    )

    $credentials = @(Invoke-UdpAzJson -Arguments @('ad', 'app', 'federated-credential', 'list', '--id', $AppId.ToString()))
    $existing = @($credentials | Where-Object { [string]$_.name -ceq $Name })
    if ($existing.Count -gt 1) { throw "Multiple federated credentials are named '$Name'." }
    if ($existing.Count -eq 1) {
        $audiences = @($existing[0].audiences)
        if ([string]$existing[0].issuer -ne $Issuer -or [string]$existing[0].subject -ne $Subject -or $audiences -notcontains 'api://AzureADTokenExchange') {
            throw "Federated credential '$Name' exists with different issuer, subject, or audience. Refusing to replace it automatically."
        }
        return $existing[0]
    }

    if (-not $Cmdlet.ShouldProcess("Federated credential '$Name'", 'Create for the Azure DevOps service connection')) { return $null }
    $configuration = [ordered]@{
        name = $Name
        issuer = $Issuer
        subject = $Subject
        audiences = @('api://AzureADTokenExchange')
    }
    return Invoke-UdpWithTemporaryJson -Value $configuration -Action {
        param($path)
        Invoke-UdpAzJson -Arguments @('ad', 'app', 'federated-credential', 'create', '--id', $AppId.ToString(), '--parameters', $path)
    }
}

function Ensure-UdpRoleAssignment {
    param(
        [Parameter(Mandatory)][guid]$PrincipalObjectId,
        [string]$RoleDefinitionName,
        [string]$Scope,
        [Parameter(Mandatory)][System.Management.Automation.PSCmdlet]$Cmdlet
    )

    if (-not (Test-UdpConfiguredValue $RoleDefinitionName)) { return $null }
    if (-not (Test-UdpConfiguredValue $Scope) -or $Scope -notmatch '^/subscriptions/[0-9a-fA-F-]{36}(?:/|$)') {
        throw '-AzureRoleScope must be an explicit Azure resource ID when -AzureRoleDefinitionName is supplied.'
    }
    $assignments = @(Invoke-UdpAzJson -Arguments @(
        'role', 'assignment', 'list',
        '--assignee-object-id', $PrincipalObjectId.ToString(),
        '--scope', $Scope,
        '--include-inherited'
    ))
    $existing = @($assignments | Where-Object { [string]$_.roleDefinitionName -ieq $RoleDefinitionName })
    if ($existing.Count -gt 0) { return $existing[0] }
    if (-not $Cmdlet.ShouldProcess("$PrincipalObjectId at $Scope", "Assign Azure role '$RoleDefinitionName'")) { return $null }
    return Invoke-UdpAzJson -Arguments @(
        'role', 'assignment', 'create',
        '--assignee-object-id', $PrincipalObjectId.ToString(),
        '--assignee-principal-type', 'ServicePrincipal',
        '--role', $RoleDefinitionName,
        '--scope', $Scope
    )
}

function Ensure-AdoWifEndpoint {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Endpoints,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][System.Collections.IDictionary]$ProjectInfo,
        [Parameter(Mandatory)][System.Collections.IDictionary]$AccountInfo,
        [Parameter(Mandatory)][string]$OrganizationUrl,
        [guid]$ServicePrincipalAppId = [guid]::Empty,
        [Parameter(Mandatory)][string]$ServicePrincipalDisplayName,
        [string]$AzureRoleDefinitionName,
        [string]$AzureRoleScope,
        [Parameter(Mandatory)][System.Management.Automation.PSCmdlet]$Cmdlet
    )

    $endpointMatches = @($Endpoints | Where-Object { [string]$_.name -ieq $Name })
    if ($endpointMatches.Count -gt 1) { throw "Multiple service connections are named '$Name'." }
    if ($endpointMatches.Count -eq 1) {
        $endpoint = $endpointMatches[0]
        if ([string]$endpoint.type -ine 'AzureRM' -or [string]$endpoint.authorization.scheme -ine 'WorkloadIdentityFederation') {
            throw "Service connection '$Name' exists but is not an AzureRM workload identity federation connection."
        }
        $appId = [guid][string]$endpoint.authorization.parameters.serviceprincipalid
        if ($ServicePrincipalAppId -ne [guid]::Empty -and $appId -ne $ServicePrincipalAppId) {
            throw "Service connection '$Name' uses a different service principal than -ServicePrincipalAppId."
        }
        $principal = Invoke-UdpAzJson -Arguments @('ad', 'sp', 'show', '--id', $appId.ToString())
        [void](Ensure-UdpRoleAssignment -PrincipalObjectId ([guid][string]$principal.id) -RoleDefinitionName $AzureRoleDefinitionName -Scope $AzureRoleScope -Cmdlet $Cmdlet)
        return $endpoint
    }

    $principal = Get-UdpServicePrincipal -DisplayName $ServicePrincipalDisplayName -AppId $ServicePrincipalAppId -Cmdlet $Cmdlet
    if ($null -eq $principal) { return $null }
    $configuration = New-AdoWifServiceEndpointConfiguration `
        -Name $Name `
        -ProjectId ([guid][string]$ProjectInfo.id) `
        -ProjectName ([string]$ProjectInfo.name) `
        -SubscriptionId ([guid][string]$AccountInfo.id) `
        -SubscriptionName ([string]$AccountInfo.name) `
        -TenantId ([guid][string]$AccountInfo.tenantId) `
        -ServicePrincipalAppId ([guid][string]$principal.appId)

    if (-not $Cmdlet.ShouldProcess("Azure DevOps service connection '$Name'", 'Create workload identity federation connection')) { return $null }
    $adoOrganizationUrl = $OrganizationUrl
    $endpoint = Invoke-UdpWithTemporaryJson -Value $configuration -Action {
        param($path)
        Invoke-UdpAzJson -Arguments @(
            'devops', 'service-endpoint', 'create',
            '--service-endpoint-configuration', $path,
            '--org', $adoOrganizationUrl,
            '--project', [string]$ProjectInfo.name
        )
    }
    $issuer = [string]$endpoint.authorization.parameters.workloadIdentityFederationIssuer
    $subject = [string]$endpoint.authorization.parameters.workloadIdentityFederationSubject
    if (-not (Test-UdpConfiguredValue $issuer) -or -not (Test-UdpConfiguredValue $subject)) {
        throw "Azure DevOps created '$Name' without returning the workload identity issuer and subject."
    }
    $credentialName = "ado-$($ProjectInfo.id)-$($endpoint.id)"
    [void](Ensure-UdpFederatedCredential -AppId ([guid][string]$principal.appId) -Name $credentialName -Issuer $issuer -Subject $subject -Cmdlet $Cmdlet)
    [void](Ensure-UdpRoleAssignment -PrincipalObjectId ([guid][string]$principal.id) -RoleDefinitionName $AzureRoleDefinitionName -Scope $AzureRoleScope -Cmdlet $Cmdlet)
    return $endpoint
}

function Ensure-AdoPipeline {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Folder,
        [Parameter(Mandatory)][string]$YamlPath,
        [Parameter(Mandatory)][string]$Branch,
        [Parameter(Mandatory)][string]$RepositorySlug,
        [Parameter(Mandatory)][string]$GitHubEndpointId,
        [Parameter(Mandatory)][string]$OrganizationUrl,
        [Parameter(Mandatory)][string]$Project,
        [Parameter(Mandatory)][System.Management.Automation.PSCmdlet]$Cmdlet
    )

    $pipelines = @(Invoke-UdpAzJson -Arguments @('pipelines', 'list', '--name', $Name, '--org', $OrganizationUrl, '--project', $Project, '--top', '100'))
    $pipelineMatches = @($pipelines | Where-Object { [string]$_.name -ceq $Name -and ([string]$_.path -eq $Folder -or -not $_.Contains('path')) })
    if ($pipelineMatches.Count -gt 1) { throw "Multiple pipelines match '$Folder/$Name'." }
    if ($pipelineMatches.Count -eq 1) {
        $pipeline = Invoke-UdpAzJson -Arguments @('pipelines', 'show', '--id', [string]$pipelineMatches[0].id, '--org', $OrganizationUrl, '--project', $Project)
        if ($pipeline.repository -and [string]$pipeline.repository.name -ine $RepositorySlug) {
            throw "Pipeline '$Name' points to '$($pipeline.repository.name)', not '$RepositorySlug'."
        }
        $currentYaml = if ($pipeline.process) { [string]$pipeline.process.yamlFilename } else { '' }
        if (Test-UdpConfiguredValue $currentYaml -and $currentYaml.TrimStart('/') -ne $YamlPath.TrimStart('/')) {
            if ($Cmdlet.ShouldProcess("Azure DevOps pipeline '$Name'", "Update YAML path to '$YamlPath'")) {
                [void](Invoke-UdpAzJson -Arguments @('pipelines', 'update', '--id', [string]$pipeline.id, '--branch', $Branch, '--yaml-path', $YamlPath, '--org', $OrganizationUrl, '--project', $Project))
                $pipeline = Invoke-UdpAzJson -Arguments @('pipelines', 'show', '--id', [string]$pipeline.id, '--org', $OrganizationUrl, '--project', $Project)
            }
        }
        return $pipeline
    }

    if (-not $Cmdlet.ShouldProcess("Azure DevOps pipeline '$Name'", "Create from $RepositorySlug/$YamlPath")) { return $null }
    $arguments = @(
        'pipelines', 'create',
        '--name', $Name,
        '--repository', $RepositorySlug,
        '--repository-type', 'github',
        '--service-connection', $GitHubEndpointId,
        '--branch', $Branch,
        '--yaml-path', $YamlPath,
        '--skip-first-run', 'true',
        '--org', $OrganizationUrl,
        '--project', $Project
    )
    if ($Folder -ne '\') { $arguments += @('--folder-path', $Folder) }
    return Invoke-UdpAzJson -Arguments $arguments
}

function Get-UdpVariableGroupValues {
    param(
        [Parameter(Mandatory)][string]$AzureServiceConnectionName,
        [Parameter(Mandatory)][guid]$SubscriptionId,
        [Parameter(Mandatory)][guid]$TenantId,
        [guid]$FabricCapacityId = [guid]::Empty,
        [hashtable]$Variable = @{}
    )

    $values = [ordered]@{
        UDP_AZURE_SERVICE_CONNECTION = $AzureServiceConnectionName
        UDP_TEST_AZURE_SUBSCRIPTION_ID = $SubscriptionId.ToString()
        UDP_TEST_ENTRA_TENANT_ID = $TenantId.ToString()
    }
    if ($FabricCapacityId -ne [guid]::Empty) {
        $values.UDP_TEST_FABRIC_CAPACITY_ID = $FabricCapacityId.ToString()
    }
    elseif (Test-UdpConfiguredValue $env:UDP_TEST_FABRIC_CAPACITY_ID) {
        $capacityId = [guid]::Empty
        if (-not [guid]::TryParse($env:UDP_TEST_FABRIC_CAPACITY_ID, [ref]$capacityId) -or $capacityId -eq [guid]::Empty) {
            throw 'UDP_TEST_FABRIC_CAPACITY_ID must be a non-empty GUID.'
        }
        $values.UDP_TEST_FABRIC_CAPACITY_ID = $capacityId.ToString()
    }
    foreach ($name in $script:OptionalVariableNames) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (Test-UdpConfiguredValue $value) { $values[$name] = $value }
    }
    foreach ($entry in $Variable.GetEnumerator()) {
        $name = [string]$entry.Key
        if ($name -match '(?i)(secret|token|password|api[_-]?key|webhook|connection[_-]?string)' -or $name -in $script:SecretVariableNames) {
            throw "Variable '$name' appears sensitive. Supply it through an environment variable instead of -Variable."
        }
        if (Test-UdpConfiguredValue $entry.Value) { $values[$name] = [string]$entry.Value }
    }
    return $values
}

function Set-AdoVariableGroupVariable {
    param(
        [Parameter(Mandatory)][int]$GroupId,
        [Parameter(Mandatory)][string]$Name,
        [AllowNull()][string]$Value,
        [Parameter(Mandatory)][bool]$Secret,
        [Parameter(Mandatory)][bool]$Exists,
        [Parameter(Mandatory)][string]$OrganizationUrl,
        [Parameter(Mandatory)][string]$Project,
        [Parameter(Mandatory)][System.Management.Automation.PSCmdlet]$Cmdlet
    )

    $verb = if ($Exists) { 'update' } else { 'create' }
    if (-not $Cmdlet.ShouldProcess("Variable '$Name' in group $GroupId", "$verb variable")) { return }
    $arguments = @(
        'pipelines', 'variable-group', 'variable', $verb,
        '--group-id', [string]$GroupId,
        '--name', $Name,
        '--secret', $Secret.ToString().ToLowerInvariant(),
        '--org', $OrganizationUrl,
        '--project', $Project
    )
    $environmentVariables = @{}
    if ($Secret) {
        $environmentVariables["AZURE_DEVOPS_EXT_PIPELINE_VAR_$Name"] = $Value
    }
    else {
        $arguments += @('--value', $Value)
    }
    [void](Invoke-UdpAzJson -Arguments $arguments -EnvironmentVariables $environmentVariables)
}

function Ensure-AdoVariableGroup {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][System.Collections.IDictionary]$Values,
        [Parameter(Mandatory)][string]$OrganizationUrl,
        [Parameter(Mandatory)][string]$Project,
        [switch]$UpdateSecrets,
        [Parameter(Mandatory)][System.Management.Automation.PSCmdlet]$Cmdlet
    )

    $groups = @(Invoke-UdpAzJson -Arguments @('pipelines', 'variable-group', 'list', '--org', $OrganizationUrl, '--project', $Project))
    $groupMatches = @($groups | Where-Object { [string]$_.name -ceq $Name })
    if ($groupMatches.Count -gt 1) { throw "Multiple variable groups are named '$Name'." }
    if ($groupMatches.Count -eq 0) {
        if (-not $Cmdlet.ShouldProcess("Azure DevOps variable group '$Name'", 'Create protected deployment variable group')) { return $null }
        $seedName = 'UDP_AZURE_SERVICE_CONNECTION'
        $group = Invoke-UdpAzJson -Arguments @(
            'pipelines', 'variable-group', 'create',
            '--name', $Name,
            '--description', 'Protected inputs for the UDP deployment example harness.',
            '--authorize', 'false',
            '--variables', "$seedName=$($Values[$seedName])",
            '--org', $OrganizationUrl,
            '--project', $Project
        )
    }
    else {
        $group = $groupMatches[0]
    }

    $variables = Invoke-UdpAzJson -Arguments @('pipelines', 'variable-group', 'variable', 'list', '--group-id', [string]$group.id, '--org', $OrganizationUrl, '--project', $Project)
    foreach ($entry in $Values.GetEnumerator()) {
        $exists = $variables.Contains([string]$entry.Key)
        $current = if ($exists) { [string]$variables[[string]$entry.Key].value } else { $null }
        if (-not $exists -or $current -ne [string]$entry.Value) {
            Set-AdoVariableGroupVariable -GroupId ([int]$group.id) -Name ([string]$entry.Key) -Value ([string]$entry.Value) -Secret $false -Exists $exists -OrganizationUrl $OrganizationUrl -Project $Project -Cmdlet $Cmdlet
        }
    }
    foreach ($secretName in $script:SecretVariableNames) {
        $secretValue = [Environment]::GetEnvironmentVariable($secretName)
        if (-not (Test-UdpConfiguredValue $secretValue)) { continue }
        $exists = $variables.Contains($secretName)
        if (-not $exists -or $UpdateSecrets) {
            Set-AdoVariableGroupVariable -GroupId ([int]$group.id) -Name $secretName -Value $secretValue -Secret $true -Exists $exists -OrganizationUrl $OrganizationUrl -Project $Project -Cmdlet $Cmdlet
        }
    }
    return Invoke-UdpAzJson -Arguments @('pipelines', 'variable-group', 'show', '--group-id', [string]$group.id, '--org', $OrganizationUrl, '--project', $Project)
}

function Ensure-AdoPipelinePermissions {
    param(
        [Parameter(Mandatory)][int]$PipelineId,
        [Parameter(Mandatory)][object[]]$Resources,
        [Parameter(Mandatory)][string]$OrganizationUrl,
        [Parameter(Mandatory)][string]$Project,
        [Parameter(Mandatory)][System.Management.Automation.PSCmdlet]$Cmdlet
    )

    if (-not $Cmdlet.ShouldProcess("Pipeline $PipelineId", 'Authorize only this pipeline for protected resources')) { return }
    $projectSegment = [Uri]::EscapeDataString($Project)
    $uri = [uri]"$OrganizationUrl/$projectSegment/_apis/pipelines/pipelinepermissions?api-version=7.1-preview.1"
    [void](Invoke-AdoRest -Method PATCH -Uri $uri -Body (New-AdoPipelinePermissionsPayload -PipelineId $PipelineId -Resources $Resources))
}

function Ensure-AdoBranchControlCheck {
    param(
        [Parameter(Mandatory)][ValidateSet('endpoint', 'variablegroup')][string]$ResourceType,
        [Parameter(Mandatory)][string]$ResourceId,
        [Parameter(Mandatory)][string]$ResourceName,
        [Parameter(Mandatory)][string]$AllowedBranch,
        [Parameter(Mandatory)][string]$OrganizationUrl,
        [Parameter(Mandatory)][string]$Project,
        [Parameter(Mandatory)][System.Management.Automation.PSCmdlet]$Cmdlet
    )

    $projectSegment = [Uri]::EscapeDataString($Project)
    $resourceTypeSegment = [Uri]::EscapeDataString($ResourceType)
    $resourceIdSegment = [Uri]::EscapeDataString($ResourceId)
    $uri = [uri]"$OrganizationUrl/$projectSegment/_apis/pipelines/checks/configurations?resourceType=$resourceTypeSegment&resourceId=$resourceIdSegment&%24expand=settings&api-version=7.1-preview.1"
    $response = Invoke-AdoRest -Method GET -Uri $uri
    $checks = @($response.value | Where-Object { [string]$_.settings.definitionRef.id -ieq $script:BranchControlDefinitionId })
    if ($checks.Count -gt 1) { throw "Protected resource '$ResourceName' has duplicate branch control checks." }
    if ($checks.Count -eq 1) {
        $inputs = $checks[0].settings.inputs
        if ([string]$inputs.allowedBranches -ne $AllowedBranch -or [string]$inputs.ensureProtectionOfBranch -ne 'true' -or [string]$inputs.allowUnknownStatusBranch -ne 'false') {
            throw "Branch control on '$ResourceName' does not require protected '$AllowedBranch' with unknown status denied. Refusing to overwrite it automatically."
        }
        return $checks[0]
    }
    if (-not $Cmdlet.ShouldProcess("Branch control on '$ResourceName'", "Require protected $AllowedBranch")) { return $null }
    $body = New-AdoBranchControlCheckConfiguration -ResourceType $ResourceType -ResourceId $ResourceId -AllowedBranch $AllowedBranch
    return Invoke-AdoRest -Method POST -Uri ([uri]"$OrganizationUrl/$projectSegment/_apis/pipelines/checks/configurations?api-version=7.1-preview.1") -Body $body
}

function Wait-UdpPipelineRun {
    param(
        [Parameter(Mandatory)][int]$RunId,
        [Parameter(Mandatory)][string]$OrganizationUrl,
        [Parameter(Mandatory)][string]$Project,
        [ValidateRange(1, 1440)][int]$TimeoutMinutes,
        [ValidateRange(5, 300)][int]$PollSeconds
    )

    $deadline = [DateTimeOffset]::UtcNow.AddMinutes($TimeoutMinutes)
    do {
        $run = Invoke-UdpAzJson -Arguments @('pipelines', 'runs', 'show', '--id', [string]$RunId, '--org', $OrganizationUrl, '--project', $Project)
        if ([string]$run.status -ieq 'completed') { return $run }
        if ([DateTimeOffset]::UtcNow -ge $deadline) {
            throw "Pipeline run $RunId did not complete within $TimeoutMinutes minutes."
        }
        Start-Sleep -Seconds $PollSeconds
    } while ($true)
}

function Get-UdpHarnessEvidence {
    param(
        [Parameter(Mandatory)][int]$RunId,
        [Parameter(Mandatory)][string]$Destination,
        [Parameter(Mandatory)][string]$OrganizationUrl,
        [Parameter(Mandatory)][string]$Project
    )

    $artifactName = "deployment-example-report-$RunId"
    $artifacts = @(Invoke-UdpAzJson -Arguments @('pipelines', 'runs', 'artifact', 'list', '--run-id', [string]$RunId, '--org', $OrganizationUrl, '--project', $Project))
    if (@($artifacts | Where-Object { [string]$_.name -eq $artifactName }).Count -eq 0) {
        return [ordered]@{ artifactName = $artifactName; downloaded = $false; summaryPath = $null; failures = @() }
    }
    [void](New-Item -ItemType Directory -Path $Destination -Force)
    [void](Invoke-UdpAzText -Arguments @('pipelines', 'runs', 'artifact', 'download', '--run-id', [string]$RunId, '--artifact-name', $artifactName, '--path', $Destination, '--org', $OrganizationUrl, '--project', $Project) -TimeoutSeconds 1800)
    $summaryFile = @(Get-ChildItem -LiteralPath $Destination -Filter summary.json -File -Recurse | Select-Object -First 1)
    if ($summaryFile.Count -eq 0) {
        return [ordered]@{ artifactName = $artifactName; downloaded = $true; summaryPath = $null; failures = @() }
    }
    $summary = Get-Content -LiteralPath $summaryFile[0].FullName -Raw | ConvertFrom-Json -AsHashtable -Depth 100
    $failures = @($summary.results | Where-Object { [string]$_.status -in @('Failed', 'Blocked') } | ForEach-Object {
        [ordered]@{
            id = [string]$_.id
            name = [string]$_.name
            status = [string]$_.status
            failedPhases = @($_.phases | Where-Object { [string]$_.status -eq 'Failed' } | ForEach-Object { [string]$_.name })
            requirements = @($_.requirements | ForEach-Object { [string]$_.name })
        }
    })
    return [ordered]@{
        artifactName = $artifactName
        downloaded = $true
        summaryPath = $summaryFile[0].FullName
        counts = $summary.counts
        fatalError = $summary.fatalError
        failures = $failures
    }
}

function Save-UdpBootstrapResult {
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$Result,
        [Parameter(Mandatory)][string]$OutputDirectory,
        [Parameter(Mandatory)][System.Management.Automation.PSCmdlet]$Cmdlet
    )

    $path = Join-Path $OutputDirectory 'bootstrap-summary.json'
    if ($Cmdlet.ShouldProcess($path, 'Write bootstrap summary without secret values')) {
        [void](New-Item -ItemType Directory -Path $OutputDirectory -Force)
        $Result | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath $path -Encoding utf8
    }
    return $path
}

function Initialize-UdpAzureDevOpsExamples {
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

    foreach ($command in @('az', 'git')) {
        if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
            throw "Required command '$command' was not found."
        }
    }
    [void](Resolve-UdpRepositoryPath -RepositoryRoot $RepositoryRoot -RelativePath $PipelineYamlPath -RequireFile)
    if (Test-UdpConfiguredValue $InputsPath) {
        [void](Resolve-UdpRepositoryPath -RepositoryRoot $RepositoryRoot -RelativePath $InputsPath -RequireFile)
    }
    foreach ($id in $ExampleId) {
        if ($id -notmatch '^(0[1-9]|1[0-4])$') { throw "Invalid example ID '$id'. Expected 01 through 14." }
    }

    $organizationUrl = ConvertTo-AdoOrganizationUrl $Organization
    $repositorySlug = Get-UdpGitHubRepositorySlug -Repository $GitHubRepository
    $branchRef = if ($Branch.StartsWith('refs/heads/', [StringComparison]::OrdinalIgnoreCase)) { $Branch } else { "refs/heads/$Branch" }
    if ($Mode -in @('Plan', 'Deploy') -and $branchRef -ne 'refs/heads/main') {
        throw 'Plan and Deploy may be queued only from refs/heads/main.'
    }
    if ((Test-UdpConfiguredValue $AzureRoleDefinitionName) -xor (Test-UdpConfiguredValue $AzureRoleScope)) {
        throw '-AzureRoleDefinitionName and -AzureRoleScope must be supplied together.'
    }

    if (-not (Ensure-UdpAzureDevOpsExtension -Cmdlet $PSCmdlet)) {
        return [ordered]@{ status = 'Planned'; reason = 'azure-devops extension would be installed'; organization = $organizationUrl; project = $Project }
    }
    $accountArguments = @('account', 'show')
    if ($AzureSubscriptionId -ne [guid]::Empty) { $accountArguments += @('--subscription', $AzureSubscriptionId.ToString()) }
    $accountInfo = Invoke-UdpAzJson -Arguments $accountArguments
    if ($TenantId -ne [guid]::Empty -and [guid][string]$accountInfo.tenantId -ne $TenantId) {
        throw "Azure subscription '$($accountInfo.id)' belongs to tenant '$($accountInfo.tenantId)', not '$TenantId'."
    }
    $projectInfo = Invoke-UdpAzJson -Arguments @('devops', 'project', 'show', '--project', $Project, '--org', $organizationUrl)
    if ([string]$projectInfo.state -ine 'wellFormed') {
        throw "Azure DevOps project '$Project' is not in the wellFormed state."
    }
    if (-not (Test-UdpConfiguredValue $ServicePrincipalDisplayName)) {
        $safeProject = [regex]::Replace([string]$projectInfo.name, '[^A-Za-z0-9-]', '-').Trim('-')
        $ServicePrincipalDisplayName = "ado-$safeProject-udp-deployment-tests"
    }
    if ($Mode -in @('Plan', 'Deploy') -and $FabricCapacityId -eq [guid]::Empty -and -not (Test-UdpConfiguredValue $env:UDP_TEST_FABRIC_CAPACITY_ID)) {
        throw 'Plan and Deploy require -FabricCapacityId or UDP_TEST_FABRIC_CAPACITY_ID.'
    }
    if (-not (Test-UdpConfiguredValue $OutputDirectory)) {
        $OutputDirectory = Join-Path $RepositoryRoot "deployment-test-artifacts/bootstrap-$([DateTimeOffset]::UtcNow.ToString('yyyyMMddHHmmss'))"
    }

    $result = [ordered]@{
        schemaVersion = '1.0'
        status = 'Configuring'
        organization = $organizationUrl
        project = [ordered]@{ id = [string]$projectInfo.id; name = [string]$projectInfo.name; visibility = [string]$projectInfo.visibility }
        repository = $repositorySlug
        azure = [ordered]@{ subscriptionId = [string]$accountInfo.id; subscriptionName = [string]$accountInfo.name; tenantId = [string]$accountInfo.tenantId; roleManaged = (Test-UdpConfiguredValue $AzureRoleDefinitionName) }
        resources = [ordered]@{}
        configuredVariableNames = @()
        configuredSecretNames = @()
        branchControl = -not $SkipBranchControl
        run = $null
        evidence = $null
        outputPath = $null
    }

    try {
        $endpoints = Get-AdoServiceEndpoints -OrganizationUrl $organizationUrl -Project ([string]$projectInfo.name)
        $githubEndpoint = Resolve-AdoGitHubEndpoint -Endpoints $endpoints -RepositorySlug $repositorySlug -ConnectionName $GitHubServiceConnectionName
        $result.resources.githubServiceConnection = [ordered]@{ id = [string]$githubEndpoint.id; name = [string]$githubEndpoint.name; type = [string]$githubEndpoint.type }

        $azureEndpoint = Ensure-AdoWifEndpoint -Endpoints $endpoints -Name $AzureServiceConnectionName -ProjectInfo $projectInfo -AccountInfo $accountInfo -OrganizationUrl $organizationUrl -ServicePrincipalAppId $ServicePrincipalAppId -ServicePrincipalDisplayName $ServicePrincipalDisplayName -AzureRoleDefinitionName $AzureRoleDefinitionName -AzureRoleScope $AzureRoleScope -Cmdlet $PSCmdlet
        if ($null -eq $azureEndpoint) {
            $result.status = 'Planned'
            return $result
        }
        $result.resources.azureServiceConnection = [ordered]@{ id = [string]$azureEndpoint.id; name = [string]$azureEndpoint.name; scheme = [string]$azureEndpoint.authorization.scheme }

        $pipeline = Ensure-AdoPipeline -Name $PipelineName -Folder $PipelineFolder -YamlPath $PipelineYamlPath -Branch $branchRef -RepositorySlug $repositorySlug -GitHubEndpointId ([string]$githubEndpoint.id) -OrganizationUrl $organizationUrl -Project ([string]$projectInfo.name) -Cmdlet $PSCmdlet
        if ($null -eq $pipeline) {
            $result.status = 'Planned'
            return $result
        }
        $result.resources.pipeline = [ordered]@{ id = [int]$pipeline.id; name = [string]$pipeline.name; folder = $PipelineFolder; yamlPath = $PipelineYamlPath }

        $variableValues = Get-UdpVariableGroupValues -AzureServiceConnectionName $AzureServiceConnectionName -SubscriptionId ([guid][string]$accountInfo.id) -TenantId ([guid][string]$accountInfo.tenantId) -FabricCapacityId $FabricCapacityId -Variable $Variable
        $variableGroup = Ensure-AdoVariableGroup -Name $VariableGroupName -Values $variableValues -OrganizationUrl $organizationUrl -Project ([string]$projectInfo.name) -UpdateSecrets:$UpdateSecrets -Cmdlet $PSCmdlet
        if ($null -eq $variableGroup) {
            $result.status = 'Planned'
            return $result
        }
        $result.resources.variableGroup = [ordered]@{ id = [int]$variableGroup.id; name = [string]$variableGroup.name }
        $result.configuredVariableNames = @($variableValues.Keys)
        $result.configuredSecretNames = @($script:SecretVariableNames | Where-Object { Test-UdpConfiguredValue ([Environment]::GetEnvironmentVariable($_)) })

        $protectedResources = @(
            [ordered]@{ type = 'endpoint'; id = [string]$githubEndpoint.id; name = [string]$githubEndpoint.name }
            [ordered]@{ type = 'endpoint'; id = [string]$azureEndpoint.id; name = [string]$azureEndpoint.name }
            [ordered]@{ type = 'variablegroup'; id = [string]$variableGroup.id; name = [string]$variableGroup.name }
        )
        Ensure-AdoPipelinePermissions -PipelineId ([int]$pipeline.id) -Resources $protectedResources -OrganizationUrl $organizationUrl -Project ([string]$projectInfo.name) -Cmdlet $PSCmdlet
        if (-not $SkipBranchControl) {
            [void](Ensure-AdoBranchControlCheck -ResourceType endpoint -ResourceId ([string]$azureEndpoint.id) -ResourceName ([string]$azureEndpoint.name) -AllowedBranch 'refs/heads/main' -OrganizationUrl $organizationUrl -Project ([string]$projectInfo.name) -Cmdlet $PSCmdlet)
            [void](Ensure-AdoBranchControlCheck -ResourceType variablegroup -ResourceId ([string]$variableGroup.id) -ResourceName ([string]$variableGroup.name) -AllowedBranch 'refs/heads/main' -OrganizationUrl $organizationUrl -Project ([string]$projectInfo.name) -Cmdlet $PSCmdlet)
        }

        $result.status = 'Configured'
        if (-not $SkipRun) {
            if ($PSCmdlet.ShouldProcess("Azure DevOps pipeline '$PipelineName'", "Queue $Mode for $branchRef")) {
                $exampleIds = $ExampleId -join ','
                $runArguments = @(
                    'pipelines', 'run',
                    '--id', [string]$pipeline.id,
                    '--branch', $branchRef,
                    '--parameters',
                    "mode=$Mode",
                    "exampleIds=$exampleIds",
                    "inputsPath=$InputsPath",
                    "keepResources=$($KeepResources.IsPresent.ToString().ToLowerInvariant())",
                    "allowTenantAdminChanges=$($AllowTenantAdminChanges.IsPresent.ToString().ToLowerInvariant())",
                    '--org', $organizationUrl,
                    '--project', [string]$projectInfo.name
                )
                $queuedRun = Invoke-UdpAzJson -Arguments $runArguments
                $result.run = [ordered]@{ id = [int]$queuedRun.id; status = [string]$queuedRun.status; result = [string]$queuedRun.result; url = [string]$queuedRun.url }
                $result.status = 'Queued'
                if ($WaitForCompletion) {
                    $completedRun = Wait-UdpPipelineRun -RunId ([int]$queuedRun.id) -OrganizationUrl $organizationUrl -Project ([string]$projectInfo.name) -TimeoutMinutes $RunTimeoutMinutes -PollSeconds $PollSeconds
                    $result.run.status = [string]$completedRun.status
                    $result.run.result = [string]$completedRun.result
                    $result.status = if ([string]$completedRun.result -ieq 'succeeded') { 'Succeeded' } else { 'Failed' }
                    if ($DownloadEvidence) {
                        $result.evidence = Get-UdpHarnessEvidence -RunId ([int]$queuedRun.id) -Destination (Join-Path $OutputDirectory 'pipeline-artifact') -OrganizationUrl $organizationUrl -Project ([string]$projectInfo.name)
                    }
                }
            }
            elseif ($WhatIfPreference) {
                $result.status = 'Planned'
            }
        }
        return $result
    }
    finally {
        $result.outputPath = Save-UdpBootstrapResult -Result $result -OutputDirectory $OutputDirectory -Cmdlet $PSCmdlet
    }
}

Export-ModuleMember -Function @(
    'ConvertTo-AdoOrganizationUrl'
    'ConvertTo-GitHubRepositorySlug'
    'New-AdoWifServiceEndpointConfiguration'
    'New-AdoBranchControlCheckConfiguration'
    'New-AdoPipelinePermissionsPayload'
    'Get-UdpVariableGroupValues'
    'Initialize-UdpAzureDevOpsExamples'
)
