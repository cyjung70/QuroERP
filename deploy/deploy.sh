#!/usr/bin/env bash
set -euo pipefail
umask 077

: "${IMAGE_PREFIX:?IMAGE_PREFIX is required}"
: "${IMAGE_TAG:?IMAGE_TAG is required}"
: "${DB_CONNECTION:?GitHub Secret DEV_DB_CONNECTION is required}"
[[ "$IMAGE_PREFIX" == quroerp-dev ]] || exit 2
[[ "$IMAGE_TAG" =~ ^[a-f0-9]{40}-[0-9]+-[0-9]+$ ]] || exit 2

# JWT 키와 배포 기록은 러너 계정 홈 아래에 보관합니다. /opt 경로 권한이 필요하지 않습니다.
readonly deploy_dir="${HOME:?GitHub runner HOME is required}/.local/state/quroerp/dev"
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
mkdir -p "$deploy_dir"

# DB 설정은 Actions 환경변수를 그대로 사용합니다. 호스트 deploy.env는 읽지 않습니다.
unset JWT_SIGNING_KEY

# 로그인 토큰 서명 키는 개발 서버에서 한 번 생성하고 이후 배포에도 재사용합니다.
# DB 비밀번호의 해시가 아니며 로그/이미지/릴리스 매니페스트에는 기록하지 않습니다.
readonly jwt_key_file="$deploy_dir/jwt-signing.key"
if [[ ! -e "$jwt_key_file" ]]; then
  generated_key="$(openssl rand -hex 32)"
  [[ "$generated_key" =~ ^[a-f0-9]{64}$ ]] || exit 2
  key_tmp="$(mktemp "$deploy_dir/.jwt-signing.XXXXXX")"
  printf '%s\n' "$generated_key" > "$key_tmp"
  mv -- "$key_tmp" "$jwt_key_file"
  unset generated_key
fi
chmod 600 "$jwt_key_file"
JWT_SIGNING_KEY="$(cat "$jwt_key_file")"
if [[ ! "$JWT_SIGNING_KEY" =~ ^[a-f0-9]{64}$ ]]; then
  echo '서버 jwt-signing.key 형식이 올바르지 않습니다. 기존 키를 확인하세요.' >&2
  exit 2
fi
export JWT_SIGNING_KEY

# /dev/null을 지정하여 checkout/호스트의 .env를 자동으로 읽지 않도록 합니다.
compose=(docker compose --project-name quroerp-dev --env-file /dev/null -f "$script_dir/compose.yaml")
# 확장된 설정에 비밀 값이 포함되므로 --quiet를 반드시 사용합니다.
"${compose[@]}" config --quiet
for target in api web migrate; do
  docker image inspect "$IMAGE_PREFIX-$target:$IMAGE_TAG" > /dev/null
done
# 외부 DB에 Schema/Seed만 적용합니다. DB 생성, 계정 변경, Demo 데이터는 실행하지 않습니다.
# 연결/마이그레이션 실패 시 set -e에 의해 API/Web 교체 전에 중단됩니다.
"${compose[@]}" run --rm --no-deps migrate
"${compose[@]}" up -d --wait --wait-timeout 240 api web

# up --wait가 API의 DB 연결 검사와 Web의 HTTP 로그인 페이지 검사를 기다립니다.
# 외부 DNS/공개 URL 검사는 수행하지 않으므로 DEV_URL은 필요하지 않습니다.

# 성공 기록에는 이미지 식별자와 비밀 값이 없는 매니페스트만 남깁니다.
release_dir="$deploy_dir/releases/$IMAGE_TAG"
mkdir -p "$release_dir"
cp "$script_dir/compose.yaml" "$release_dir/compose.yaml"
printf 'IMAGE_PREFIX=%s\nIMAGE_TAG=%s\n' "$IMAGE_PREFIX" "$IMAGE_TAG" > "$release_dir/images.env"
printf '%s\n' "$IMAGE_TAG" > "$deploy_dir/current-release"
printf 'Deployed %s\n' "$IMAGE_TAG"
