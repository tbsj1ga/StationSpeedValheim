# Builds the Nexus Mods kit of a released version into build\nexus\<version>\:
#
#   <Mod>-<version>-nexus.zip  BepInEx\plugins\<Mod>\<Mod>.dll (+ README.md, CHANGELOG.md):
#                              unpacks straight into the game folder, Vortex recognises it
#   description.txt            the package's README as Nexus BBCode
#   summary.txt                the short description from manifest.json
#   gallery\                   00-cover.png (1280x720, the primary image of the mod page) and
#                              the README's media - animated webp converted to GIF
#
#   powershell -ExecutionPolicy Bypass -File .\nexus.ps1                 # the latest package
#   powershell -ExecutionPolicy Bypass -File .\nexus.ps1 -Version 1.2.3  # a given release
#
# Everything comes from the Thunderstore package build\<Mod>-<version>.zip (build.ps1
# -Package): the DLL, README, CHANGELOG and manifest, so Nexus gets exactly what was
# published on Thunderstore. Media are taken from docs\media. The cover and the GIFs need
# Python with Pillow (pip install pillow); without it the gallery holds only the still
# images. A GIF is made as smooth as fits in -GifLimitMB (8 by default): full frame rate
# at the largest width that fits, then every other frame. The file is the same in every
# j1gA mod repository.

param([string]$Version, [string]$IconSource, [double]$GifLimitMB = 8)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$utf8 = New-Object System.Text.UTF8Encoding($false)

if (-not $Version) {
    # the highest version among the Thunderstore packages in build\
    $Version = Get-ChildItem "$root\build\*.zip" | ForEach-Object {
        $m = [regex]::Match($_.Name, '-(\d+\.\d+\.\d+)\.zip$'); if ($m.Success) { $m.Groups[1].Value }
    } | Sort-Object { [version]$_ } | Select-Object -Last 1
    if (-not $Version) { throw "No Thunderstore package in $root\build: run build.ps1 -Package first" }
}
$tsZip = @(Get-ChildItem "$root\build\*-$Version.zip")
if ($tsZip.Count -eq 0) { throw "No Thunderstore package of $Version in $root\build: run build.ps1 -Package first" }

$out = "$root\build\nexus\$Version"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force "$out\gallery" | Out-Null

# --- The archive ---------------------------------------------------------------------
Add-Type -AssemblyName System.IO.Compression.FileSystem
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("nexus-" + [guid]::NewGuid().ToString("N"))
try {
    [System.IO.Compression.ZipFile]::ExtractToDirectory($tsZip[0].FullName, "$tmp\ts")
    $manifest = [System.IO.File]::ReadAllText("$tmp\ts\manifest.json", $utf8) | ConvertFrom-Json
    $readme = [System.IO.File]::ReadAllText("$tmp\ts\README.md", $utf8) -replace "`r", ""
    $mod = $manifest.name
    $dir = "$tmp\pkg\BepInEx\plugins\$mod"
    New-Item -ItemType Directory -Force $dir | Out-Null
    Copy-Item "$tmp\ts\*.dll" $dir
    Copy-Item "$tmp\ts\README.md" $dir
    $pubIcon = Join-Path ([System.IO.Path]::GetTempPath()) ("nexus-icon-" + [guid]::NewGuid().ToString("N") + ".png")
    Copy-Item "$tmp\ts\icon.png" $pubIcon
    Copy-Item "$tmp\ts\CHANGELOG.md" $dir
    [System.IO.Compression.ZipFile]::CreateFromDirectory("$tmp\pkg", "$out\$mod-$Version-nexus.zip")
} finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

# --- Markdown to Nexus BBCode --------------------------------------------------------
# Covers what the READMEs use: headings, paragraphs, lists with wrapped lines, block
# quotes, tables, images, links, bold, italic, inline code (coloured) and code blocks.
# Nexus keeps line breaks as they are, so wrapped lines are joined into one line per paragraph.

