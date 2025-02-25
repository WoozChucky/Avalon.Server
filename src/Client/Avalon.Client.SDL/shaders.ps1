# Define paths
$currentDir = Get-Location
$sourceDir = Join-Path $currentDir "Assets\Shaders\Source"
$outputDir = Join-Path $currentDir "bin\Debug\net9.0\Assets\Shaders\Compiled\SPIRV"

# Ensure output directory exists
if (-not (Test-Path $outputDir))
{
    New-Item -ItemType Directory -Path $outputDir -Force
}

# Get all .vert.glsl and .frag.glsl files
$shaderFiles = Get-ChildItem -Path $sourceDir -Recurse -Include *.vert.glsl, *.frag.glsl

# Loop through each shader file
foreach ($shaderFile in $shaderFiles)
{
    # Determine shader type (vertex or fragment) based on file extension
    if ($shaderFile.Name -like "*.vert.glsl")
    {
        $shaderStage = "vert"
    }
    elseif ($shaderFile.Name -like "*.frag.glsl")
    {
        $shaderStage = "frag"
    }
    else
    {
        continue
    }

    # Construct output file path (replace .glsl with .spv)
    $relativePath = $shaderFile.FullName.Substring($sourceDir.Length).TrimStart('\')
    $outputPath = Join-Path $outputDir ($relativePath -replace ".glsl$", ".spv")

    # Ensure output directory for the file exists
    $outputFileDir = Split-Path $outputPath
    if (-not (Test-Path $outputFileDir))
    {
        New-Item -ItemType Directory -Path $outputFileDir -Force
    }

    Write-Host "Compiling $shaderStage shader: $( $shaderFile.FullName )"

    # Compile the shader using glslc with -fshader-stage flag
    # Write-Host "Compiling $($shaderFile.FullName) to $outputPath"
    & C:/VulkanSDK/1.3.290.0/Bin/glslc "-fshader-stage=$shaderStage" $shaderFile.FullName "-o" $outputPath

    # Check if the compilation was successful
    if ($LASTEXITCODE -ne 0)
    {
        Write-Host "Failed to compile $( $shaderFile.FullName )" -ForegroundColor Red
    }
}
