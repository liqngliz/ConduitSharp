#!/bin/sh
# Docker Desktop resolves host.docker.internal for us; plain Linux Docker does not, and the
# `local` route needs it to reach a model server on the host. Without this the container talks to
# nothing and the failure is a silent connection refused, so map it here rather than making every
# Linux user remember --add-host.
if ! getent hosts host.docker.internal >/dev/null 2>&1; then
    # /proc/net/route field 3 is the gateway as little-endian hex, e.g. 010011AC for 172.17.0.1.
    # Built with plain hex arithmetic: the base image ships mawk, which has no strtonum().
    gw=$(awk '
        function hex(s,   i, c, n) {
            n = 0
            for (i = 1; i <= length(s); i++) {
                c = index("0123456789ABCDEF", toupper(substr(s, i, 1))) - 1
                if (c < 0) return -1
                n = n * 16 + c
            }
            return n
        }
        $2 == "00000000" && $8 == "00000000" {
            print hex(substr($3,7,2)) "." hex(substr($3,5,2)) "." hex(substr($3,3,2)) "." hex(substr($3,1,2))
            exit
        }' /proc/net/route 2>/dev/null)

    case "$gw" in
        [0-9]*.[0-9]*.[0-9]*.[0-9]*)
            echo "$gw host.docker.internal" >> /etc/hosts
            echo "conduit-spend: host.docker.internal -> $gw (added for Linux Docker)"
            ;;
        *)
            echo "conduit-spend: could not resolve the host address; the local-model route needs --add-host host.docker.internal:host-gateway" >&2
            ;;
    esac
fi

exec dotnet ConduitSharp.Spend.dll "$@"
