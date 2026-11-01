#!/usr/bin/env bash
# Exercise orchestration failure boundaries without Docker, credentials or a server.
set -euo pipefail
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
test_root="$(mktemp -d)"
mkdir -p "$test_root/bin" "$test_root/server"
touch "$test_root/server/deploy.env"
export DEPLOY_DIRECTORY="$test_root/server"
export DEPLOY_URL=https://example.invalid
export IMAGE_PREFIX=quroerp
export IMAGE_TAG=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-1-1
export TEST_LOG="$test_root/commands"
export PATH="$test_root/bin:$PATH"

cat > "$test_root/bin/docker" <<'MOCK'
#!/usr/bin/env bash
printf '%s\n' "$*" >> "$TEST_LOG"
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
chmod +x "$test_root/bin/"*

expect_failure() {
  if bash "$script_dir/deploy.sh"; then
    echo 'Expected deployment failure.' >&2
    exit 1
  fi
  test ! -e "$test_root/server/current-release"
}

export FAIL_MIGRATION=1
expect_failure
! grep -q '240 api web\|public-https' "$TEST_LOG"
unset FAIL_MIGRATION
echo 'PASS: migration failure prevents application replacement'

: > "$TEST_LOG"
export FAIL_HEALTH=1
expect_failure
! grep -q public-https "$TEST_LOG"
unset FAIL_HEALTH
echo 'PASS: health failure prevents successful release recording'

: > "$TEST_LOG"
export HTTP_STATUS=302
expect_failure
unset HTTP_STATUS
echo 'PASS: public redirect does not count as healthy HTTPS'

: > "$TEST_LOG"
bash "$script_dir/deploy.sh"
test "$(cat "$test_root/server/current-release")" = "$IMAGE_TAG"
test -s "$test_root/server/releases/$IMAGE_TAG/compose.yaml"
test -s "$test_root/server/releases/$IMAGE_TAG/images.env"
test "$(tail -n 1 "$TEST_LOG")" = public-https
echo 'PASS: success records the deployed manifest and image identity'

# Remove only the files this test created; never recursively remove a computed path.
rm -f "$test_root/bin/docker" "$test_root/bin/curl" "$test_root/bin/flock" \
  "$test_root/commands" "$test_root/server/deploy.env" "$test_root/server/deploy.lock" \
  "$test_root/server/current-release" \
  "$test_root/server/releases/$IMAGE_TAG/compose.yaml" \
  "$test_root/server/releases/$IMAGE_TAG/images.env"
rmdir "$test_root/server/releases/$IMAGE_TAG" "$test_root/server/releases" \
  "$test_root/server" "$test_root/bin" "$test_root"
