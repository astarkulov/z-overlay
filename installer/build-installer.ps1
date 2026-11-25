$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "[1/3] Publishing single-file ApexHUD.exe..." -ForegroundColor Cyan
dotnet publish "$root\ApexHUD.csproj" -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o "$root\dist"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "[2/3] Locating Inno Setup Compiler (ISCC.exe)..." -ForegroundColor Cyan
$isccCandidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}
if (-not $iscc) {
    throw "ISCC.exe not found! Install Inno Setup 6: winget install --id JRSoftware.InnoSetup --scope user"
}

Write-Host "[3/3] Compiling Inno Setup installer using $iscc..." -ForegroundColor Cyan
& $iscc "$root\installer\Z-Overlay.iss"
if ($LASTEXITCODE -ne 0) { throw "ISCC compilation failed" }

Write-Host "Done! Artifacts in $root\dist:" -ForegroundColor Green
Get-ChildItem "$root\dist" | Select-Object Name, Length
