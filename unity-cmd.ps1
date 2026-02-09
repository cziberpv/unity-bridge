<#
.SYNOPSIS
    Synchronous Unity Bridge command wrapper
.DESCRIPTION
    Sends a command to Unity Bridge and waits for response.
    Works even when Unity is not focused (Unity uses polling).
.PARAMETER Command
    JSON command string, e.g. '{"type": "help"}'
.PARAMETER File
    Path to a JSON file containing the command. Use this instead of Command
    when your JSON is complex and PowerShell string escaping mangles it.
.PARAMETER Timeout
    Timeout in seconds (default: 60 for compilation)
.EXAMPLE
    .\unity-cmd.ps1 '{"type": "help"}'
    .\unity-cmd.ps1 '{"type": "refresh"}' -Timeout 120
    .\unity-cmd.ps1 -File request.json
    .\unity-cmd.ps1 -f complex-batch.json -Timeout 120
#>
param(
    [Parameter(Mandatory=$false, Position=0)]
    [string]$Command,

    [Parameter(Mandatory=$false)]
    [Alias("f")]
    [string]$File,

    [Parameter(Mandatory=$false)]
    [int]$Timeout = 60
)

$ErrorActionPreference = "Stop"

# Resolve input: -File or positional Command, not both
if ($File -and $Command) {
    Write-Error "Specify either -File or a command string, not both."
    exit 1
}

if ($File) {
    if (-not (Test-Path $File)) {
        Write-Error "File not found: $File"
        exit 1
    }
    $Command = Get-Content $File -Raw -Encoding UTF8
    if (-not $Command -or $Command.Trim().Length -eq 0) {
        Write-Error "File is empty: $File"
        exit 1
    }
} elseif (-not $Command) {
    Write-Host "Usage: unity-cmd.ps1 <json-command> [-Timeout <seconds>]"
    Write-Host "       unity-cmd.ps1 -File <path.json> [-Timeout <seconds>]"
    Write-Host ""
    Write-Host "Examples:"
    Write-Host "  .\unity-cmd.ps1 '{""type"": ""help""}'"
    Write-Host "  .\unity-cmd.ps1 -File request.json"
    Write-Host "  .\unity-cmd.ps1 -f complex-batch.json -Timeout 120"
    exit 1
}

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$requestFile = Join-Path $projectRoot "Assets\LLM\Bridge\request.json"
$responseFile = Join-Path $projectRoot "Assets\LLM\Bridge\response.md"

# Ensure bridge folder exists
$bridgeFolder = Split-Path -Parent $requestFile
if (-not (Test-Path $bridgeFolder)) {
    New-Item -ItemType Directory -Path $bridgeFolder -Force | Out-Null
}

# Get response file time BEFORE sending request
$beforeTime = if (Test-Path $responseFile) {
    (Get-Item $responseFile).LastWriteTime
} else {
    [DateTime]::MinValue
}

# Write request
$Command | Out-File -FilePath $requestFile -Encoding UTF8 -NoNewline

# Poll for response change
$pollInterval = 0.5  # seconds
$elapsed = 0

while ($elapsed -lt $Timeout) {
    Start-Sleep -Milliseconds ($pollInterval * 1000)
    $elapsed += $pollInterval

    if (Test-Path $responseFile) {
        $currentTime = (Get-Item $responseFile).LastWriteTime
        if ($currentTime -gt $beforeTime) {
            # Response updated - return content
            $content = Get-Content $responseFile -Raw -Encoding UTF8
            Write-Output $content
            exit 0
        }
    }
}

# Timeout
Write-Error "Timeout ($Timeout s) waiting for Unity response. Is Unity running?"
exit 1
