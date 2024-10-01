param($out)
./build.ps1

if (!$out)
{
    $out = Join-Path $pwd output 
}

if (-Not (Test-Path $out))
{
    mkdir $out
}

Push-Location (Join-Path src vscode-avalonia)
vsce package --skip-license -o (Join-Path $out vscode-avalonia.vsix)

Pop-Location