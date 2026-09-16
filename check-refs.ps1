# Сверка собранного DLL с текущей версией игры.
#
#   powershell -ExecutionPolicy Bypass -File .\check-refs.ps1 [-Dll путь]
#
# build.ps1 вызывает его сам после каждой сборки. Проверяются три вещи:
#
#   1. Каждая ссылка мода на тип или член (метод, поле) любой сборки из папки
#      игры и BepInEx резолвится в реальное определение. Компилятор это тоже
#      проверяет, но только против тех сборок, что лежали рядом при сборке; после
#      обновления игры несовпадение всплывает уже в рантайме как
#      MissingMethodException / TypeLoadException. Именно так ломались
#      AutoRepair, CraftFromContainers и EmoteWheelReworked.
#
#   2. Цели рефлексии. Компилятор их не видит вовсе: `typeof(Plant).GetMethod(
#      "GetGrowTime", …)` или `AccessTools.Method(typeof(Plant), "GetGrowTime")` —
#      просто строка. Скрипт находит в IL такие вызовы, берёт ближайшие перед ними
#      ldtoken (тип) и ldstr (имя) и проверяет, что такой член у типа есть.
#
#   3. Цели Harmony-патчей: `[HarmonyPatch(typeof(Character), "RPC_Damage")]` —
#      тоже строка. Harmony сообщит о пропавшем методе только при загрузке мода,
#      и тогда не встанет ни один патч сборки. Скрипт читает атрибуты и сверяет.
#
# Инструмент — Mono.Cecil.dll из BepInEx\core, ничего ставить не нужно.

param([string]$Dll = "")

$ErrorActionPreference = "Stop"
$managed = "D:\SteamLibrary\steamapps\common\Valheim\valheim_Data\Managed"
$core    = "$env:APPDATA\r2modmanPlus-local\Valheim\profiles\Valheim\BepInEx\core"

if ($Dll -eq "") {
    $first = Get-ChildItem "$PSScriptRoot\build\*.dll" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $first) { throw "No DLL in $PSScriptRoot\build; build first or pass -Dll" }
    $Dll = $first.FullName
}
if (-not (Test-Path $Dll))     { throw "DLL not found: $Dll" }
if (-not (Test-Path $managed)) { throw "Game assemblies not found: $managed" }
if (-not (Test-Path $core))    { throw "BepInEx core not found: $core" }

Add-Type -Path "$core\Mono.Cecil.dll"

$resolver = New-Object Mono.Cecil.DefaultAssemblyResolver
$resolver.AddSearchDirectory($managed)
$resolver.AddSearchDirectory($core)
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.AssemblyResolver = $resolver
$mod = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($Dll, $rp).MainModule

$problems = New-Object System.Collections.Generic.List[string]
$types = 0; $members = 0; $reflect = 0; $patches = 0

# --- 1. типы
foreach ($tr in $mod.GetTypeReferences()) {
    $types++
    try {
        $d = $tr.Resolve()
        if ($null -eq $d) { $problems.Add("type not found: $($tr.FullName) [$($tr.Scope.Name)]") }
    } catch {
        $problems.Add("type unresolvable: $($tr.FullName) [$($tr.Scope.Name)]: $($_.Exception.Message)")
    }
}

# --- 1. члены
foreach ($mr in $mod.GetMemberReferences()) {
    $members++
    $where = $mr.DeclaringType.FullName + "::" + $mr.Name
    try {
        $d = $mr.Resolve()
        if ($null -eq $d) { $problems.Add("member not found: $where [$($mr.DeclaringType.Scope.Name)]") }
    } catch {
        $problems.Add("member unresolvable: $where : $($_.Exception.Message)")
    }
}

# Есть ли у типа (или его предков) член с таким именем нужного рода
function Has-Member($typeRef, $name, $kind) {
    $def = $null
    try { $def = $typeRef.Resolve() } catch { }
    if ($null -eq $def) { return $null }
    $cur = $def
    while ($null -ne $cur) {
        $found = switch ($kind) {
            "field"    { ($cur.Fields     | Where-Object { $_.Name -eq $name }).Count -gt 0 }
            "method"   { ($cur.Methods    | Where-Object { $_.Name -eq $name }).Count -gt 0 }
            "property" { ($cur.Properties | Where-Object { $_.Name -eq $name }).Count -gt 0 }
            "any"      { (($cur.Fields + $cur.Methods + $cur.Properties) | Where-Object { $_.Name -eq $name }).Count -gt 0 }
        }
        if ($found) { return $true }
        $cur = if ($null -ne $cur.BaseType) { try { $cur.BaseType.Resolve() } catch { $null } } else { $null }
    }
    return $false
}

