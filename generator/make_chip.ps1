# yankee MakerChip (poker-chip style, 40mm x 3.2mm, 4 colours) generator
# Usage: powershell -File make_chip.ps1
param(
    [string]$Icon = "$PSScriptRoot\icon.png",
    [string]$Url = "https://x.com/yankee_sns",
    [string]$Font = "Arial Rounded MT",
    [int]$LineThr = 140,     # pencil line detection (silhouette)
    [int]$BlackThr = 135,    # darker than this -> black filament
    [double]$FitR = 16.4,    # character fits inside this radius (mm)
    [double]$AdaptT = 16,    # pencil lines: darker than the local average by this much -> black (0 = off)
    [int]$SpeckMin = 6,      # black specks smaller than this (cells of 0.125mm) are removed
    [int]$LineGrow = 1,      # thicken black lines by this many cells (0.125mm each)
    [string]$Mockup = "$PSScriptRoot\mockup2.webp",
    [double]$SplitY = -3.6,  # above: head from icon / below: body from mockup (mm)
    [double]$TextScale = 1.02,
    [double]$QrModule = 0.6,  # QR module size (mm)
    [int]$XClear = 9,         # blank centre, in modules
    [double]$XBadge = 4.9,    # X emblem badge size (mm)
    [double]$XSize = 3.6      # X mark height (mm)
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
Add-Type -Path "$PSScriptRoot\CoinGen.cs", "$PSScriptRoot\Chip.cs", "$PSScriptRoot\OnePiece.cs" -ReferencedAssemblies PresentationCore, WindowsBase, System.Xaml, System.IO.Compression, System.IO.Compression.FileSystem
$out = Join-Path $PSScriptRoot 'stl_chip'; New-Item -ItemType Directory -Force $out | Out-Null
$prev = Join-Path $PSScriptRoot 'preview'; New-Item -ItemType Directory -Force $prev | Out-Null
$N = [CoinGen]::N; $NN = $N * $N
$disc = [CoinGen]::Disc()

# ---------- front: icon ----------
$w = 0; $h = 0
$g = [CoinGen]::LoadGray($Icon, [ref]$w, [ref]$h)
$map = $null
[Chip]::AdaptT = $AdaptT
# sunglasses lenses: fill dark, keep only the bright glints white  (image cx, cy, rx, ry, threshold)
[Chip]::DarkZones.Add([double[]](237, 287, 38, 35, 215))
[Chip]::DarkZones.Add([double[]](360, 280, 48, 38, 215))
$top = [Chip]::IconFront($g, $w, $h, 312, 296, 272, $LineThr, $BlackThr, $FitR, [ref]$map)
"icon centre=({0:N0},{1:N0}) scale={2:N4} mm/px" -f $map[0], $map[1], $map[2]


# ---------- body: traced from the mockup image (the icon has a laptop there) ----------
$w2 = 0; $h2 = 0
$img2 = [Chip]::LoadRgb($Mockup, [ref]$w2, [ref]$h2)
$el = [Chip]::FitRing($img2, $w2, $h2, 213.8, 206.5, 111, 96.7)
$cls = [Chip]::ClassMap($img2, $w2, $h2, $el)
$st = [Chip]::Stats([CoinGen]::RemoveSmall([Chip]::Eq($cls, 3), $true, 20, $disc))
"mockup text: centre=({0:N1},{1:N1}) width={2:N1}mm angle={3:N1}deg" -f $st[0], $st[1], $st[2], $st[3]
$body = New-Object bool[] $NN; $bodyBlack = [Chip]::Eq($cls, 2)
for ($k = 0; $k -lt $NN; $k++) { $body[$k] = $cls[$k] -ge 1 }
$body = [CoinGen]::Majority($body, 2)
$bodyBlack = [CoinGen]::Majority($bodyBlack, 1)
$bodyBlack = [Chip]::Dilate($bodyBlack, $body)

# "yankee" big navy lettering with white outline, at the mockup's position
$tcx = $st[0]; $tcy = $st[1]; $tw = $st[2] * $TextScale; $tAng = $st[3]
$txt = [Chip]::Text('yankee', $Font, $tcx, $tcy, $tw, $tAng, 0.55)
$a = $tAng * [math]::PI / 180; $ca = [math]::Cos($a); $sa = [math]::Sin($a)

# head from the icon (above SplitY), body from the mockup (below)
$sil = New-Object bool[] $NN; $black = New-Object bool[] $NN
for ($j = 0; $j -lt $N; $j++) { for ($i = 0; $i -lt $N; $i++) {
    $k = $j * $N + $i; $y0 = [CoinGen]::Cy($j)
    if ($y0 -ge $SplitY) { $sil[$k] = $top[$k] -ge 0; $black[$k] = $top[$k] -eq 2 }
    else { $sil[$k] = $body[$k]; $black[$k] = $bodyBlack[$k] }
    if (-not $sil[$k]) { continue }
    $x = [CoinGen]::Cx($i) - $tcx; $y = $y0 - $tcy
    $v = - $x * $sa + $y * $ca
    if ($v -lt 0 -and -not $txt[1][$k]) { $sil[$k] = $false }               # nothing below the text centre line
} }
$sil = [CoinGen]::Majority($sil, 1)
$sil = [Chip]::KeepLargest($sil)
$sil = [CoinGen]::RemoveSmall($sil, $false, 250, $disc)
# keep thin pencil lines: drop specks, then thicken lines so they print (~0.4mm)
$black = [CoinGen]::RemoveSmall($black, $true, $SpeckMin, $disc)
for ($d = 0; $d -lt $LineGrow; $d++) { $black = [Chip]::Dilate($black, $sil) }
$black = [CoinGen]::RemoveSmall($black, $false, 5, $disc)   # fill pin-holes in black areas
$inner = [Chip]::Erode($sil, 4)                               # 0.5mm black outline band

$border = [Chip]::Grow($sil, 2)
for ($k = 0; $k -lt $NN; $k++) {
    $t = -1
    if ($border[$k]) { $t = 1 }
    if ($sil[$k]) { $t = if ($black[$k] -or -not $inner[$k]) { 2 } else { 1 } }
    if ($txt[1][$k]) { $t = 1 }
    if ($txt[0][$k]) { $t = 3 }
    $top[$k] = $t
}
[Chip]::BuildHalf($top, "$out\front", "$prev\chip_front.png")

# ---------- back: QR (V4-H, 30% error correction) with an X emblem in the centre ----------
$mask = 0
$n = 33
$qr = [CoinGen]::MakeQr($Url, 4, [ref]$mask)
"QR {0}x{0} V4-H mask={1}" -f $n, $mask
$mod = $QrModule; $qy = 0
$plate = [Chip]::RoundRect(0, $qy, $n * $mod / 2 + 2 * $mod, 0.5)            # white plate incl. 2-module quiet zone
$qm = [Chip]::QrModules($qr, $mod, 0, $qy)
$clear = [Chip]::RoundRect(0, $qy, ($XClear / 2) * $mod, 0.6)                # blank centre (XClear x XClear modules)
$xe = [Chip]::XEmblem(0, $qy, $XSize / 2, $XBadge / 2, 0.85, 0.62, 0.36, 0.23, 0.13)
$top2 = New-Object int[] $NN
for ($k = 0; $k -lt $NN; $k++) {
    $t = -1
    if ($plate[$k]) { $t = if ($qm[$k] -and -not $clear[$k]) { 2 } else { 1 } }
    if ($xe[$k] -ge 0) { $t = $xe[$k] }
    $top2[$k] = $t
}
[Chip]::BuildHalf($top2, "$out\back", "$prev\chip_back.png")

# ---------- one-piece chip for AMS (no gluing): back = flat inlay on the bottom ----------
$one = Join-Path $PSScriptRoot 'onepiece'; New-Item -ItemType Directory -Force $one | Out-Null
$ms = [OnePiece]::Make($top, $top2)
for ($c = 0; $c -lt 4; $c++) {
    if ($ms[$c].Tris -gt 0) { [OnePiece]::WriteStl("$one\chip_$c`_$([OnePiece]::Names[$c]).stl", $ms[$c]); "{0,-8} {1,8} tris" -f [OnePiece]::Names[$c], $ms[$c].Tris }
}
"run make_bambu_3mf.ps1 next to build the Bambu Studio project"