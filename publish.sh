#!/bin/bash
# ServerHub Release Publisher
# Bumps version and creates a new release tag
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

set -e

# Parse arguments
BUMP_TYPE="patch"
FORCE=false

while [[ $# -gt 0 ]]; do
    case $1 in
        --force|-f)
            FORCE=true
            shift
            ;;
        major|minor|patch)
            BUMP_TYPE="$1"
            shift
            ;;
        *)
            echo "Error: Invalid argument '$1'"
            echo "Usage: $0 [major|minor|patch] [--force]"
            echo ""
            echo "Examples:"
            echo "  $0              # Bump patch version (default)"
            echo "  $0 patch        # Bump patch version (1.0.0 -> 1.0.1)"
            echo "  $0 minor        # Bump minor version (1.0.0 -> 1.1.0)"
            echo "  $0 major        # Bump major version (1.0.0 -> 2.0.0)"
            echo "  $0 patch --force # Skip confirmation prompt"
            exit 1
            ;;
    esac
done

# Check for uncommitted changes
if ! git diff-index --quiet HEAD --; then
    echo "Error: You have uncommitted changes. Commit or stash them first."
    git status --short
    exit 1
fi

# Check for unpushed commits
CURRENT_BRANCH=$(git rev-parse --abbrev-ref HEAD)
UPSTREAM_BRANCH=$(git rev-parse --abbrev-ref --symbolic-full-name @{u} 2>/dev/null || echo "")

if [ -z "$UPSTREAM_BRANCH" ]; then
    echo "Warning: No upstream branch configured for '$CURRENT_BRANCH'"
    read -p "Continue anyway? [y/N] " -n 1 -r
    echo
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        echo "Aborted."
        exit 0
    fi
else
    LOCAL_COMMIT=$(git rev-parse HEAD)
    REMOTE_COMMIT=$(git rev-parse "$UPSTREAM_BRANCH")

    if [ "$LOCAL_COMMIT" != "$REMOTE_COMMIT" ]; then
        UNPUSHED=$(git log "$UPSTREAM_BRANCH..HEAD" --oneline 2>/dev/null | wc -l)
        if [ "$UNPUSHED" -gt 0 ]; then
            echo "Error: You have $UNPUSHED unpushed commit(s) on '$CURRENT_BRANCH'"
            echo ""
            git log "$UPSTREAM_BRANCH..HEAD" --oneline
            echo ""
            echo "Push your commits first: git push"
            exit 1
        fi
    fi
fi

# Get latest tag
LATEST_TAG=$(git describe --tags --abbrev=0 2>/dev/null || echo "v0.0.0")
echo "Latest tag: $LATEST_TAG"

# Remove 'v' prefix and parse version
VERSION="${LATEST_TAG#v}"
IFS='.' read -r MAJOR MINOR PATCH <<< "$VERSION"

# Validate version format
if [[ ! "$MAJOR" =~ ^[0-9]+$ ]] || [[ ! "$MINOR" =~ ^[0-9]+$ ]] || [[ ! "$PATCH" =~ ^[0-9]+$ ]]; then
    echo "Error: Invalid version format in tag '$LATEST_TAG'"
    echo "Expected format: v0.0.0"
    exit 1
fi

# Bump version based on type
case "$BUMP_TYPE" in
    major)
        MAJOR=$((MAJOR + 1))
        MINOR=0
        PATCH=0
        ;;
    minor)
        MINOR=$((MINOR + 1))
        PATCH=0
        ;;
    patch)
        PATCH=$((PATCH + 1))
        ;;
esac

NEW_VERSION="$MAJOR.$MINOR.$PATCH"
NEW_TAG="v$NEW_VERSION"

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "Creating new release: $NEW_TAG"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""
echo "  Previous: $LATEST_TAG"
echo "  New:      $NEW_TAG"
echo "  Type:     $BUMP_TYPE"
echo ""

# Confirm
if [ "$FORCE" = false ]; then
    read -p "Create and push tag '$NEW_TAG'? [y/N] " -n 1 -r
    echo
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        echo "Aborted."
        exit 0
    fi
fi

# Create and push tag
echo ""
echo "Creating tag $NEW_TAG..."
git tag -a "$NEW_TAG" -m "Release $NEW_TAG"

echo "Pushing tag to origin..."
git push origin "$NEW_TAG"

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "✓ Release $NEW_TAG published!"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""
echo "GitHub Actions will now:"
echo "  1. Build self-contained binaries (linux-x64, linux-arm64)"
echo "  2. Package widgets"
echo "  3. Create GitHub Release"
echo ""
echo "Watch progress at:"
echo "  https://github.com/nickprotop/ServerHub/actions"
echo ""
echo "Release will be available at:"
echo "  https://github.com/nickprotop/ServerHub/releases/tag/$NEW_TAG"
