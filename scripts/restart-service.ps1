$ErrorActionPreference = "Stop"

$serviceName = "ClevoLEDKeyboardControlService"

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Please run this script as Administrator."
}

$service = Get-Service -Name $serviceName -ErrorAction Stop
if ($service.Status -ne "Stopped") {
    Stop-Service -Name $serviceName -Force
    $service.WaitForStatus("Stopped", "00:00:20")
}

Start-Service -Name $serviceName
Write-Host "Service restarted."
