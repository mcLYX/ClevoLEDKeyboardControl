$ErrorActionPreference = "Stop"

$serviceName = "ClevoLEDKeyboardControlService"
$displayName = "ClevoLEDKeyboardControl Service"
$root = Split-Path -Parent $PSScriptRoot
$serviceExe = Join-Path $root "publish\Service\ColorfulLedKeyboard.Service.exe"

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Please run this script as Administrator."
}

if (-not (Test-Path $serviceExe)) {
    throw "Service executable not found: $serviceExe. Run scripts\publish.ps1 first."
}

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne "Stopped") {
        Stop-Service -Name $serviceName -Force
        $existing.WaitForStatus("Stopped", "00:00:20")
    }
    sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 2
}

sc.exe create $serviceName binPath= "`"$serviceExe`"" start= auto DisplayName= "`"$displayName`"" | Out-Null
sc.exe description $serviceName "Controls Clevo-compatible keyboard RGB lighting in the background." | Out-Null
Start-Service -Name $serviceName

Write-Host "$displayName installed and started."
