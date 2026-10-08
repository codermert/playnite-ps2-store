<#
  BD Soft PS2 Store - build script

  Compiles the plugin with the C# compiler that ships with Windows (.NET Framework 4.x),
  so no Visual Studio / .NET SDK is required, then packs a .pext with Playnite's Toolbox.

  Usage:
    powershell -ExecutionPolicy Bypass -File build.ps1 -PlayniteDir "D:\Playnite"
    powershell -ExecutionPolicy Bypass -File build.ps1 -PlayniteDir "D:\Playnite" -Install

  -Install copies the build into %APPDATA%\Playnite\Extensions (close Playnite first).
#>
param(
    [string]$PlayniteDir = 'D:\Playnite',
    [switch]$Install
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$src = Join-Path $root 'src'
$out = Join-Path $root 'build\BDSoftPS2Store'
$dist = Join-Path $root 'dist'

$fx = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
$csc = Join-Path $fx 'csc.exe'
$sdk = Join-Path $PlayniteDir 'Playnite.SDK.dll'
foreach ($required in @($csc, $sdk, (Join-Path $src 'catalog.json'))) {
    if (-not (Test-Path $required)) { throw "Missing: $required" }
}

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out, $dist | Out-Null

# Icon (generated once, kept in src)
$icon = Join-Path $src 'icon.png'
if (-not (Test-Path $icon)) {
    Add-Type -AssemblyName System.Drawing
    $bmp = New-Object System.Drawing.Bitmap 256, 256
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = 64
    $path.AddArc(0, 0, $r, $r, 180, 90); $path.AddArc(256 - $r, 0, $r, $r, 270, 90)
    $path.AddArc(256 - $r, 256 - $r, $r, $r, 0, 90); $path.AddArc(0, 256 - $r, $r, $r, 90, 90); $path.CloseFigure()
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point 256, 256), ([System.Drawing.Color]::FromArgb(0, 112, 209)), ([System.Drawing.Color]::FromArgb(0, 55, 145))
    $g.FillPath($brush, $path)
    $font = New-Object System.Drawing.Font 'Segoe UI', 74, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
    $g.DrawString('PS2', $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF 0, 0, 256, 256), $fmt)
    $g.Dispose()
    $bmp.Save($icon, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$sources = @('BDSoftPS2StorePlugin.cs', 'Models.cs', 'Services.cs', 'Gamepad.cs', 'StoreController.cs', 'StorePreviewControl.cs', 'StoreTileControl.cs', 'UiSounds.cs', 'DeveloperWindow.cs', 'ThemeIntegration.cs') | ForEach-Object { Join-Path $src $_ }
$refs = @(
    $sdk,
    (Join-Path $fx 'System.dll'),
    (Join-Path $fx 'System.Core.dll'),
    (Join-Path $fx 'System.Xaml.dll'),
    (Join-Path $fx 'System.Xml.dll'),
    (Join-Path $fx 'System.Runtime.Serialization.dll'),
    (Join-Path $fx 'WPF\PresentationCore.dll'),
    (Join-Path $fx 'WPF\PresentationFramework.dll'),
    (Join-Path $fx 'WPF\WindowsBase.dll')
)
$args = @('/nologo', '/target:library', '/optimize+', '/platform:anycpu', '/utf8output',
    ('/out:' + (Join-Path $out 'BDSoftPS2Store.dll')),
    ('/resource:' + (Join-Path $src 'StoreView.xaml') + ',BDSoftPS2Store.StoreView.xaml'))
$args += $refs | ForEach-Object { '/reference:' + $_ }
$args += $sources

Write-Host 'Compiling...'
& $csc $args
if ($LASTEXITCODE -ne 0) { throw "csc failed ($LASTEXITCODE)" }

Copy-Item (Join-Path $src 'extension.yaml'), (Join-Path $src 'catalog.json'), $icon $out
Copy-Item (Join-Path $src 'assets') (Join-Path $out 'assets') -Recurse

$toolbox = Join-Path $PlayniteDir 'Toolbox.exe'
Get-ChildItem $dist -Filter '*.pext' | Remove-Item -Force
if (Test-Path $toolbox) {
    Write-Host 'Packing .pext with Playnite Toolbox...'
    & $toolbox pack $out $dist | Out-Host
}
if (-not (Get-ChildItem $dist -Filter '*.pext')) {
    # Fallback: a .pext is a zip archive of the extension folder
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = Join-Path $dist 'BDSoftPS2Store_9411ebc2-4eeb-439b-84a2-ca7011456b22_1_0_0.pext'
    [System.IO.Compression.ZipFile]::CreateFromDirectory($out, $zip)
}
Get-ChildItem $dist -Filter '*.pext' | ForEach-Object { Write-Host ("Package: " + $_.FullName) }

if ($Install) {
    if (Get-Process -Name 'Playnite.DesktopApp', 'Playnite.FullscreenApp' -ErrorAction SilentlyContinue) {
        throw 'Close Playnite before using -Install.'
    }
    $target = Join-Path $env:APPDATA 'Playnite\Extensions\BDSoftPS2Store_9411ebc2-4eeb-439b-84a2-ca7011456b22'
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    Copy-Item $out $target -Recurse
    Write-Host "Installed to $target"
}
