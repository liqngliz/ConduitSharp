#!/usr/bin/env pwsh
# Generates a demo JWT signed with the same key as routes.json.
# Output: prints the Bearer token to stdout.
#
# Usage:
#   pwsh generate-token.ps1
#   $TOKEN = pwsh generate-token.ps1
#   curl http://localhost:5000/api/orders -H "Authorization: Bearer $TOKEN"

# Base64-encoded signing key — must match the 'signingKey' in routes.json.
# The gateway base64-decodes this before computing the HMAC, so we do the same.
$signingKey = "ZGVtby1zaWduaW5nLWtleS1jb25kdWl0c2hhcnAtZXhhbXBsZS0zMmNo"
$issuer     = "conduitsharp-demo"
$audience   = "conduitsharp-demo"
$subject    = "demo-user"
$expiryMins = 60

function ConvertTo-Base64Url([byte[]]$bytes) {
    [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+','-').Replace('/','_')
}

$header  = '{"alg":"HS256","typ":"JWT"}'
$now     = [DateTimeOffset]::UtcNow
$payload = [PSCustomObject]@{
    sub = $subject
    iss = $issuer
    aud = $audience
    iat = $now.ToUnixTimeSeconds()
    exp = $now.AddMinutes($expiryMins).ToUnixTimeSeconds()
    name = "Demo User"
    role = "analyst"
} | ConvertTo-Json -Compress

$h = ConvertTo-Base64Url ([System.Text.Encoding]::UTF8.GetBytes($header))
$p = ConvertTo-Base64Url ([System.Text.Encoding]::UTF8.GetBytes($payload))
$data = "$h.$p"

$keyBytes = [Convert]::FromBase64String($signingKey)
$hmac     = [System.Security.Cryptography.HMACSHA256]::new($keyBytes)
$sig      = ConvertTo-Base64Url ($hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($data)))

$token = "$data.$sig"
Write-Host $token
