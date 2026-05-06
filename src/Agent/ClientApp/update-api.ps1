$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..\..\..")
$agentProject = Join-Path $repoRoot "src\Agent\Agent.csproj"
$solution = Join-Path $repoRoot "Agent.slnx"
$openApiUrl = "http://localhost:5213/openapi/v1.json"
$apiBaseUrl = "http://localhost:5213"
$startedProcess = $null

function Test-OpenApi {
    try {
        $response = Invoke-WebRequest -Uri $openApiUrl -UseBasicParsing -TimeoutSec 2
        return $response.StatusCode -eq 200
    }
    catch {
        return $false
    }
}

function Wait-OpenApi {
    $deadline = (Get-Date).AddSeconds(30)

    while ((Get-Date) -lt $deadline) {
        if (Test-OpenApi) {
            return
        }

        Start-Sleep -Milliseconds 500
    }

    throw "The dashboard OpenAPI document did not become available at $openApiUrl."
}

Push-Location $repoRoot
try {
    dotnet build $solution

    if (-not (Test-OpenApi)) {
        $startedProcess = Start-Process `
            -FilePath "dotnet" `
            -ArgumentList @("run", "--project", $agentProject, "--no-build", "--urls", $apiBaseUrl) `
            -WorkingDirectory $repoRoot `
            -WindowStyle Hidden `
            -PassThru

        Wait-OpenApi
    }

    Push-Location $scriptRoot
    try {
        $env:OPENAPI_URL = $openApiUrl
        npm run generate-api
        npm run typecheck
        npm run build
    }
    finally {
        Pop-Location
    }
}
finally {
    if ($startedProcess -ne $null -and -not $startedProcess.HasExited) {
        Stop-Process -Id $startedProcess.Id
    }

    Pop-Location
}
