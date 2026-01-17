#!/bin/bash
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

echo "title: SSL Certificates"
echo "refresh: 3600"  # Refresh every hour

# Common certificate paths
CERT_PATHS=(
    "/etc/ssl/certs"
    "/etc/letsencrypt/live"
    "/etc/nginx/ssl"
    "/etc/apache2/ssl"
)

found_certs=0

# Check Let's Encrypt certificates
if [ -d "/etc/letsencrypt/live" ]; then
    for domain_dir in /etc/letsencrypt/live/*/; do
        if [ -d "$domain_dir" ]; then
            domain=$(basename "$domain_dir")
            cert_file="${domain_dir}cert.pem"

            if [ -f "$cert_file" ]; then
                # Get expiration date
                expiry=$(openssl x509 -enddate -noout -in "$cert_file" 2>/dev/null | cut -d= -f2)

                if [ -n "$expiry" ]; then
                    expiry_epoch=$(date -d "$expiry" +%s 2>/dev/null)
                    now_epoch=$(date +%s)
                    days_left=$(( (expiry_epoch - now_epoch) / 86400 ))

                    if [ $days_left -lt 7 ]; then
                        status="error"
                    elif [ $days_left -lt 30 ]; then
                        status="warn"
                    else
                        status="ok"
                    fi

                    echo "row: [status:$status] [cyan1]$domain[/]"
                    echo "row:   Expires in: [yellow]$days_left[/] days"
                    echo "row:   [grey70]${expiry:0:11}[/]"
                    echo "row: "

                    ((found_certs++))
                fi
            fi
        fi
    done
fi

# Check for custom SSL certificates in nginx config
if command -v nginx &> /dev/null && [ -d "/etc/nginx" ]; then
    # Find SSL certificate directives in nginx configs
    ssl_certs=$(grep -r "ssl_certificate " /etc/nginx/ 2>/dev/null | grep -v "ssl_certificate_key" | awk '{print $NF}' | tr -d ';' | sort -u)

    echo "$ssl_certs" | while read -r cert_path; do
        if [ -f "$cert_path" ] && [[ "$cert_path" != *"letsencrypt"* ]]; then
            # Get domain from certificate
            domain=$(openssl x509 -noout -subject -in "$cert_path" 2>/dev/null | grep -oP 'CN\s*=\s*\K[^,]+' | head -n1)
            expiry=$(openssl x509 -enddate -noout -in "$cert_path" 2>/dev/null | cut -d= -f2)

            if [ -n "$expiry" ] && [ -n "$domain" ]; then
                expiry_epoch=$(date -d "$expiry" +%s 2>/dev/null)
                now_epoch=$(date +%s)
                days_left=$(( (expiry_epoch - now_epoch) / 86400 ))

                if [ $days_left -lt 7 ]; then
                    status="error"
                elif [ $days_left -lt 30 ]; then
                    status="warn"
                else
                    status="ok"
                fi

                echo "row: [status:$status] [cyan1]$domain[/] (custom)"
                echo "row:   Expires in: [yellow]$days_left[/] days"
                echo "row: "

                ((found_certs++))
            fi
        fi
    done
fi

if [ $found_certs -eq 0 ]; then
    echo "row: [status:warn] No SSL certificates found"
    echo "row: "
    echo "row: [grey70]Checked paths:[/]"
    echo "row: [grey70]  - /etc/letsencrypt/live[/]"
    echo "row: [grey70]  - /etc/nginx/ssl[/]"
    echo "row: [grey70]  - nginx configs[/]"
fi

# Summary
if [ $found_certs -gt 0 ]; then
    echo "row: [grey70]Total certificates: $found_certs[/]"
fi

# Action to renew Let's Encrypt
if [ -d "/etc/letsencrypt" ]; then
    echo "action: Renew Let's Encrypt:certbot renew"
fi
