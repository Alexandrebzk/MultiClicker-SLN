# Define color names and HEX codes
$colors = @{
    "yellow" = "#FFFF00"
    "red"    = "#FF0000"
    "green"  = "#00FF00"
    "brown"  = "#8B4513"
    "purple" = "#800080"
    "orange" = "#FFA500"
    "pink"   = "#FFC0CB"
    "white"  = "#FFFFFF"
    "black"  = "#000000"
    "blue"  = "#0013ff"
}

# Create cosmetics folder if it doesn't exist
$outputDir = "cosmetics"
if (!(Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

# Set image size
$width = 100
$height = 100

Add-Type -AssemblyName System.Drawing

foreach ($name in $colors.Keys) {
    $hex = $colors[$name]
    $filePath = Join-Path $outputDir "$name.png"
    if (!(Test-Path $filePath)) {
        $bitmap = New-Object System.Drawing.Bitmap $width, $height
        $color = [System.Drawing.ColorTranslator]::FromHtml($hex)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.Clear($color)
        $bitmap.Save($filePath, [System.Drawing.Imaging.ImageFormat]::Png)
        $graphics.Dispose()
        $bitmap.Dispose()
        Write-Host "Created $filePath"
    } else {
        Write-Host "$filePath already exists"
    }
}