# Сборка build\StationSpeed.dll.
#
#   powershell -ExecutionPolicy Bypass -File .\build.ps1            # собрать и проверить ссылки
#   powershell -ExecutionPolicy Bypass -File .\build.ps1 -Install   # ... и положить в plugins
#   powershell -ExecutionPolicy Bypass -File .\build.ps1 -Package   # ... и собрать zip для Thunderstore
#   powershell -ExecutionPolicy Bypass -File .\build.ps1 -NoCheck   # без проверки ссылок
#
# Компилятор — csc.exe из .NET Framework, который есть на любой Windows. Он понимает
# только C# 5, и исходник намеренно написан в этих рамках (без out var, ?., $"" и
# nameof). Если появится dotnet SDK, можно собирать и через src\StationSpeed.csproj,
# результат тот же. Ссылки берутся прямо из установленной игры и профиля r2modman,
# поэтому сборка идёт против ровно той версии игры, в которой мод будет работать.
#
# После сборки check-refs.ps1 сверяет каждое обращение DLL к сборкам игры (цели
# рефлексии и Harmony-патчей тоже) с тем, что в них реально есть — компилятор этого
# не гарантирует, если игра обновилась, а падает уже в рантайме.

param([switch]$Install, [switch]$Package, [switch]$NoCheck)

$ErrorActionPreference = "Stop"
$root    = $PSScriptRoot
$managed = "D:\SteamLibrary\steamapps\common\Valheim\valheim_Data\Managed"
$profile = "$env:APPDATA\r2modmanPlus-local\Valheim\profiles\Valheim"
$core    = "$profile\BepInEx\core"
$plugins = "$profile\BepInEx\plugins\StationSpeed"
$out     = "$root\build\StationSpeed.dll"
$src     = Get-ChildItem "$root\src\*.cs" | Sort-Object Name | ForEach-Object { $_.FullName }
$main    = "$root\src\StationSpeedPlugin.cs"

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (-not (Test-Path $csc))     { throw "csc.exe not found under $env:WINDIR\Microsoft.NET" }
if (-not (Test-Path $managed)) { throw "Game assemblies not found: $managed (edit `$managed in build.ps1)" }
if (-not (Test-Path $core))    { throw "BepInEx core not found: $core (edit `$profile in build.ps1)" }

$refs = @(
    "assembly_valheim", "assembly_utils", "SoftReferenceableAssets",
    "UnityEngine", "UnityEngine.CoreModule",
    "netstandard", "mscorlib", "System", "System.Core"
) | ForEach-Object { "/r:$managed\$_.dll" }
$refs += "/r:$core\BepInEx.dll"
$refs += "/r:$core\0Harmony.dll"

New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

# /nostdlib + mscorlib игры: собираем против того рантайма, в котором мод будет жить
& $csc /nologo /noconfig /nostdlib+ /target:library /optimize+ /nowarn:0618,1701,1702 "/out:$out" @refs @src
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$ver = [System.Text.RegularExpressions.Regex]::Match((Get-Content $main -Raw), 'Version = "([^"]+)"').Groups[1].Value
Write-Host "Built $out (v$ver, $((Get-Item $out).Length) bytes, $($src.Count) source files)"

if (-not $NoCheck) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File "$root\check-refs.ps1" -Dll $out
    if ($LASTEXITCODE -ne 0) { throw "check-refs.ps1 found references that do not exist in the current game build" }
}

if ($Install) {
    New-Item -ItemType Directory -Force $plugins | Out-Null
    Copy-Item $out "$plugins\StationSpeed.dll" -Force
    Write-Host "Installed to $plugins"
}

if ($Package) {
    # Пакет Thunderstore: manifest.json (версия подставляется из исходника), icon.png
    # 256x256, README.md, CHANGELOG.md и DLL в корне архива.
    $ts   = "$root\thunderstore"
    $tmp  = Join-Path ([System.IO.Path]::GetTempPath()) ("StationSpeed-pkg-" + [guid]::NewGuid().ToString("N"))
    $zip  = "$root\build\StationSpeed-$ver.zip"
    New-Item -ItemType Directory -Force $tmp | Out-Null
    try {
        $manifest = Get-Content "$ts\manifest.json" -Raw -Encoding UTF8
        $manifest = $manifest -replace '"version_number"\s*:\s*"[^"]*"', ('"version_number": "' + $ver + '"')
        [System.IO.File]::WriteAllText("$tmp\manifest.json", $manifest, (New-Object System.Text.UTF8Encoding($false)))
        Copy-Item "$ts\icon.png"   "$tmp\icon.png"
        Copy-Item "$ts\README.md"  "$tmp\README.md"
        Copy-Item "$root\CHANGELOG.md" "$tmp\CHANGELOG.md"
        Copy-Item $out "$tmp\StationSpeed.dll"
        if (Test-Path $zip) { Remove-Item $zip -Force }
        Compress-Archive -Path "$tmp\*" -DestinationPath $zip
        Write-Host "Packaged $zip ($((Get-Item $zip).Length) bytes)"
    } finally {
        Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
