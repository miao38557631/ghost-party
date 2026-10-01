# Run before opening a fresh clone in Unity. Existing keys are never overwritten.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$keyDirectory = Join-Path $projectRoot 'Assets/UOSLauncherEncrypt'
$keyPath = Join-Path $keyDirectory 'EncryptKey.cs'
if (Test-Path -LiteralPath $keyPath) {
    Write-Output 'Local UOS key already exists; preserved without changes.'
    return
}
if (-not (Test-Path -LiteralPath $keyDirectory)) {
    throw 'Run this script from a complete Ghost Party checkout.'
}
$localKey = [Guid]::NewGuid().ToString('N')
$source = "namespace Unity.UOS.Encrypt`r`n{`r`n    public class EncryptKey`r`n    {`r`n        public static string Value = `"$localKey`";`r`n    }`r`n}`r`n"
# CreateNew also protects against an existing key created concurrently.
$stream = [System.IO.File]::Open($keyPath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write)
try {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($source)
    $stream.Write($bytes, 0, $bytes.Length)
} finally {
    $stream.Dispose()
}
Write-Output 'Created an ignored local UOS key. You can now open the project in Unity.'
