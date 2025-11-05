#!/bin/bash

# Test Discord Webhook for Fightarr releases
# Usage: ./test-discord-webhook.sh <your-webhook-url>

if [ -z "$1" ]; then
  echo "Usage: $0 <discord-webhook-url>"
  echo "Example: $0 https://discord.com/api/webhooks/..."
  exit 1
fi

WEBHOOK_URL="$1"
VERSION="v4.0.999.999"
VERSION_NUMBER="4.0.999.999"
RELEASE_URL="https://github.com/Fightarr/Fightarr/releases/tag/$VERSION"
CHANGELOG_URL="https://github.com/Fightarr/Fightarr/blob/main/CHANGELOG.md"

echo "Sending test notification to Discord..."
echo "Version: $VERSION"
echo "Release URL: $RELEASE_URL"

# Get recent commits for the test (last 5)
COMMITS=$(git log --pretty=format:"• %s" -n 5 --no-merges | sed 's/"/\\"/g' | sed ':a;N;$!ba;s/\n/\\n/g')

# Build description with commits
DESCRIPTION="**What's New**\n${COMMITS}\n\n**[📋 View Full Changelog](${CHANGELOG_URL})** • **[📦 View Release](${RELEASE_URL})**\n\n**Docker Installation**\n\`\`\`\ndocker pull fightarr/fightarr:latest\ndocker pull fightarr/fightarr:${VERSION_NUMBER}\n\`\`\`"

curl -X POST "$WEBHOOK_URL" \
  -H "Content-Type: application/json" \
  -d "{
    \"username\": \"Fightarr\",
    \"embeds\": [{
      \"title\": \"New Release - $VERSION (TEST)\",
      \"description\": \"$DESCRIPTION\",
      \"color\": 14427686,
      \"timestamp\": \"$(date -u +%Y-%m-%dT%H:%M:%S.000Z)\"
    }]
  }"

echo ""
echo "Test notification sent! Check your Discord server."
