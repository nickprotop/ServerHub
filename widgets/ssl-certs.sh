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
expired=0

# Store certificate info for tables
cert_data=""

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

                    if [ $days_left -lt 0 ]; then
                        status="expired"
                        ((expired++))
                        ((expiring_soon++))
                    elif [ $days_left -lt 7 ]; then
                        status="error"
                        ((expiring_soon++))
                    elif [ $days_left -lt 30 ]; then
                        status="warn"
                        ((expiring_soon++))
                    else
                        status="ok"
                    fi

                    expiry_short=$(echo "$expiry" | cut -d' ' -f1-3)
                    cert_data="$cert_data|$domain|$expiry_short|$days_left|$status|letsencrypt"
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

                if [ $days_left -lt 0 ]; then
                    status="expired"
                    ((expired++))
                    ((expiring_soon++))
                elif [ $days_left -lt 7 ]; then
                    status="error"
                    ((expiring_soon++))
                elif [ $days_left -lt 30 ]; then
                    status="warn"
                    ((expiring_soon++))
                else
                    status="ok"
                fi

                expiry_short=$(echo "$expiry" | cut -d' ' -f1-3)
                cert_data="$cert_data|$domain|$expiry_short|$days_left|$status|custom"
                ((found_certs++))
            fi
        fi
    done
fi

# Dashboard mode: compact table
if [ "$EXTENDED" = false ]; then
    if [ $found_certs -eq 0 ]; then
        echo "row: [status:warn] No SSL certificates found"
        echo "row: "
        echo "row: [grey70]Checked paths:[/]"
        echo "row: [grey70]  - /etc/letsencrypt/live[/]"
        echo "row: [grey70]  - nginx configs[/]"
    else
        if [ $expired -gt 0 ]; then
            echo "row: [status:error] Total: $found_certs | Expired: [red]$expired[/] | Expiring soon: [yellow]$expiring_soon[/]"
        elif [ $expiring_soon -gt 0 ]; then
            echo "row: [status:warn] Total: $found_certs | Expiring soon: [yellow]$expiring_soon[/]"
        else
            echo "row: [status:ok] Total: $found_certs | All certificates valid"
        fi
        echo "row: "

        # Certificates table
        echo "row: [bold]SSL Certificates:[/]"
        echo "[table:Domain|Expires|Days Left|Status]"

        echo "$cert_data" | tr '|' '\n' | while IFS='|' read -r domain expiry days status type; do
            [ -z "$domain" ] && continue

            domain_short=$(echo "$domain" | cut -c1-25)
            type_label=$([ "$type" = "letsencrypt" ] && echo "" || echo " (custom)")

            if [ "$status" = "expired" ]; then
                echo "[tablerow:$domain_short$type_label|$expiry|[red]${days}d[/]|[red]Expired[/]]"
            elif [ "$status" = "error" ]; then
                echo "[tablerow:$domain_short$type_label|$expiry|[red]${days}d[/]|[red]Critical[/]]"
            elif [ "$status" = "warn" ]; then
                echo "[tablerow:$domain_short$type_label|$expiry|[yellow]${days}d[/]|[yellow]Warning[/]]"
            else
                echo "[tablerow:$domain_short$type_label|$expiry|[green]${days}d[/]|[green]OK[/]]"
            fi
        done | head -n 10

        # Show indicator if more certs exist
        if [ $found_certs -gt 10 ]; then
            remaining=$((found_certs - 10))
            echo "row: "
            echo "row: [grey70]... and $remaining more certificates[/]"
        fi
    fi
