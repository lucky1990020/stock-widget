Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root 'Assets'
$provider = Join-Path $root 'ProviderAssets'
New-Item -ItemType Directory -Force -Path $assets, $provider | Out-Null

function New-Canvas {
    param([int]$Width, [int]$Height)
    $bmp = New-Object System.Drawing.Bitmap($Width, $Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $bg = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(24, 28, 36))
    $g.FillRectangle($bg, 0, 0, $Width, $Height)
    $bg.Dispose()
    return @{ Bitmap = $bmp; Graphics = $g }
}

function Close-Canvas {
    param($Canvas, [string]$Path)
    $Canvas.Graphics.Dispose()
    $Canvas.Bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $Canvas.Bitmap.Dispose()
}

function Add-Bars {
    param($Graphics, [int]$Width, [int]$Height)
    $red = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 93, 100))
    $green = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(53, 208, 142))
    $blue = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(77, 141, 255))
    $unit = [Math]::Max(2, [int]($Width / 30))
    $baseY = [int]($Height * 0.78)
    for ($i = 0; $i -lt 5; $i++) {
        $h = [int]($Height * (0.12 + 0.1 * $i))
        $x = [int]($Width * 0.18 + $i * $unit * 3.2)
        $brush = if ($i -eq 4) { $red } elseif ($i -eq 2) { $green } else { $blue }
        $Graphics.FillRectangle($brush, $x, $baseY - $h, $unit * 2, $h)
    }
    $red.Dispose(); $green.Dispose(); $blue.Dispose()
}

function Add-Label {
    param($Graphics, [string]$Text, [float]$Size, [float]$X, [float]$Y, $Color)
    $font = New-Object System.Drawing.Font('Segoe UI', $Size, [System.Drawing.FontStyle]::Bold)
    $brush = New-Object System.Drawing.SolidBrush($Color)
    $Graphics.DrawString($Text, $font, $brush, $X, $Y)
    $font.Dispose(); $brush.Dispose()
}

# StoreLogo 50 x 50
$c = New-Canvas 50 50
Add-Bars $c.Graphics 50 50
Close-Canvas $c (Join-Path $assets 'StoreLogo.png')

# Square44x44Logo
$c = New-Canvas 44 44
Add-Bars $c.Graphics 44 44
Close-Canvas $c (Join-Path $assets 'Square44x44Logo.png')

# Square150x150Logo
$c = New-Canvas 150 150
Add-Bars $c.Graphics 150 150
Add-Label $c.Graphics 'A' 34 18 18 ([System.Drawing.Color]::FromArgb(245, 248, 255))
Close-Canvas $c (Join-Path $assets 'Square150x150Logo.png')

# Wide310x150Logo
$c = New-Canvas 310 150
Add-Bars $c.Graphics 310 150
Add-Label $c.Graphics 'A-SHARE' 22 150 34 ([System.Drawing.Color]::FromArgb(245, 248, 255))
Add-Label $c.Graphics 'MARKET WIDGET' 9 151 66 ([System.Drawing.Color]::FromArgb(150, 160, 178))
Close-Canvas $c (Join-Path $assets 'Wide310x150Logo.png')

# SplashScreen
$c = New-Canvas 620 300
Add-Bars $c.Graphics 620 300
Add-Label $c.Graphics 'A-SHARE' 34 330 60 ([System.Drawing.Color]::FromArgb(245, 248, 255))
Add-Label $c.Graphics 'MARKET WIDGET' 12 332 108 ([System.Drawing.Color]::FromArgb(150, 160, 178))
Close-Canvas $c (Join-Path $assets 'SplashScreen.png')

# Provider icon
$c = New-Canvas 48 48
Add-Bars $c.Graphics 48 48
Close-Canvas $c (Join-Path $provider 'StockIcon.png')

# Provider screenshot
$c = New-Canvas 600 300
$g = $c.Graphics
$card = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(24, 28, 36))
$g.FillRectangle($card, 0, 0, 600, 300)
$card.Dispose()
Add-Label $g 'A-SHARE' 16 26 18 ([System.Drawing.Color]::FromArgb(245, 248, 255))
Add-Label $g 'MARKET' 8 26 44 ([System.Drawing.Color]::FromArgb(150, 160, 178))

$rows = @(
    @('SSE', '3,663.72', '+0.08%', [System.Drawing.Color]::FromArgb(255, 93, 100)),
    @('SZSE', '11,241.54', '+0.86%', [System.Drawing.Color]::FromArgb(255, 93, 100)),
    @('CHINEXT', '2,494.40', '+1.46%', [System.Drawing.Color]::FromArgb(255, 93, 100)),
    @('STAR 50', '1,265.02', '+0.64%', [System.Drawing.Color]::FromArgb(255, 93, 100)),
    @('KWEICHOW MOUTAI', '1,439.00', '-0.41%', [System.Drawing.Color]::FromArgb(53, 208, 142)),
    @('CATL', '249.95', '+0.53%', [System.Drawing.Color]::FromArgb(255, 93, 100)),
    @('BYD', '84.44', '-0.54%', [System.Drawing.Color]::FromArgb(53, 208, 142))
)
$y = 74
foreach ($row in $rows) {
    $font = New-Object System.Drawing.Font('Segoe UI', 10, [System.Drawing.FontStyle]::Bold)
    $small = New-Object System.Drawing.Font('Segoe UI', 9)
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(235, 240, 248))
    $valueBrush = New-Object System.Drawing.SolidBrush($row[3])
    $g.DrawString($row[0], $small, $white, 26, $y + 4)
    $g.DrawString($row[1], $font, $white, 300, $y)
    $g.DrawString($row[2], $font, $valueBrush, 470, $y)
    $line = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(28, 255, 255, 255), 1)
    $g.DrawLine($line, 26, $y + 29, 574, $y + 29)
    $line.Dispose(); $font.Dispose(); $small.Dispose(); $white.Dispose(); $valueBrush.Dispose()
    $y += 32
}

Close-Canvas $c (Join-Path $provider 'StockScreenshot.png')

Write-Output "Assets generated in $assets and $provider"