function Inline([string]$s) {
    $s = [regex]::Replace($s, '`([^`]+)`', '[color=#e6c07b]$1[/color]')
    $s = [regex]::Replace($s, '\[!\[[^\]]*\]\(([^)]+)\)\]\(([^)]+)\)', '[url=$2][img]$1[/img][/url]')
    $s = [regex]::Replace($s, '!\[[^\]]*\]\(([^)]+)\)', '[img]$1[/img]')
    $s = [regex]::Replace($s, '(?<!\])\[([^\]]+)\]\(([^)]+)\)', '[url=$2]$1[/url]')
    $s = [regex]::Replace($s, '\*\*(.+?)\*\*', '[b]$1[/b]')
    $s = [regex]::Replace($s, '(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])', '[i]$1[/i]')
    $s = [regex]::Replace($s, '(?<![\w=/\]])(https?://[^\s\[\]<>]+[^\s\[\]<>.,;:)])', '[url]$1[/url]')
    return $s
}

function Cells([string]$row) {
    $t = $row.Trim()
    if ($t.StartsWith("|")) { $t = $t.Substring(1) }
    if ($t.EndsWith("|")) { $t = $t.Substring(0, $t.Length - 1) }
    return @($t.Split("|") | ForEach-Object { $_.Trim() })
}

function Table([string[]]$rows) {
    $head = Cells $rows[0]
    $body = @($rows | Select-Object -Skip 2)
    $res = New-Object System.Collections.Generic.List[string]
    if (-not ($head | Where-Object { $_ })) {
        # A headerless grid of pictures with a row of captions under each row of them:
        # every picture with its caption under it, one after the other
        for ($k = 0; $k -lt $body.Count; $k++) {
            $cells = Cells $body[$k]
            $caps = $null
            if ($k + 1 -lt $body.Count -and $body[$k] -match '!\[' -and $body[$k + 1] -notmatch '!\[') { $caps = Cells $body[$k + 1]; $k++ }
            for ($c = 0; $c -lt $cells.Count; $c++) {
                if ($cells[$c]) { $res.Add((Inline $cells[$c])) }
                if ($caps -and $c -lt $caps.Count -and $caps[$c]) { $res.Add("[i]" + (Inline $caps[$c]) + "[/i]") }
                $res.Add("")
            }
        }
        return $res
    }
    $res.Add("[list]")
    foreach ($r in $body) {
        # picture-only cells (the icons of "More mods") are left out of a list
        $cells = @(Cells $r | Where-Object { $_ -and $_ -notmatch '^\[?!\[[^\]]*\]\([^)]*\)(\]\([^)]*\))?$' })
        if ($cells.Count -eq 0) { continue }
        $first = Inline $cells[0]
        if ($first -notmatch '^\[(b|url)') { $first = "[b]$first[/b]" }
        $rest = @($cells | Select-Object -Skip 1 | ForEach-Object { Inline $_ })
        $res.Add("[*]" + (@($first) + $rest -join " - "))
    }
    $res.Add("[/list]")
    return $res
}

function Convert-Block([string[]]$lines) {
    $res = New-Object System.Collections.Generic.List[string]
    $para = New-Object System.Collections.Generic.List[string]
    $inList = $false
    $i = 0
    $flush = {
        if ($para.Count -gt 0) {
            $text = Inline (($para | ForEach-Object { $_.Trim() }) -join " ")
            if ($inList) { $res.Add("[*]" + $text) } else { $res.Add($text); $res.Add("") }
            $para.Clear()
        }
    }
    while ($i -lt $lines.Count) {
        $line = $lines[$i]
        if ($line -match '^```') {
            & $flush
            $code = New-Object System.Collections.Generic.List[string]
            $i++
            while ($i -lt $lines.Count -and $lines[$i] -notmatch '^```') { $code.Add($lines[$i]); $i++ }
            $res.Add("[code]" + ($code -join "`n") + "[/code]"); $res.Add("")
        } elseif ($line -match '^\s*$') {
            & $flush
            if ($inList) { $res.Add("[/list]"); $res.Add(""); $inList = $false }
        } elseif ($line -match '^(#{1,4})\s+(.*)$') {
            & $flush
            if ($inList) { $res.Add("[/list]"); $inList = $false }
            $size = @{ 1 = 6; 2 = 5; 3 = 4; 4 = 3 }[$matches[1].Length]
            $res.Add("[size=$size][b]" + (Inline $matches[2]) + "[/b][/size]")
        } elseif ($line -match '^>') {
            & $flush
            $quote = New-Object System.Collections.Generic.List[string]
            while ($i -lt $lines.Count -and $lines[$i] -match '^>\s?(.*)$') { $quote.Add($matches[1]); $i++ }
            $inner = (Convert-Block $quote.ToArray()) -join "`n"
            $res.Add("[quote]" + $inner.Trim() + "[/quote]"); $res.Add("")
            continue
        } elseif ($line -match '^\|') {
            & $flush
            $rows = New-Object System.Collections.Generic.List[string]
            while ($i -lt $lines.Count -and $lines[$i] -match '^\|') { $rows.Add($lines[$i]); $i++ }
            foreach ($t in (Table $rows.ToArray())) { $res.Add($t) }
            $res.Add("")
            continue
        } elseif ($line -match '^\s*(---+|\*\*\*+)\s*$') {
            & $flush
            $res.Add("[line]")
        } elseif ($line -match '^\s{0,1}[-*]\s+(.*)$') {
            & $flush
            if (-not $inList) { $res.Add("[list]"); $inList = $true }
            $para.Add($matches[1])
        } else {
            $para.Add($line)
        }
        $i++
    }
    & $flush
    if ($inList) { $res.Add("[/list]") }
    return $res.ToArray()
}

