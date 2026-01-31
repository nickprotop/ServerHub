#!/bin/bash
# Generates templates/index.json from template.yaml files

set -e

TEMPLATES_DIR="templates"
OUTPUT_FILE="$TEMPLATES_DIR/index.json"
TIMESTAMP=$(date -u +"%Y-%m-%dT%H:%M:%SZ")

echo "Generating template index..."

# Check if yq is installed
if ! command -v yq &> /dev/null; then
    echo "Error: yq is not installed"
    echo "Install it with: sudo wget -qO /usr/local/bin/yq https://github.com/mikefarah/yq/releases/latest/download/yq_linux_amd64 && sudo chmod +x /usr/local/bin/yq"
    exit 1
fi

# Start JSON
cat > "$OUTPUT_FILE" <<EOF
{
  "schema_version": "1.0",
  "updated_at": "$TIMESTAMP",
  "templates": [
EOF

FIRST=true
for template_dir in "$TEMPLATES_DIR"/*; do
    if [ -d "$template_dir" ] && [ -f "$template_dir/template.yaml" ]; then
        TEMPLATE_ID=$(basename "$template_dir")

        # Extract metadata from template.yaml
        NAME=$(yq '.display_name' "$template_dir/template.yaml")
        DESC=$(yq '.description' "$template_dir/template.yaml")
        LANG=$(yq '.language' "$template_dir/template.yaml")
        DIFF=$(yq '.difficulty' "$template_dir/template.yaml")
        TAGS=$(yq '.tags | @json' "$template_dir/template.yaml")

        # Add comma separator (not for first item)
        if [ "$FIRST" = false ]; then
            echo "," >> "$OUTPUT_FILE"
        fi
        FIRST=false

        # Add template entry
        cat >> "$OUTPUT_FILE" <<ENTRY
    {
      "id": "$TEMPLATE_ID",
      "name": $NAME,
      "description": $DESC,
      "language": $LANG,
      "difficulty": $DIFF,
      "tags": $TAGS
    }
ENTRY
    fi
done

# Close JSON
cat >> "$OUTPUT_FILE" <<EOF

  ]
}
EOF

echo "✓ Generated $OUTPUT_FILE"
echo "  Templates found: $(ls -d $TEMPLATES_DIR/*/ 2>/dev/null | wc -l)"
