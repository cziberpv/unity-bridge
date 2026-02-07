<#
.SYNOPSIS
    Synchronous Unity Bridge command wrapper
.DESCRIPTION
    Sends a command to Unity Bridge and waits for response.
    Works even when Unity is not focused (Unity uses polling).
.PARAMETER Command
    JSON command string, e.g. '{"type": "help"}'
.PARAMETER Timeout
    Timeout in seconds (default: 60 for compilation)
.EXAMPLE
    .\unity-cmd.ps1 '{"type": "help"}'
    .\unity-cmd.ps1 '{"type": "refresh"}' -Timeout 120
    .\unity-cmd.ps1 '{"type": "scene"}'
#>
param(
    [Parameter(Mandatory=$true, Position=0)]
    [string]$Command,

    [Parameter(Mandatory=$false)]
    [int]$Timeout = 60
)

$ErrorActionPreference = "Stop"

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
