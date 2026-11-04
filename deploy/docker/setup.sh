#!/usr/bin/env sh
# Docket server setup: writes .env with fresh secrets, starts the stack, and walks you through the first account.
# Needs Docker with the compose plugin. Run from this folder:  ./setup.sh
set -eu
cd "$(dirname "$0")"

command -v docker >/dev/null 2>&1 || { echo "Docker isn't installed. See https://docs.docker.com/engine/install/"; exit 1; }
docker compose version >/dev/null 2>&1 || { echo "The Docker compose plugin isn't installed."; exit 1; }

secret() { od -An -N32 -tx1 /dev/urandom | tr -d ' \n'; }

if [ ! -f .env ]; then
  printf 'Domain for this server (DNS must point here, ports 80+443 open), e.g. tasks.example.com: '
  read -r domain
  default_tz=$(cat /etc/timezone 2>/dev/null || readlink /etc/localtime 2>/dev/null | sed 's|.*/zoneinfo/||')
  [ -n "$default_tz" ] || default_tz=Etc/UTC
  printf 'Time zone [%s]: ' "$default_tz"
  read -r tz
  [ -n "$tz" ] || tz=$default_tz
  sed -e "s|^DOCKET_DOMAIN=.*|DOCKET_DOMAIN=$domain|" \
      -e "s|^TZ=.*|TZ=$tz|" \
      -e "s|^VIKUNJA_SECRET=.*|VIKUNJA_SECRET=$(secret)|" \
      -e "s|^DOCKET_MCP_KEY=.*|DOCKET_MCP_KEY=$(secret)|" \
      .env.example > .env
  chmod 600 .env
  echo "Wrote .env (keep it private; it holds your secrets)."
fi
. ./.env

docker compose pull --quiet 2>/dev/null || true
docker compose up -d
echo
echo "1. Open https://$DOCKET_DOMAIN and create your account (the first certificate can take a minute)."
printf '   Press Enter when your account exists... '
read -r _
echo
echo "2. Sign in once so the AI connector gets its own no-delete token:"
docker compose exec connector docket-mcp setup
echo
if grep -q '^VIKUNJA_REGISTRATION=true' .env; then
  printf '3. Close sign-ups so nobody else can make an account? [Y/n] '
  read -r ans
  case "$ans" in
    n|N) ;;
    *) sed -i.bak 's|^VIKUNJA_REGISTRATION=true|VIKUNJA_REGISTRATION=false|' .env && rm -f .env.bak && docker compose up -d vikunja ;;
  esac
fi
echo
echo "Done."
echo "  Web app and Docket apps: https://$DOCKET_DOMAIN"
echo "  AI connector (MCP):      https://$DOCKET_DOMAIN/mcp"
echo "  OpenAPI for GPT actions: https://$DOCKET_DOMAIN/openapi.json"
echo "  Connector key:           DOCKET_MCP_KEY in $(pwd)/.env"
echo "  Hook up your AI:         docs/ai/README.md"