$bb = (Convert-Block ($readme -split "`n")) -join "`n"
$bb = [regex]::Replace($bb, "\n{3,}", "`n`n").Trim()
$repoUrl = $manifest.website_url
$bb += "`n`n[line]`nFull documentation, changelog and the Russian version: [url=$repoUrl]GitHub[/url] ([url=$repoUrl/blob/main/README-RU.md]README-RU[/url]).`n"
[System.IO.File]::WriteAllText("$out\description.txt", $bb, $utf8)
[System.IO.File]::WriteAllText("$out\summary.txt", $manifest.description, $utf8)

# --- Gallery: cover and media --------------------------------------------------------
$media = @([regex]::Matches($readme, '/docs/media/([\w.-]+\.(?:png|webp|gif|jpg))') | ForEach-Object { $_.Groups[1].Value } |
    Where-Object { $_ -notlike "icon-*" } | Select-Object -Unique)

# The cover is drawn from the icon in the package. A large original (icons-src) is used
# instead only when it is the same picture - a stale original must not end up on Nexus.
if (-not $IconSource) { $IconSource = "$env:USERPROFILE\Documents\icons-src\$mod-1254px.png" }

$py = Get-Command python -ErrorAction SilentlyContinue
$pyOk = $false
if ($py) { & python -c "import PIL" 2>$null; $pyOk = ($LASTEXITCODE -eq 0) }

