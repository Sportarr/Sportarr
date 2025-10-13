#!/bin/bash
# Script to create a GitHub release for Fightarr
# Automatically extracts version from src/Version.cs and changelog from CHANGELOG.md

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Extract version from Version.cs
VERSION=$(grep 'AppVersion =' src/Version.cs | sed 's/.*"\(.*\)".*/\1/')
TAG="v$VERSION"

echo -e "${GREEN}Fightarr Release Creator${NC}"
echo -e "${YELLOW}Version: $TAG${NC}"
echo ""

# Check if gh CLI is installed
if ! command -v gh &> /dev/null; then
    echo -e "${RED}Error: GitHub CLI (gh) is not installed${NC}"
    echo "Install it from: https://cli.github.com/"
    exit 1
fi

# Check if user is authenticated
if ! gh auth status &> /dev/null; then
    echo -e "${RED}Error: Not authenticated with GitHub CLI${NC}"
    echo "Run: gh auth login"
    exit 1
fi

# Check if tag already exists
if gh release view "$TAG" &> /dev/null; then
    echo -e "${YELLOW}Release $TAG already exists${NC}"
    read -p "Do you want to delete and recreate it? (y/N) " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        gh release delete "$TAG" --yes
        git tag -d "$TAG" 2>/dev/null || true
        git push origin --delete "$TAG" 2>/dev/null || true
    else
        echo "Aborted"
        exit 0
    fi
fi

# Extract changelog for this version
echo -e "${GREEN}Extracting changelog for $TAG...${NC}"
sed -n "/## \[$TAG\]/,/^## \[/p" CHANGELOG.md | sed '$d' > /tmp/release_notes.md

# If no changelog found, create a default message
if [ ! -s /tmp/release_notes.md ]; then
    echo -e "${YELLOW}Warning: No changelog found for $TAG in CHANGELOG.md${NC}"
    cat > /tmp/release_notes.md <<EOF
Release $TAG

See [CHANGELOG.md](https://github.com/Fightarr/Fightarr/blob/main/CHANGELOG.md) for details.
EOF
fi

# Show the release notes
echo -e "${GREEN}Release Notes:${NC}"
echo "----------------------------------------"
cat /tmp/release_notes.md
echo "----------------------------------------"
echo ""

# Confirm
read -p "Create release $TAG? (y/N) " -n 1 -r
echo
if [[ ! $REPLY =~ ^[Yy]$ ]]; then
    echo "Aborted"
    exit 0
fi

# Create the release
echo -e "${GREEN}Creating GitHub release $TAG...${NC}"
gh release create "$TAG" \
    --title "Fightarr $TAG" \
    --notes-file /tmp/release_notes.md

echo -e "${GREEN}✓ Release $TAG created successfully!${NC}"
echo "View it at: $(gh repo view --json url -q .url)/releases/tag/$TAG"

# Cleanup
rm -f /tmp/release_notes.md
