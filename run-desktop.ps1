param(
    [string]$Url = "http://localhost:5240/"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Write-Host "Restoring and building the solution..." -ForegroundColor Cyan
dotnet build "$root\SmartX.Desktop.sln" -c Debug | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

Write-Host "Starting the gateway at $Url ..." -ForegroundColor Cyan
$api = Start-Process -PassThru -FilePath "dotnet" `
    -ArgumentList @("run", "--project", "$root\src\SmartX.Api\SmartX.Api.csproj", "--no-build", "--urls", $Url)

try {
    $health = ($Url.TrimEnd('/')) + "/api/health"
    $ready = $false

    # the window is a plain client so the gateway has to be answering before it opens
    foreach ($attempt in 1..40) {
        Start-Sleep -Milliseconds 500
        try {
            $response = Invoke-WebRequest -Uri $health -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -eq 200) { $ready = $true; break }
        } catch { }
    }

    if (-not $ready) { throw "The gateway did not become healthy at $health within 20 seconds." }

    Write-Host "Gateway is healthy. Launching the console..." -ForegroundColor Green
    dotnet run --project "$root\src\SmartX.Wpf\SmartX.Wpf.csproj" --no-build -- $Url | Out-Host
}
finally {
    if ($api -and -not $api.HasExited) {
        Write-Host "Stopping the gateway..." -ForegroundColor Cyan
        Stop-Process -Id $api.Id -Force -ErrorAction SilentlyContinue
    }
}
