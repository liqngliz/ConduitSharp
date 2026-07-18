#!/usr/bin/env bash
# Generates a demo JWT signed with the same key as routes.json.
# Requires python3 (standard on macOS and most Linux distros).
#
# Usage:
#   bash generate-token.sh
#   TOKEN=$(bash generate-token.sh)
#   curl http://localhost:5000/api/orders -H "Authorization: Bearer $TOKEN"

python3 - <<'EOF'
import hmac, hashlib, base64, json, time

def b64u(data):
    if isinstance(data, str): data = data.encode()
    return base64.urlsafe_b64encode(data).rstrip(b'=').decode()

# Base64-encoded signing key — must match 'signingKey' in routes.json.
key = base64.b64decode("ZGVtby1zaWduaW5nLWtleS1jb25kdWl0c2hhcnAtZXhhbXBsZS0zMmNo")
now = int(time.time())

header  = b64u(json.dumps({"alg": "HS256", "typ": "JWT"}, separators=(',', ':')))
payload = b64u(json.dumps({
    "sub":  "demo-user",
    "iss":  "conduitsharp-demo",
    "aud":  "conduitsharp-demo",
    "iat":  now,
    "exp":  now + 3600,
    "name": "Demo User",
    "role": "analyst",
}, separators=(',', ':')))

msg = f"{header}.{payload}"
sig = b64u(hmac.new(key, msg.encode(), hashlib.sha256).digest())
print(f"{msg}.{sig}")
EOF
