#!/usr/bin/env bash
set -euo pipefail
umask 077

: "${IMAGE_PREFIX:?IMAGE_PREFIX is required}"
: "${IMAGE_TAG:?IMAGE_TAG is required}"
: "${DEPLOY_URL:?DEPLOY_URL is required}"
[[ "$IMAGE_PREFIX" == quroerp-dev ]] || exit 2
[[ "$IMAGE_TAG" =~ ^[a-f0-9]{40}-[0-9]+-[0-9]+$ ]] || exit 2
[[ "$DEPLOY_URL" == https://* ]] || exit 2

deploy_dir="${DEPLOY_DIRECTORY:-/opt/quroerp/dev}"
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
test -r "$deploy_dir/deploy.env"
exec 9>"$deploy_dir/deploy.lock"
flock -w 300 9

# 미등록 GitHub Secret의 빈 환경변수를 제거해야 Compose가 서버 파일 값을 사용합니다.
# 서버 파일은 source/eval하지 않고 Compose의 dotenv 형식으로만 읽습니다.
for secret_name in DB_CONNECTION JWT_SIGNING_KEY TLS_PASSWORD; do
  if [[ -z "${!secret_name:-}" ]]; then
    unset "$secret_name"
  fi
done

compose=(docker compose --project-name quroerp-dev --env-file "$deploy_dir/deploy.env" -f "$script_dir/compose.yaml")
# 확장된 설정에 비밀 값이 포함되므로 --quiet를 반드시 사용합니다.
"${compose[@]}" config --quiet
for target in api web migrate; do
  docker image inspect "$IMAGE_PREFIX-$target:$IMAGE_TAG" > /dev/null
done
# 외부 DB에 Schema/Seed만 적용합니다. DB 생성, 계정 변경, Demo 데이터는 실행하지 않습니다.
# 연결/마이그레이션 실패 시 set -e에 의해 API/Web 교체 전에 중단됩니다.
"${compose[@]}" run --rm --no-deps migrate
"${compose[@]}" up -d --wait --wait-timeout 240 api web

status="$(curl --silent --show-error --retry 5 --retry-all-errors --retry-delay 3 \
  --connect-timeout 10 --max-time 30 --fail --output /dev/null \
  --write-out '%{http_code}' "${DEPLOY_URL%/}/bc/login")"
test "$status" = 200

# 성공 기록에는 이미지 식별자와 비밀 값이 없는 매니페스트만 남깁니다.
release_dir="$deploy_dir/releases/$IMAGE_TAG"
mkdir -p "$release_dir"
cp "$script_dir/compose.yaml" "$release_dir/compose.yaml"
printf 'IMAGE_PREFIX=%s\nIMAGE_TAG=%s\n' "$IMAGE_PREFIX" "$IMAGE_TAG" > "$release_dir/images.env"
printf '%s\n' "$IMAGE_TAG" > "$deploy_dir/current-release"
printf 'Deployed %s\n' "$IMAGE_TAG"
