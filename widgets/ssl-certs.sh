#!/bin/bash
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

# Check for extended mode
EXTENDED=false
if [[ "$1" == "--extended" ]]; then
    EXTENDED=true
fi

echo "title: SSL Certificates"
echo "refresh: 3600"  # Refresh every hour

found_certs=0
expiring_soon=0

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
                        ((expiring_soon++))
                    elif [ $days_left -lt 30 ]; then
                        status="warn"
                        ((expiring_soon++))
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
    if [ $expiring_soon -gt 0 ]; then
        echo "row: [yellow]Expiring soon: $expiring_soon[/]"
    fi
fi

# Extended mode: detailed certificate information
if [ "$EXTENDED" = true ]; then
    echo "row: "
    echo "row: [bold]Certificate Details:[/]"

    if [ -d "/etc/letsencrypt/live" ]; then
        for domain_dir in /etc/letsencrypt/live/*/; do
            if [ -d "$domain_dir" ]; then
                domain=$(basename "$domain_dir")
                cert_file="${domain_dir}cert.pem"

                if [ -f "$cert_file" ]; then
                    echo "row: "
                    echo "row: [bold cyan1]$domain[/]"

                    # Issuer
                    issuer=$(openssl x509 -noout -issuer -in "$cert_file" 2>/dev/null | sed 's/issuer=//')
                    issuer_short=$(echo "$issuer" | grep -oP 'O\s*=\s*\K[^,]+' | head -n1)
                    echo "row: [grey70]Issuer: $issuer_short[/]"

                    # Subject Alternative Names (SANs)
                    sans=$(openssl x509 -noout -text -in "$cert_file" 2>/dev/null | grep -A1 "Subject Alternative Name" | tail -n1 | tr ',' '\n' | grep "DNS:" | head -n 5)
                    if [ -n "$sans" ]; then
                        echo "row: [grey70]SANs:[/]"
                        echo "$sans" | while read -r san; do
                            san_clean=$(echo "$san" | sed 's/DNS://g' | xargs)
                            echo "row: [grey70]  - $san_clean[/]"
                        done
                    fi

                    # Key type and size
                    key_info=$(openssl x509 -noout -text -in "$cert_file" 2>/dev/null | grep "Public Key Algorithm" | head -n1)
                    key_size=$(openssl x509 -noout -text -in "$cert_file" 2>/dev/null | grep "Public-Key:" | head -n1 | grep -oP '\d+')
                    echo "row: [grey70]Key: ${key_size:-unknown} bit[/]"

                    # Serial number
                    serial=$(openssl x509 -noout -serial -in "$cert_file" 2>/dev/null | cut -d= -f2)
                    echo "row: [grey70]Serial: ${serial:0:20}...[/]"

                    # File paths
                    echo "row: [grey70]Path: $cert_file[/]"
                fi
            fi
        done
    fi

    # Certbot status
    if command -v certbot &> /dev/null; then
        echo "row: "
        echo "row: [bold]Certbot Status:[/]"
        certbot_version=$(certbot --version 2>&1 | head -n1)
        echo "row: [grey70]$certbot_version[/]"

        # Show renewal config
        if [ -d "/etc/letsencrypt/renewal" ]; then
            renewal_count=$(ls /etc/letsencrypt/renewal/*.conf 2>/dev/null | wc -l)
            echo "row: [grey70]Renewal configs: $renewal_count[/]"
        fi
    fi
fi

# Actions
if [ -d "/etc/letsencrypt" ]; then
    echo "action: [sudo,refresh,timeout=180] Renew all certificates:certbot renew"

    if [ $expiring_soon -gt 0 ]; then
        echo "action: [sudo,danger,refresh,timeout=180] Force renewal:certbot renew --force-renewal"
    fi
fi

echo "action: [timeout=120] Test certificates:certbot certificates 2>/dev/null || echo 'Certbot not available'"
echo "action: Check OCSP status:openssl s_client -connect localhost:443 -status < /dev/null 2>/dev/null | grep -A1 'OCSP Response'"
