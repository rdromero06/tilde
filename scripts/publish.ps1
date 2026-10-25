<#
.SYNOPSIS
  Publica Tilde.exe con Native AOT en la carpeta publish\.

.DESCRIPTION
  Native AOT necesita el enlazador de Visual Studio (Build Tools con la carga de trabajo
  «Desarrollo de escritorio con C++»). El compilador lo localiza con vswhere.exe, que no
  siempre está en el PATH: lo añadimos aquí.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
# Si el SDK se acaba de instalar, esta consola puede no tener aún dotnet en el PATH.
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { $env:Path = "$env:ProgramFiles\dotnet;$env:Path" }
$vsInstaller = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if (Test-Path $vsInstaller) { $env:Path = "$vsInstaller;$env:Path" }

Push-Location $root
try {
    dotnet publish src\Tilde.App\Tilde.App.csproj -c Release -o publish -nologo
    if ($LASTEXITCODE -ne 0) { throw "La publicación ha fallado" }
    $exe = Get-Item publish\Tilde.exe
    Write-Host ("`nListo: {0} ({1:N1} MB)" -f $exe.FullName, ($exe.Length / 1MB)) -ForegroundColor Green
}
finally {
    Pop-Location
}