$n = 0
if ($pyOk) {
    $script = Join-Path ([System.IO.Path]::GetTempPath()) ("nexus-gallery-" + [guid]::NewGuid().ToString("N") + ".py")
    @'
import os, sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageSequence

LIMIT = int(float(os.environ.get("NEXUS_GIF_MB", "8")) * 1024 * 1024)  # -GifLimitMB

def gif(src, dst):
    im = Image.open(src)
    frames, durations = [], []
    for f in ImageSequence.Iterator(im):
        frames.append(f.convert("RGB"))
        f.load()
        durations.append(f.info.get("duration") or 83)
    if len(frames) == 1:
        frames[0].save(dst[:-4] + ".png", optimize=True)
        return dst[:-4] + ".png"
    for width, step in ((800, 1), (720, 1), (640, 1), (560, 1), (800, 2), (640, 2), (480, 3), (400, 4)):
        out, durs = [], []
        for k in range(0, len(frames), step):
            f = frames[k]
            h = round(f.height * width / f.width)
            f = f.resize((width, h), Image.LANCZOS) if f.width > width else f
            out.append(f.quantize(colors=256, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.FLOYDSTEINBERG))
            durs.append(sum(durations[k:k + step]))
        out[0].save(dst, save_all=True, append_images=out[1:], duration=durs, loop=0, optimize=True, disposal=1)
        if os.path.getsize(dst) <= LIMIT:
            break
    return dst

def font(size, bold):
    names = ["segoeuib.ttf", "arialbd.ttf"] if bold else ["segoeui.ttf", "arial.ttf"]
    for n in names:
        p = os.path.join(os.environ.get("WINDIR", "C:\\Windows"), "Fonts", n)
        if os.path.exists(p):
            return ImageFont.truetype(p, size)
    return ImageFont.load_default()

def same(a, b):
    a = a.convert("RGB").resize((32, 32), Image.LANCZOS).getdata()
    b = b.convert("RGB").resize((32, 32), Image.LANCZOS).getdata()
    return sum(abs(x - y) for p, q in zip(a, b) for x, y in zip(p, q)) / (32 * 32 * 3) < 12

def cover(big_icon, icon, title, summary, dst):
    W, H = 1280, 720
    ic = Image.open(icon).convert("RGBA")
    if os.path.exists(big_icon):
        big = Image.open(big_icon).convert("RGBA")
        if same(big, ic):
            ic = big
        else:
            print("  %s is not the published icon, the cover uses the package's icon" % big_icon)
    bg = ic.convert("RGB").resize((W, W), Image.LANCZOS).crop((0, (W - H) // 2, W, (W - H) // 2 + H))
    bg = bg.filter(ImageFilter.GaussianBlur(40))
    bg = Image.blend(bg, Image.new("RGB", (W, H), (10, 10, 14)), 0.55)
    side = 440
    big = ic.resize((side, side), Image.LANCZOS)
    mask = Image.new("L", (side, side), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, side - 1, side - 1), radius=48, fill=255)
    shadow = Image.new("RGBA", (side + 80, side + 80), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle((40, 50, side + 40, side + 50), radius=48, fill=(0, 0, 0, 170))
    shadow = shadow.filter(ImageFilter.GaussianBlur(18))
    x, y = 80, (H - side) // 2
    bg.paste(shadow, (x - 40, y - 40), shadow)
    bg.paste(big, (x, y), Image.composite(big, Image.new("RGBA", big.size, (0, 0, 0, 0)), mask))
    d = ImageDraw.Draw(bg)
    tx = x + side + 64
    room = W - tx - 56
    size = 92
    while size > 40 and font(size, True).getlength(title) > room:
        size -= 4
    tf = font(size, True)
    th = tf.getbbox(title)[3]
    # the whole summary, in the largest size that fits in the space under the title
    for ss in range(34, 17, -2):
        sf = font(ss, False)
        lines, cur = [], ""
        for w in summary.split():
            t = (cur + " " + w).strip()
            if sf.getlength(t) <= room:
                cur = t
            else:
                lines.append(cur)
                cur = w
        lines.append(cur)
        lh = int(ss * 1.35)
        if th + 28 + len(lines) * lh <= H - 120:
            break
    total = th + 28 + len(lines) * lh
    ty = (H - total) // 2
    d.text((tx, ty), title, font=tf, fill=(255, 255, 255))
    for k, l in enumerate(lines):
        d.text((tx, ty + th + 28 + k * lh), l, font=sf, fill=(225, 225, 230))
    bg.save(dst, optimize=True)

args = sys.argv[1:]
out = args[0]
cover(args[1], args[2], args[3], args[4], os.path.join(out, "00-cover.png"))
for k, src in enumerate(args[5:], 1):
    base = "%02d-%s" % (k, os.path.splitext(os.path.basename(src))[0])
    if src.lower().endswith(".webp"):
        r = gif(src, os.path.join(out, base + ".gif"))
    else:
        r = os.path.join(out, base + os.path.splitext(src)[1].lower())
        Image.open(src).save(r, optimize=True)
'@ | Out-File -FilePath $script -Encoding utf8
    try {
        $srcs = @($media | ForEach-Object { "$root\docs\media\$_" } | Where-Object { Test-Path $_ })
        $env:NEXUS_GIF_MB = $GifLimitMB.ToString([Globalization.CultureInfo]::InvariantCulture)
        & python -I $script "$out\gallery" $IconSource $pubIcon $mod $manifest.description @srcs
        if ($LASTEXITCODE -ne 0) { throw "gallery script failed" }
    } finally { Remove-Item $script, $pubIcon -Force -ErrorAction SilentlyContinue }
} else {
    Write-Warning "Python with Pillow not found: no cover and no GIFs, still images only"
    foreach ($m in $media) {
        if ($m -notlike "*.webp" -and (Test-Path "$root\docs\media\$m")) {
            $n++; Copy-Item "$root\docs\media\$m" ("$out\gallery\{0:D2}-$m" -f $n)
        }
    }
}

Write-Host "Nexus kit for $mod $Version in $out"
Get-ChildItem $out -Recurse -File | ForEach-Object {
    Write-Host ([string]::Format([Globalization.CultureInfo]::InvariantCulture, "  {0,-40} {1,8:N0} KB", $_.FullName.Substring($out.Length + 1), [math]::Round($_.Length / 1KB)))
}
