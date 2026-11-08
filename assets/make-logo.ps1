# USBridge logo generator (ASCII only)
# Draws the logo with GDI+ and emits:
#   assets\logo.ico  (multi-size: 16/24/32/48/64/128/256)
#   assets\logo.png  (256, for README)
param([string]$OutDir = (Split-Path -Parent $MyInvocation.MyCommand.Path))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-Logo([int]$size) {
  $k = $size / 256.0
  $bmp = New-Object System.Drawing.Bitmap($size, $size)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

  # rounded tile
  $r = 56 * $k; $d = 2 * $r
  $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
  $tile.AddArc(0, 0, $d, $d, 180, 90)
  $tile.AddArc($size - $d, 0, $d, $d, 270, 90)
  $tile.AddArc($size - $d, $size - $d, $d, $d, 0, 90)
  $tile.AddArc(0, $size - $d, $d, $d, 90, 90)
  $tile.CloseFigure()
  $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
  $c1 = [System.Drawing.Color]::FromArgb(255, 0x43, 0x93, 0xFF)
  $c2 = [System.Drawing.Color]::FromArgb(255, 0x1A, 0x56, 0xDB)
  $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, 62.0)
  $g.FillPath($grad, $tile)

  # top gloss (clipped to tile)
  $g.SetClip($tile)
  $gloss = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(22, 255, 255, 255))
  $g.FillRectangle($gloss, 0, 0, $size, [int]($size * 0.46))
  $g.ResetClip()

  # white glyph: USB trident standing on a bridge arch
  $pen = New-Object System.Drawing.Pen([System.Drawing.Brushes]::White, (13 * $k))
  $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'

  # bridge arch
  $g.DrawBezier($pen, (26*$k), (198*$k), (94*$k), (150*$k), (162*$k), (150*$k), (230*$k), (198*$k))
  # trident stem
  $g.DrawLine($pen, (128*$k), (170*$k), (128*$k), (70*$k))
  # arrow head
  $g.DrawLine($pen, (110*$k), (89*$k), (128*$k), (70*$k))
  $g.DrawLine($pen, (128*$k), (70*$k), (146*$k), (89*$k))
  # branches
  $g.DrawLine($pen, (128*$k), (126*$k), (95*$k), (151*$k))
  $g.DrawLine($pen, (128*$k), (126*$k), (161*$k), (151*$k))

  $thick = New-Object System.Drawing.Pen([System.Drawing.Brushes]::White, (10 * $k))
  # left circle (sits on arch)
  $g.DrawEllipse($thick, (85*$k), (149*$k), (20*$k), (20*$k))
  # right square (sits on arch)
  $g.DrawRectangle($thick, (152*$k), (151*$k), (18*$k), (18*$k))

  $g.Dispose()
  return $bmp
}

function Save-Ico {
  param([string]$path, [int[]]$sizes)
  $entries = @()
  foreach ($s in $sizes) {
    $bmp = New-Logo $s
    if ($s -ge 256) {
      $ms = New-Object System.IO.MemoryStream
      $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
      $entries += ,@($s, $ms.ToArray())
      $ms.Dispose()
    } else {
      $w = $s; $h = $s
      $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
      $bd = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
      $px = New-Object byte[] ($w * $h * 4)
      [System.Runtime.InteropServices.Marshal]::Copy($bd.Scan0, $px, 0, $px.Length)
      $bmp.UnlockBits($bd)
      $stride = $w * 4
      $maskStride = [int][Math]::Ceiling($w / 32.0) * 4
      $xorLen = $stride * $h
      $andLen = $maskStride * $h
      $ms = New-Object System.IO.MemoryStream
      $bw = New-Object System.IO.BinaryWriter($ms)
      $bw.Write([uint32]40); $bw.Write([int32]$w); $bw.Write([int32]($h * 2)); $bw.Write([uint16]1)
      $bw.Write([uint16]32); $bw.Write([uint32]0); $bw.Write([uint32]($xorLen + $andLen))
      $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([uint32]0); $bw.Write([uint32]0)
      for ($y = $h - 1; $y -ge 0; $y--) { $bw.Write($px, ($y * $stride), $stride) }
      $bw.Write((New-Object byte[] $andLen))
      $bw.Flush()
      $entries += ,@($s, $ms.ToArray())
      $bw.Dispose(); $ms.Dispose()
    }
    $bmp.Dispose()
  }
  $out = New-Object System.IO.MemoryStream
  $bw = New-Object System.IO.BinaryWriter($out)
  $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$entries.Count)
  $offset = 6 + 16 * $entries.Count
  foreach ($e in $entries) {
    $edge = if ($e[0] -ge 256) { 0 } else { $e[0] }
    $bw.Write([byte]$edge); $bw.Write([byte]$edge); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$e[1].Length); $bw.Write([uint32]$offset)
    $offset += $e[1].Length
  }
  foreach ($e in $entries) { $bw.Write($e[1]) }
  $bw.Flush()
  [IO.File]::WriteAllBytes($path, $out.ToArray())
  $bw.Dispose(); $out.Dispose()
}

$icoPath = Join-Path $OutDir 'logo.ico'
$pngPath = Join-Path $OutDir 'logo.png'
Save-Ico -path $icoPath -sizes @(16, 24, 32, 48, 64, 128, 256)
$png = New-Logo 256
$png.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$png.Dispose()
Write-Output ("OK: " + $icoPath + "  +  " + $pngPath)
