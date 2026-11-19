# Builds a Bambu Studio project (.3mf) from the one-piece colour STLs using Bambu Studio's own CLI,
# then fixes the project's filament list to 4 colours (orange / white / black / navy).
# Usage: powershell -File make_bambu_3mf.ps1 [-Printer "Bambu Lab P1S 0.4 nozzle"] [-Process "0.20mm Standard @BBL X1C"]
param(
    [string]$Printer = 'Bambu Lab P1S 0.4 nozzle',
    [string]$Process = '0.20mm Standard @BBL X1C',
    [string]$Filament = 'Bambu PLA Basic @BBL P1S 0.4 nozzle',
    [string]$Out = "$PSScriptRoot\yankee_MakerChip_Bambu.3mf"
)
$ErrorActionPreference = 'Stop'
$exe = 'C:\Program Files\Bambu Studio\bambu-studio.exe'
$prof = 'C:\Program Files\Bambu Studio\resources\profiles\BBL'
$tmp = Join-Path $env:TEMP 'yankee_bambu'; Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue; New-Item -ItemType Directory $tmp | Out-Null
$names = 'orange', 'white', 'black', 'navy'
$colors = '#F28A1E', '#F4F2EC', '#262626', '#1F3A8F'

# 1) assemble the 4 STLs into one object, part n -> filament n
$objs = @()
for ($c = 0; $c -lt 4; $c++) {
    $objs += [ordered]@{ path = (Resolve-Path "$PSScriptRoot\onepiece\chip_$c`_$($names[$c]).stl").Path; count = 1; filaments = @($c + 1)
                         assemble_index = @(1); pos_x = @(128.0); pos_y = @(128.0); pos_z = @(0.0) }
}
[ordered]@{ plates = @([ordered]@{ plate_name = 'yankee_MakerChip'; need_arrange = $false; objects = $objs }) } |
    ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 "$tmp\assemble.json"
$f = "$prof\filament\$Filament.json"
$a = @('--load-assemble-list', "`"$tmp\assemble.json`"", '--load-settings', "`"$prof\machine\$Printer.json;$prof\process\$Process.json`"",
       '--load-filaments', "`"$f;$f;$f;$f`"", '--outputdir', "`"$tmp`"", '--export-3mf', 'raw.3mf')
$p = Start-Process $exe -ArgumentList $a -Wait -PassThru -NoNewWindow
if ($p.ExitCode -ne 0) { throw "Bambu Studio CLI failed ($($p.ExitCode))" }

# 2) patch Metadata/project_settings.config: 4 filaments with the chip colours
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
Copy-Item "$tmp\raw.3mf" $Out -Force
$z = [IO.Compression.ZipFile]::Open($Out, 'Update')
$e = $z.GetEntry('Metadata/project_settings.config')
$sr = New-Object IO.StreamReader($e.Open()); $j = $sr.ReadToEnd() | ConvertFrom-Json; $sr.Close(); $e.Delete()
foreach ($pr in @($j.PSObject.Properties)) {
    if ($pr.Value -is [array] -and $pr.Value.Count -eq 1 -and ($pr.Name -like 'filament_*' -or $pr.Name -like 'nozzle_temperature_range_*')) {
        $pr.Value = @($pr.Value[0], $pr.Value[0], $pr.Value[0], $pr.Value[0])
    }
}
$j.filament_colour = $colors
$j.filament_settings_id = @($Filament, $Filament, $Filament, $Filament)
$ne = $z.CreateEntry('Metadata/project_settings.config')
$sw = New-Object IO.StreamWriter($ne.Open(), (New-Object Text.UTF8Encoding($false))); $sw.Write(($j | ConvertTo-Json -Depth 10)); $sw.Close()
$z.Dispose()
"written: $Out"
