#!/bin/sh
set -e

mkdir -p /etc/nginx/certs

if [ ! -f /etc/nginx/certs/selfsigned.crt ] || [ ! -f /etc/nginx/certs/selfsigned.key ]; then
    echo "Generating self-signed SSL certificate for Nginx..."
    openssl req -x509 -nodes -days 3650 -newkey rsa:2048 \
        -keyout /etc/nginx/certs/selfsigned.key \
        -out /etc/nginx/certs/selfsigned.crt \
        -subj "/CN=localhost/O=TradingBot"
fi

exec "$@"