else
    # Extended mode: detailed certificate tables
    if [ $found_certs -eq 0 ]; then
        echo "row: [status:warn] No SSL certificates found"
        echo "row: "
        echo "row: [grey70]Checked paths:[/]"
        echo "row: [grey70]  - /etc/letsencrypt/live[/]"
        echo "row: [grey70]  - /etc/nginx/ssl[/]"
        echo "row: [grey70]  - nginx configs[/]"
    else
        echo "row: [status:$([ $expired -gt 0 ] && echo "error" || ([ $expiring_soon -gt 0 ] && echo "warn" || echo "ok"))] Total: $found_certs | Expired: [red]$expired[/] | Expiring soon: [yellow]$expiring_soon[/]"
        echo "row: "

        # All certificates table
        echo "row: [bold]All SSL Certificates:[/]"
        echo "[table:Domain|Type|Expires|Days Left|Status]"

        echo "$cert_data" | tr '|' '\n' | while IFS='|' read -r domain expiry days status type; do
            [ -z "$domain" ] && continue

            domain_short=$(echo "$domain" | cut -c1-30)
            type_label=$([ "$type" = "letsencrypt" ] && echo "Let's Encrypt" || echo "Custom")

            if [ "$status" = "expired" ]; then
                echo "[tablerow:$domain_short|$type_label|$expiry|[red]${days}d[/]|[red]Expired[/]]"
            elif [ "$status" = "error" ]; then
                echo "[tablerow:$domain_short|$type_label|$expiry|[red]${days}d[/]|[red]Critical[/]]"
            elif [ "$status" = "warn" ]; then
                echo "[tablerow:$domain_short|$type_label|$expiry|[yellow]${days}d[/]|[yellow]Warning[/]]"
            else
                echo "[tablerow:$domain_short|$type_label|$expiry|[green]${days}d[/]|[green]OK[/]]"
            fi
        done

        # Certificate details table
        echo "row: "
        echo "row: [divider]"
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
                        echo "[table:Property|Value]"

                        # Issuer
                        issuer=$(openssl x509 -noout -issuer -in "$cert_file" 2>/dev/null | sed 's/issuer=//')
                        issuer_short=$(echo "$issuer" | grep -oP 'O\s*=\s*\K[^,]+' | head -n1 | cut -c1-30)
                        echo "[tablerow:Issuer|$issuer_short]"

                        # Expiration
                        expiry=$(openssl x509 -enddate -noout -in "$cert_file" 2>/dev/null | cut -d= -f2)
                        expiry_epoch=$(date -d "$expiry" +%s 2>/dev/null)
                        now_epoch=$(date +%s)
                        days_left=$(( (expiry_epoch - now_epoch) / 86400 ))
                        echo "[tablerow:Expires|$expiry]"

                        if [ $days_left -lt 0 ]; then
                            echo "[tablerow:Days Left|[red]$days_left[/]]"
                        elif [ $days_left -lt 7 ]; then
                            echo "[tablerow:Days Left|[red]$days_left[/]]"
                        elif [ $days_left -lt 30 ]; then
                            echo "[tablerow:Days Left|[yellow]$days_left[/]]"
                        else
                            echo "[tablerow:Days Left|[green]$days_left[/]]"
                        fi

                        # Subject Alternative Names (SANs)
                        sans=$(openssl x509 -noout -text -in "$cert_file" 2>/dev/null | grep -A1 "Subject Alternative Name" | tail -n1 | tr ',' '\n' | grep "DNS:" | head -n 3)
                        san_count=$(echo "$sans" | wc -l)
                        if [ -n "$sans" ]; then
                            sans_short=$(echo "$sans" | sed 's/DNS://g' | xargs | sed 's/ /, /g' | cut -c1-40)
                            echo "[tablerow:SANs ($san_count)|$sans_short]"
                        fi

                        # Key size
                        key_size=$(openssl x509 -noout -text -in "$cert_file" 2>/dev/null | grep "Public-Key:" | head -n1 | grep -oP '\d+')
                        [ -n "$key_size" ] && echo "[tablerow:Key Size|${key_size} bit]"

                        # Serial number
                        serial=$(openssl x509 -noout -serial -in "$cert_file" 2>/dev/null | cut -d= -f2 | cut -c1-25)
                        echo "[tablerow:Serial|$serial...]"

                        # File path
                        echo "[tablerow:Path|$cert_file]"
                    fi
                fi
            done | head -n 100
        fi

        # Certbot status
        if command -v certbot &> /dev/null; then
            echo "row: "
            echo "row: [divider]"
            echo "row: "
            echo "row: [bold]Certbot Status:[/]"
            echo "[table:Property|Value]"

            certbot_version=$(certbot --version 2>&1 | head -n1 | cut -c1-40)
            echo "[tablerow:Version|$certbot_version]"

            # Renewal configs
            if [ -d "/etc/letsencrypt/renewal" ]; then
                renewal_count=$(ls /etc/letsencrypt/renewal/*.conf 2>/dev/null | wc -l)
                echo "[tablerow:Renewal Configs|$renewal_count]"
            fi

            # Last renewal attempt
            if [ -f "/var/log/letsencrypt/letsencrypt.log" ]; then
                last_renewal=$(grep -i "renew" /var/log/letsencrypt/letsencrypt.log 2>/dev/null | tail -n1 | cut -c1-30)
                [ -n "$last_renewal" ] && echo "[tablerow:Last Log Entry|$last_renewal...]"
            fi
        fi

        # Statistics
        echo "row: "
        echo "row: [divider:─:cyan1]"
        echo "row: "
        echo "row: [bold]Statistics:[/]"
        echo "row: [grey70]Total certificates: $found_certs[/]"
        echo "row: [grey70]Expired: $expired[/]"
        echo "row: [grey70]Expiring soon (< 30 days): $expiring_soon[/]"
        valid=$((found_certs - expiring_soon))
        echo "row: [grey70]Valid: $valid[/]"
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
