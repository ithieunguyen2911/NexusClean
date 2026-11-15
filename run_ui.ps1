<#
.SYNOPSIS
    Khởi động giao diện Desktop WPF của NexusClean (Windows .NET 8).
.EXAMPLE
    .\run_ui.ps1
#>
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot

Write-Host "[NexusClean] Đang kiểm tra & biên dịch giao diện Desktop ($Configuration)..." -ForegroundColor Cyan
dotnet build "$PSScriptRoot\src\NexusClean.UI\NexusClean.UI.csproj" -c $Configuration --nologo -v quiet

$exePath = Join-Path $PSScriptRoot "src\NexusClean.UI\bin\$Configuration\net8.0-windows\NexusClean.UI.exe"
if (-not (Test-Path $exePath)) {
    throw "Không tìm thấy tệp thực thi tại: $exePath"
}

Write-Host "[NexusClean] Đang mở bảng điều khiển NexusClean Desktop..." -ForegroundColor Green
Start-Process -FilePath $exePath -WorkingDirectory $PSScriptRoot
