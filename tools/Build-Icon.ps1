<#
/**
 * @file Build-Icon.ps1
 * @brief 从 XAML 矢量源生成透明 PNG 和多尺寸 ICO，仅在设计资源更新时运行。
 */
#>
$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    throw 'Run this script with Windows PowerShell -STA.'
}

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$assetDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'assets'
$reader = [Xml.XmlReader]::Create((Join-Path $assetDirectory 'app-icon.xaml'))
try { $image = [Windows.Markup.XamlReader]::Load($reader) }
finally { $reader.Dispose() }
if ($image -isnot [Windows.Media.DrawingImage]) {
    throw 'The icon source must be a DrawingImage.'
}
$image.Freeze()

<#
/** @brief 以指定像素尺寸直接渲染矢量源，返回透明位图。 */
#>
function Render-IconBitmap {
    param([int]$Size)

    $visual = New-Object Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    try {
        $context.PushTransform((New-Object Windows.Media.ScaleTransform(($Size / 256.0), ($Size / 256.0))))
        $context.DrawDrawing($image.Drawing)
        $context.Pop()
    }
    finally { $context.Close() }
    $bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap($Size, $Size, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32))
    $bitmap.Render($visual)
    $bitmap.Freeze()
    return $bitmap
}

<#
/** @brief 编码 PNG，供 256 像素图标帧和文档预览使用。 */
#>
function ConvertTo-PngBytes {
    param([Windows.Media.Imaging.BitmapSource]$Bitmap)

    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($Bitmap))
    $stream = New-Object IO.MemoryStream
    try {
        $encoder.Save($stream)
        return ,$stream.ToArray()
    }
    finally { $stream.Dispose() }
}

<#
/**
 * @brief 将小尺寸图标编码为 32 位 DIB，兼容 .NET Framework 的图标读取。
 * @details ICO 位图自底向上排列；高度包含颜色图与掩码，透明像素对应掩码位为一。
 */
#>
function ConvertTo-DibBytes {
    param([Windows.Media.Imaging.BitmapSource]$Bitmap)

    $width = $Bitmap.PixelWidth
    $height = $Bitmap.PixelHeight
    $stride = $width * 4
    $converted = New-Object Windows.Media.Imaging.FormatConvertedBitmap($Bitmap, ([Windows.Media.PixelFormats]::Bgra32), $null, 0)
    $pixels = New-Object byte[] ($stride * $height)
    $converted.CopyPixels($pixels, $stride, 0)
    $maskStride = [int][Math]::Ceiling($width / 32.0) * 4
    $mask = New-Object byte[] ($maskStride * $height)
    for ($y = 0; $y -lt $height; $y++) {
        for ($x = 0; $x -lt $width; $x++) {
            if ($pixels[$y * $stride + $x * 4 + 3] -eq 0) {
                $maskIndex = ($height - 1 - $y) * $maskStride + [int][Math]::Floor($x / 8.0)
                $mask[$maskIndex] = [byte]($mask[$maskIndex] -bor (0x80 -shr ($x % 8)))
            }
        }
    }

    $stream = New-Object IO.MemoryStream
    $writer = New-Object IO.BinaryWriter($stream)
    try {
        $writer.Write([uint32]40)
        $writer.Write([int32]$width)
        $writer.Write([int32]($height * 2))
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]0)
        $writer.Write([uint32]$pixels.Length)
        $writer.Write([int32]0)
        $writer.Write([int32]0)
        $writer.Write([uint32]0)
        $writer.Write([uint32]0)
        for ($y = $height - 1; $y -ge 0; $y--) {
            $writer.Write([byte[]]$pixels, [int]($y * $stride), [int]$stride)
        }
        $writer.Write([byte[]]$mask)
        $writer.Flush()
        return ,$stream.ToArray()
    }
    finally { $writer.Dispose(); $stream.Dispose() }
}

$sizes = @(16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 128, 256)
$frames = foreach ($size in $sizes) {
    $bitmap = Render-IconBitmap -Size $size
    $data = if ($size -eq 256) { ConvertTo-PngBytes -Bitmap $bitmap } else { ConvertTo-DibBytes -Bitmap $bitmap }
    [pscustomobject]@{ Size = $size; Data = $data }
}

<#
/** @brief ICO 使用 6 字节头和每帧 16 字节目录；256 像素以零编码。 */
#>
$iconStream = New-Object IO.MemoryStream
$writer = New-Object IO.BinaryWriter($iconStream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Data.Length)
        $writer.Write([uint32]$offset)
        $offset += $frame.Data.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Data) }
    $writer.Flush()
    [IO.File]::WriteAllBytes((Join-Path $assetDirectory 'app-icon.ico'), $iconStream.ToArray())
}
finally { $writer.Dispose(); $iconStream.Dispose() }
[IO.File]::WriteAllBytes((Join-Path $assetDirectory 'app-icon.png'), (ConvertTo-PngBytes -Bitmap (Render-IconBitmap -Size 512)))
Write-Output ('Generated assets/app-icon.ico (' + $frames.Count + ' sizes) and assets/app-icon.png.')
