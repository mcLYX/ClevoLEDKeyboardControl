param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publishRoot = Join-Path $root "publish"
$servicePublish = Join-Path $publishRoot "Service"
$trayPublish = Join-Path $publishRoot "Tray"
$installerPayload = Join-Path $root "ColorfulLedKeyboard.Installer\Payload\payload.zip"
$installerPublish = Join-Path $publishRoot "Setup"

New-Item -ItemType Directory -Force -Path $servicePublish, $trayPublish, $installerPublish | Out-Null

dotnet publish (Join-Path $root "ColorfulLedKeyboard.Service\ColorfulLedKeyboard.Service.csproj") `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -o $servicePublish

dotnet publish (Join-Path $root "ColorfulLedKeyboard.Tray\ColorfulLedKeyboard.Tray.csproj") `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -o $trayPublish

if (Test-Path $installerPayload) {
    Remove-Item -LiteralPath $installerPayload -Force
}

$payloadStage = Join-Path $publishRoot "Payload"
if (Test-Path $payloadStage) {
    Remove-Item -LiteralPath $payloadStage -Recurse -Force
}

New-Item -ItemType Directory -Force -Path (Join-Path $payloadStage "Service"), (Join-Path $payloadStage "Tray") | Out-Null
Copy-Item -Path (Join-Path $servicePublish "*") -Destination (Join-Path $payloadStage "Service") -Recurse -Force
Copy-Item -Path (Join-Path $trayPublish "*") -Destination (Join-Path $payloadStage "Tray") -Recurse -Force
Compress-Archive -Path (Join-Path $payloadStage "*") -DestinationPath $installerPayload -Force

dotnet publish (Join-Path $root "ColorfulLedKeyboard.Installer\ColorfulLedKeyboard.Installer.csproj") `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -o $installerPublish

Copy-Item -LiteralPath (Join-Path $installerPublish "ClevoRGBControlSetup.exe") -Destination (Join-Path $publishRoot "ClevoRGBControlSetup.exe") -Force

Write-Host "Published service to $servicePublish"
Write-Host "Published tray app to $trayPublish"
Write-Host "Published setup executable to $(Join-Path $publishRoot "ClevoRGBControlSetup.exe")"
Write-Host "After setup, copy InsydeDCHU.dll to C:\Program Files\ClevoRGBControl\Service."