function Walk-Types($t) {
    $t
    foreach ($n in $t.NestedTypes) { Walk-Types $n }
}
$allTypes = @($mod.Types | ForEach-Object { Walk-Types $_ })

# --- 2. рефлексия: System.Type.GetX и HarmonyLib.AccessTools.X
$kinds = @{
    "GetField" = "field"; "GetMethod" = "method"; "GetProperty" = "property";
    "Field" = "field"; "DeclaredField" = "field"; "Method" = "method"; "DeclaredMethod" = "method";
    "Property" = "property"; "DeclaredProperty" = "property"; "PropertyGetter" = "property"; "PropertySetter" = "property"
}
foreach ($t in $allTypes) {
    foreach ($m in $t.Methods) {
        if (-not $m.HasBody) { continue }
        $ins = $m.Body.Instructions
        for ($i = 0; $i -lt $ins.Count; $i++) {
            $op = $ins[$i]
            if ($op.OpCode.Name -notmatch '^callvirt$|^call$') { continue }
            $callee = $op.Operand
            $owner = $callee.DeclaringType.FullName
            if ($owner -ne "System.Type" -and $owner -ne "HarmonyLib.AccessTools") { continue }
            if (-not $kinds.ContainsKey($callee.Name)) { continue }

            # назад до ldstr (имя), затем до первого ldtoken перед ним (тип): у GetMethod
            # с массивом типов параметров между ldstr и вызовом лежат ldtoken параметров
            $name = $null; $type = $null
            for ($j = $i - 1; $j -ge 0 -and $j -ge $i - 60; $j--) {
                $p = $ins[$j]
                if ($null -eq $name) {
                    if ($p.OpCode.Name -eq "ldstr") { $name = $p.Operand }
                    continue
                }
                if ($p.OpCode.Name -eq "ldtoken" -and $p.Operand -is [Mono.Cecil.TypeReference]) { $type = $p.Operand; break }
            }
            if (-not $name -or -not $type) {
                $problems.Add("reflection in $($m.Name): could not read the target of $($callee.Name)")
                continue
            }
            $reflect++
            $found = Has-Member $type $name $kinds[$callee.Name]
            if ($null -eq $found) { $problems.Add("reflection: type $($type.FullName) not found (for $($callee.Name)('$name'))"); continue }
            if ($found) { Write-Host ("  ok  {0,-14} {1}.{2}" -f $callee.Name, $type.FullName, $name) }
            else        { $problems.Add("reflection: $($type.FullName).$name not found (in $($m.Name), $($callee.Name))") }
        }
    }
}

# --- 3. Harmony: [HarmonyPatch(typeof(T), "name")] на классах и методах
function Check-PatchAttrs($owner, $attrs) {
    foreach ($a in $attrs) {
        if ($a.AttributeType.Name -ne "HarmonyPatch") { continue }
        $args = $a.ConstructorArguments
        if ($args.Count -lt 2) { continue }
        if ($args[0].Type.FullName -ne "System.Type" -or $args[1].Type.FullName -ne "System.String") { continue }
        $type = $args[0].Value; $name = $args[1].Value
        $script:patches++
        $found = Has-Member $type $name "any"
        if ($null -eq $found) { $problems.Add("harmony: type $($type.FullName) not found (patch on $owner)"); continue }
        if ($found) { Write-Host ("  ok  {0,-14} {1}.{2}" -f "HarmonyPatch", $type.FullName, $name) }
        else        { $problems.Add("harmony: $($type.FullName).$name not found (patch on $owner)") }
    }
}
foreach ($t in $allTypes) {
    Check-PatchAttrs $t.FullName $t.CustomAttributes
    foreach ($m in $t.Methods) { Check-PatchAttrs "$($t.FullName)::$($m.Name)" $m.CustomAttributes }
}

Write-Host "Checked $types type refs, $members member refs, $reflect reflection targets, $patches Harmony targets against $managed"
if ($problems.Count -gt 0) {
    Write-Host ""
    Write-Host "PROBLEMS:" -ForegroundColor Red
    $problems | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}
Write-Host "All references resolve against the current game build." -ForegroundColor Green
exit 0
