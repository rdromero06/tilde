<#
.SYNOPSIS
  Compila la solución en Debug y ejecuta las pruebas.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\build.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
# Si el SDK se acaba de instalar, esta consola puede no tener aún dotnet en el PATH.
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { $env:Path = "$env:ProgramFiles\dotnet;$env:Path" }
Push-Location $root
try {
    dotnet build Tilde.slnx -c Debug -nologo
    if ($LASTEXITCODE -ne 0) { throw "La compilación ha fallado" }
    dotnet test tests\Tilde.Core.Tests -nologo
    if ($LASTEXITCODE -ne 0) { throw "Hay pruebas que fallan" }
    Write-Host "`nListo. Ejecutable de depuración: src\Tilde.App\bin\Debug\net10.0-windows10.0.22621.0\win-x64\Tilde.exe" -ForegroundColor Green
}
finally {
    Pop-Location
}
