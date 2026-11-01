#!/usr/bin/env bash
# Docker/실제 인증정보/서버 없이 배포 실패 경계와 비밀 값 주입 방식을 검증합니다.
set -euo pipefail
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
test_root="$(mktemp -d)"
mkdir -p "$test_root/bin" "$test_root/server"
touch "$test_root/server/deploy.env"
# 운영 스크립트의 고정 경로 선언만 테스트용 복사본에서 임시 경로로 바꿉니다.
# 실제 /opt 파일은 읽거나 수정하지 않습니다.
test_script="$test_root/deploy.sh"
sed "s#^readonly deploy_dir=/opt/quroerp/dev\$#readonly deploy_dir='$test_root/server'#" "$script_dir/deploy.sh" > "$test_script"
cp "$script_dir/compose.yaml" "$test_root/compose.yaml"
grep -q "^readonly deploy_dir='$test_root/server'" "$test_script"
# 이전 경로 변수와 비밀 환경변수가 남아 있어도 사용되지 않아야 합니다.
export DEPLOY_DIRECTORY=/ignored-path
export DEPLOY_URL=https://example.invalid
export IMAGE_PREFIX=quroerp-dev
export IMAGE_TAG=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-1-1
export TEST_LOG="$test_root/commands"
export PATH="$test_root/bin:$PATH"

cat > "$test_root/bin/docker" <<'MOCK'
#!/usr/bin/env bash
printf '%s\n' "$*" >> "$TEST_LOG"
[[ ! -v DB_CONNECTION && ! -v TLS_PASSWORD ]] || exit 44
[[ "${JWT_SIGNING_KEY:-}" == "$(printf '%064d' 0)" ]] || exit 46
[[ ! -v TLS_DIRECTORY && ! -v WEB_HOST && ! -v HTTPS_PORT ]] || exit 45
if [[ "${FAIL_MIGRATION:-0}" == 1 && " $* " == *' run --rm --no-deps migrate '* ]]; then
  exit 42
fi
if [[ "${FAIL_HEALTH:-0}" == 1 && " $* " == *' up -d --wait --wait-timeout 240 api web '* ]]; then
  exit 43
fi
MOCK
cat > "$test_root/bin/curl" <<'MOCK'
#!/usr/bin/env bash
printf 'public-https\n' >> "$TEST_LOG"
printf '%s' "${HTTP_STATUS:-200}"
MOCK
cat > "$test_root/bin/flock" <<'MOCK'
#!/usr/bin/env bash
exit 0
MOCK
cat > "$test_root/bin/openssl" <<'MOCK'
#!/usr/bin/env bash
printf 'key-generation\n' >> "$TEST_LOG"
printf '%064d\n' 0
MOCK
chmod +x "$test_root/bin/"*

expect_failure() {
  if bash "$test_script"; then
    echo 'Expected deployment failure.' >&2
    exit 1
  else
    test "$?" -eq "$1"
  fi
  test ! -e "$test_root/server/current-release"
}

export FAIL_MIGRATION=1
# 빈 환경변수도 서버 파일을 가리지 않도록 제거해야 합니다.
export DB_CONNECTION='' JWT_SIGNING_KEY='' TLS_PASSWORD=''
expect_failure 42
! grep -q '240 api web\|public-https' "$TEST_LOG"
unset FAIL_MIGRATION
echo 'PASS: migration failure prevents application replacement'
echo 'PASS: fixed server path ignores path overrides and empty environment values'
test "$(grep -c '^key-generation$' "$TEST_LOG")" = 1

# 이전 설정에서 남은 비어 있지 않은 환경변수도 제거해야 합니다.
export DB_CONNECTION=test-db JWT_SIGNING_KEY=test-jwt TLS_PASSWORD=test-tls
export TLS_DIRECTORY=/ignored-tls WEB_HOST=ignored.invalid HTTPS_PORT=9999

: > "$TEST_LOG"
export FAIL_HEALTH=1
expect_failure 43
! grep -q public-https "$TEST_LOG"
unset FAIL_HEALTH
echo 'PASS: health failure prevents successful release recording'

: > "$TEST_LOG"
export HTTP_STATUS=302
expect_failure 1
unset HTTP_STATUS
echo 'PASS: public redirect does not count as healthy HTTPS'

# 기존 키가 손상되었으면 조용히 재발급하지 않고 배포를 중단합니다.
printf 'invalid-key\n' > "$test_root/server/jwt-signing.key"
: > "$TEST_LOG"
expect_failure 2
test ! -s "$TEST_LOG"
printf '%064d\n' 0 > "$test_root/server/jwt-signing.key"
echo 'PASS: malformed existing key stops deployment without rotating it'

: > "$TEST_LOG"
bash "$test_script"
test "$(cat "$test_root/server/current-release")" = "$IMAGE_TAG"
test -s "$test_root/server/releases/$IMAGE_TAG/compose.yaml"
test -s "$test_root/server/releases/$IMAGE_TAG/images.env"
test "$(tail -n 1 "$TEST_LOG")" = public-https
echo 'PASS: success records the deployed manifest and image identity'
! grep -q 'pull postgres\|120 postgres' "$TEST_LOG"
! grep -R -q 'test-db\|test-jwt\|test-tls' "$test_root/server/releases"
echo 'PASS: external database only; no secrets in release records'
echo 'PASS: inherited credentials and web settings cannot override the host file'
! grep -q '^key-generation$' "$TEST_LOG"
! grep -R -q "$(printf '%064d' 0)" "$test_root/server/releases"
echo 'PASS: generated JWT key is reused and excluded from release records'

# 이 테스트가 만든 파일만 제거하며 계산된 경로를 재귀 삭제하지 않습니다.
rm -f "$test_root/bin/docker" "$test_root/bin/curl" "$test_root/bin/flock" \
  "$test_root/bin/openssl" "$test_root/server/jwt-signing.key" \
  "$test_script" "$test_root/compose.yaml" \
  "$test_root/commands" "$test_root/server/deploy.env" "$test_root/server/deploy.lock" \
  "$test_root/server/current-release" \
  "$test_root/server/releases/$IMAGE_TAG/compose.yaml" \
  "$test_root/server/releases/$IMAGE_TAG/images.env"
rmdir "$test_root/server/releases/$IMAGE_TAG" "$test_root/server/releases" \
  "$test_root/server" "$test_root/bin" "$test_root"
