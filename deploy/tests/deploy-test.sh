#!/usr/bin/env bash
# GitHub 환경변수 전달과 배포 실패 경계를 실제 서버/인증정보 없이 검증합니다.
set -euo pipefail
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
test_root="$(mktemp -d)"
mkdir -p "$test_root/bin" "$test_root/server"
test_script="$test_root/deploy.sh"
sed "s#^readonly deploy_dir=/opt/quroerp/dev\$#readonly deploy_dir='$test_root/server'#" "$script_dir/deploy.sh" > "$test_script"
cp "$script_dir/compose.yaml" "$test_root/compose.yaml"
grep -q "^readonly deploy_dir='$test_root/server'" "$test_script"

export IMAGE_PREFIX=quroerp-dev
export IMAGE_TAG=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-1-1
export TEST_LOG="$test_root/commands"
export PATH="$test_root/bin:$PATH"
# 외부 URL이나 deploy.env 없이 동작해야 합니다.
unset DEPLOY_URL
export DB_CONNECTION='test-db;Password=test-$-value'
export EXPECTED_DB_CONNECTION="$DB_CONNECTION"
export TLS_PASSWORD=test-tls TLS_DIRECTORY=/test-tls HTTPS_PORT=8443
unset WEB_HOST

cat > "$test_root/bin/docker" <<'MOCK'
#!/usr/bin/env bash
printf '%s\n' "$*" >> "$TEST_LOG"
[[ "${DB_CONNECTION:-}" == "$EXPECTED_DB_CONNECTION" ]] || exit 44
[[ "${TLS_PASSWORD:-}" == test-tls ]] || exit 45
[[ "${JWT_SIGNING_KEY:-}" == "$(printf '%064d' 0)" ]] || exit 46
if [[ "${FAIL_MIGRATION:-0}" == 1 && " $* " == *' run --rm --no-deps migrate '* ]]; then
  exit 42
fi
if [[ "${FAIL_HEALTH:-0}" == 1 && " $* " == *' up -d --wait --wait-timeout 240 api web '* ]]; then
  exit 43
fi
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

: > "$TEST_LOG"
unset DB_CONNECTION
expect_failure 1
test ! -s "$TEST_LOG"
export DB_CONNECTION="$EXPECTED_DB_CONNECTION"
echo 'PASS: missing DB secret stops deployment before any Docker operation'

export FAIL_MIGRATION=1
expect_failure 42
! grep -q '240 api web' "$TEST_LOG"
test "$(grep -c '^key-generation$' "$TEST_LOG")" = 1
unset FAIL_MIGRATION
echo 'PASS: migration failure prevents application replacement'

: > "$TEST_LOG"
export FAIL_HEALTH=1
expect_failure 43
unset FAIL_HEALTH
echo 'PASS: health failure prevents successful release recording'

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
test ! -e "$test_root/server/deploy.env"
grep -q -- '--env-file /dev/null' "$TEST_LOG"
! grep -q 'pull postgres\|120 postgres\|key-generation' "$TEST_LOG"
! grep -R -q 'test-db\|test-tls' "$test_root/server/releases"
! grep -R -q "$(printf '%064d' 0)" "$test_root/server/releases"
echo 'PASS: GitHub secrets reach Docker unchanged without DEV_URL or deploy.env'
echo 'PASS: generated JWT key is reused and secrets stay out of release records'

# 테스트가 만든 파일만 제거합니다.
rm -f "$test_root/bin/docker" "$test_root/bin/flock" "$test_root/bin/openssl" \
  "$test_script" "$test_root/compose.yaml" "$test_root/commands" \
  "$test_root/server/jwt-signing.key" "$test_root/server/deploy.lock" \
  "$test_root/server/current-release" \
  "$test_root/server/releases/$IMAGE_TAG/compose.yaml" \
  "$test_root/server/releases/$IMAGE_TAG/images.env"
rmdir "$test_root/server/releases/$IMAGE_TAG" "$test_root/server/releases" \
  "$test_root/server" "$test_root/bin" "$test_root"
