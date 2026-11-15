<#
.SYNOPSIS
    Chạy công cụ dòng lệnh (CLI) của NexusClean với hỗ trợ hiển thị Tiếng Việt UTF-8.
.EXAMPLE
    .\run_cli.ps1 scan --drive C:
    .\run_cli.ps1 simulate --drive C: --tier safe
    .\run_cli.ps1 relocate-plan --drive C:
    .\run_cli.ps1 rules validate
#>
$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

Set-Location -Path $PSScriptRoot

$cliArgs = if ($args.Count -gt 0) { $args } else { @("--help") }
dotnet run --project "$PSScriptRoot\src\NexusClean.Cli\NexusClean.Cli.csproj" -c Release -- @cliArgs
