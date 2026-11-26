# Compila y firma Battery Level y su instalador con el csc de .NET Framework 4 (viene con Windows).
# La firma usa el certificado de Ctrl + Save Design Studio guardado en esta PC (su clave privada no se puede exportar).
# Copyright (c) 2026 Ctrl + Save Design Studio. Todos los derechos reservados.
$ErrorActionPreference = 'Stop'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$huella = '56BDEEC1FB847B3C2A684D7D0E3F7FB26E620A6C'
Set-Location $PSScriptRoot
New-Item -ItemType Directory -Force dist | Out-Null

$cert = Get-Item "Cert:\CurrentUser\My\$huella" -ErrorAction SilentlyContinue
if (-not $cert -or -not $cert.HasPrivateKey) { throw 'No está el certificado de firma de Ctrl + Save Design Studio en esta PC.' }

function Firmar($archivo) {
    # El sello de tiempo hace que la firma siga valiendo después de que venza el certificado.
    # Al servidor solo se le manda el hash del archivo.
    $f = Set-AuthenticodeSignature -FilePath $archivo -Certificate $cert -HashAlgorithm SHA256 -TimestampServer 'http://timestamp.digicert.com'
    if (-not $f.SignerCertificate -or $f.SignerCertificate.Thumbprint -ne $huella) { throw "No se pudo firmar $archivo" }
}

& $csc /nologo /target:winexe /platform:x64 /optimize+ /win32icon:icono.ico /out:dist\BatteryLevel.exe `
  /r:System.Windows.Forms.dll /r:System.Drawing.dll BatteryLevel.cs Integridad.cs
if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación de BatteryLevel.exe' }
Firmar dist\BatteryLevel.exe

& $csc /nologo /target:winexe /platform:x64 /optimize+ /win32icon:icono.ico /out:dist\BatteryLevel-Setup.exe `
  /r:System.Windows.Forms.dll /r:System.Drawing.dll `
  "/resource:dist\BatteryLevel.exe,BatteryLevel.exe" "/resource:LICENSE,LICENSE" Instalador.cs Integridad.cs
if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación del instalador' }
Firmar dist\BatteryLevel-Setup.exe

$sumas = Get-ChildItem dist\*.exe | ForEach-Object { '{0}  {1}' -f (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name }
Set-Content dist\SHA256SUMS.txt $sumas -Encoding ascii

Get-ChildItem dist\*.exe | ForEach-Object {
    $s = Get-AuthenticodeSignature $_.FullName
    '{0}  {1:N0} KB  firmado por: {2}' -f $_.Name, ($_.Length / 1KB), $s.SignerCertificate.GetNameInfo('SimpleName', $false)
}
