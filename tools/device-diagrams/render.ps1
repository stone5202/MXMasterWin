param([string]$Folder, [string]$WorkDir, [string]$Sheet = '')
# Renders every SVG in the folder to an 800x1040 transparent PNG with Edge (headless).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge was not found.' }
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
$profile2 = Join-Path $WorkDir 'edge-profile'
foreach ($svg in Get-ChildItem $Folder -Filter *.svg) {
    $png = [IO.Path]::ChangeExtension($svg.FullName, '.png')
    if (Test-Path $png) { Remove-Item $png -Confirm:$false }
    $page = Join-Path $WorkDir 'page.html'
    $url = 'file:///' + ($svg.FullName -replace '\\', '/')
    [IO.File]::WriteAllText($page, "<!doctype html><html><body style='margin:0;background:transparent'><img src='$url' width='800' height='1040' style='display:block'></body></html>")
    $pageUrl = 'file:///' + ($page -replace '\\', '/')
    $args2 = @('--headless=new', '--disable-gpu', '--hide-scrollbars', "--user-data-dir=$profile2", '--force-device-scale-factor=1', '--default-background-color=00000000', '--window-size=800,1040', '--allow-file-access-from-files', "--screenshot=$png", $pageUrl)
    $p = Start-Process -FilePath $edge -ArgumentList $args2 -WindowStyle Hidden -PassThru
    if (-not $p.WaitForExit(30000)) { Stop-Process -Id $p.Id -Force; throw "Edge timed out on $($svg.Name)" }
    for ($i = 0; $i -lt 40 -and -not (Test-Path $png); $i++) { Start-Sleep -Milliseconds 100 }
    if (-not (Test-Path $png)) { throw "No PNG for $($svg.Name)" }
}
"rendered: " + @(Get-ChildItem $Folder -Filter *.png).Count
# Smaller labeled copies and the model table that build.ps1 embeds in the Windows EXE.
$windows = Join-Path $Folder 'windows'
New-Item -ItemType Directory -Force -Path $windows | Out-Null
foreach ($file in Get-ChildItem $Folder -Filter *-labeled.png) {
    $source = [System.Drawing.Image]::FromFile($file.FullName)
    $small = New-Object System.Drawing.Bitmap 320, 416, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($small)
    $graphics.InterpolationMode = 'HighQualityBicubic'
    $graphics.DrawImage($source, 0, 0, 320, 416)
    $graphics.Dispose(); $source.Dispose()
    $small.Save((Join-Path $windows (($file.BaseName -replace '-labeled$', '') + '.png')), [System.Drawing.Imaging.ImageFormat]::Png)
    $small.Dispose()
}
$manifest = Get-Content (Join-Path $Folder 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$table = @($manifest.models.PSObject.Properties | ForEach-Object { $_.Name + '=' + $_.Value })
[IO.File]::WriteAllLines((Join-Path $windows 'models.txt'), [string[]]$table, (New-Object System.Text.ASCIIEncoding))
"windows copies: " + @(Get-ChildItem $windows -Filter *.png).Count + ", " + [int]((Get-ChildItem $windows | Measure-Object Length -Sum).Sum / 1024) + " KB"
if ($Sheet) {
    $files = @(Get-ChildItem $Folder -Filter *-labeled.png | Sort-Object Name)
    $cols = 6; $tw = 300; $th = 390
    $rows = [Math]::Ceiling($files.Count / $cols)
    $bmp = New-Object System.Drawing.Bitmap ($cols * $tw), ($rows * $th)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::White)
    $g.InterpolationMode = 'HighQualityBicubic'
    $font = New-Object System.Drawing.Font 'Segoe UI', 10, ([System.Drawing.FontStyle]::Bold)
    for ($i = 0; $i -lt $files.Count; $i++) {
        $img = [System.Drawing.Image]::FromFile($files[$i].FullName)
        $x = ($i % $cols) * $tw; $y = [Math]::Floor($i / $cols) * $th
        $g.DrawImage($img, $x, $y, $tw, $th)
        $img.Dispose()
        $g.DrawString(($files[$i].BaseName -replace '-labeled', ''), $font, [System.Drawing.Brushes]::Black, $x + 100, $y + 2)
    }
    $g.Dispose()
    $bmp.Save($Sheet, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}
