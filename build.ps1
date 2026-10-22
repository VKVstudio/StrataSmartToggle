# Build script for StrataSmartToggle (Zero-dependency Windows 10/11 build)
$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$IcoPath = Join-Path $Root "strata-icon.ico"
$ExePath = Join-Path $Root "StrataSmartToggle.exe"
$CsFile  = Join-Path $Root "StrataSmartToggle.cs"

Add-Type -AssemblyName System.Drawing

if (!(Test-Path $IcoPath)) {
    $bmp = New-Object System.Drawing.Bitmap(256, 256)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $plates = @(
        @{ Y = 172; C1 = [System.Drawing.Color]::FromArgb(255, 14, 165, 233); C2 = [System.Drawing.Color]::FromArgb(255, 56, 189, 248) },
        @{ Y = 128; C1 = [System.Drawing.Color]::FromArgb(255, 99, 102, 241); C2 = [System.Drawing.Color]::FromArgb(255, 129, 140, 248) },
        @{ Y = 84;  C1 = [System.Drawing.Color]::FromArgb(255, 168, 85, 247); C2 = [System.Drawing.Color]::FromArgb(255, 216, 180, 254) }
    )
    foreach ($p in $plates) {
        $y = $p.Y
        $topFace = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF(128, ($y - 42))),
            (New-Object System.Drawing.PointF(228, $y)),
            (New-Object System.Drawing.PointF(128, ($y + 42))),
            (New-Object System.Drawing.PointF(28,  $y))
        )
        $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            (New-Object System.Drawing.PointF(28, ($y - 42))),
            (New-Object System.Drawing.PointF(228, ($y + 42))),
            $p.C2, $p.C1
        )
        $g.FillPolygon($grad, $topFace)
        $penEdge = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(110, 255, 255, 255), 2.5)
        $g.DrawPolygon($penEdge, $topFace)
        $grad.Dispose(); $penEdge.Dispose()
    }
    $g.Dispose()

    $msPng = New-Object System.IO.MemoryStream
    $bmp.Save($msPng, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBytes = $msPng.ToArray()
    $fsIco = [System.IO.File]::Create($IcoPath)
    $bw = New-Object System.IO.BinaryWriter($fsIco)
    $bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]1)
    $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$pngBytes.Length); $bw.Write([UInt32]22)
    $bw.Write($pngBytes)
    $bw.Close(); $fsIco.Close(); $bmp.Dispose()
}

$csc = Join-Path ([System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) "csc.exe"
$wpfDir = Join-Path ([System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) "WPF"

Get-Process -Name "StrataSmartToggle" -ErrorAction SilentlyContinue | Stop-Process -Force

& $csc /nologo /target:winexe /out:"$ExePath" /win32icon:"$IcoPath" `
    /r:"$wpfDir\PresentationFramework.dll" `
    /r:"$wpfDir\PresentationCore.dll" `
    /r:"$wpfDir\WindowsBase.dll" `
    /r:"System.Xaml.dll" `
    "$CsFile"

Write-Host "Built successfully: $ExePath" -ForegroundColor Green
