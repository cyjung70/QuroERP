#!/usr/bin/env bash
set -euo pipefail
umask 077

: "${IMAGE_PREFIX:?IMAGE_PREFIX is required}"
: "${IMAGE_TAG:?IMAGE_TAG is required}"
: "${DEPLOY_URL:?DEPLOY_URL is required}"
[[ "$IMAGE_PREFIX" == quroerp ]] || exit 2
[[ "$IMAGE_TAG" =~ ^[a-f0-9]{40}-[0-9]+-[0-9]+$ ]] || exit 2
[[ "$DEPLOY_URL" == https://* ]] || exit 2

deploy_dir="${DEPLOY_DIRECTORY:-/opt/quroerp/production}"
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
test -r "$deploy_dir/deploy.env"
exec 9>"$deploy_dir/deploy.lock"
flock -w 300 9

compose=(docker compose --project-name quroerp-production --env-file "$deploy_dir/deploy.env" -f "$script_dir/compose.yaml")
# Validate without printing expanded secrets.
"${compose[@]}" config --quiet
for target in api web migrate; do
  docker image inspect "$IMAGE_PREFIX-$target:$IMAGE_TAG" > /dev/null
done
"${compose[@]}" pull postgres
"${compose[@]}" up -d --wait --wait-timeout 120 postgres
"${compose[@]}" run --rm --no-deps migrate
"${compose[@]}" up -d --wait --wait-timeout 240 api web

status="$(curl --silent --show-error --retry 5 --retry-all-errors --retry-delay 3 \
  --connect-timeout 10 --max-time 30 --fail --output /dev/null \
  --write-out '%{http_code}' "${DEPLOY_URL%/}/bc/login")"
test "$status" = 200

# Keep the manifest and image identity of successful releases outside the checkout.
release_dir="$deploy_dir/releases/$IMAGE_TAG"
mkdir -p "$release_dir"
cp "$script_dir/compose.yaml" "$release_dir/compose.yaml"
printf 'IMAGE_PREFIX=%s\nIMAGE_TAG=%s\n' "$IMAGE_PREFIX" "$IMAGE_TAG" > "$release_dir/images.env"
printf '%s\n' "$IMAGE_TAG" > "$deploy_dir/current-release"
printf 'Deployed %s\n' "$IMAGE_TAG"
