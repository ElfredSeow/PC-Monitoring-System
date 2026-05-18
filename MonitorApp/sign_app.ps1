$certPath = Join-Path $PSScriptRoot "PC_Monitor_SelfSigned.cer"
$exePath = Join-Path $PSScriptRoot "Publish\PC Component Monitoring.exe"

# Create a self-signed code signing certificate
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=PC Component Monitoring" -FriendlyName "PC Component Monitoring Developer Cert" -NotAfter (Get-Date).AddYears(10)

# Export it just in case, but we have it in the $cert variable
Export-Certificate -Cert $cert -FilePath $certPath

# Sign the EXE
Set-AuthenticodeSignature -FilePath $exePath -Certificate $cert
