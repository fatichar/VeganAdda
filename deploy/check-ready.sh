#!/usr/bin/env bash
set -euo pipefail

binding="${1:?Supply the loopback address and port, for example 127.0.0.1:5080}"
[[ "$binding" =~ ^127\.0\.0\.1:([0-9]{1,5})$ ]] || { echo 'Readiness probe must use a 127.0.0.1 binding' >&2; exit 1; }
port="${BASH_REMATCH[1]}"
(( 10#$port > 0 && 10#$port < 65536 )) || { echo 'Readiness probe port is out of range' >&2; exit 1; }

# The app filters Host against its production AllowedHosts list, which includes
# localhost but not 127.0.0.1. Pin the Host header while probing the loopback port.
curl --fail --silent --show-error --max-time 15 \
  --header 'Host: localhost' \
  "http://$binding/health/ready" > /dev/null
