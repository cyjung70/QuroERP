# Deployment

The `Deploy` workflow runs after a successful `ci` **push to main**. PR runs cannot deploy. It checks out the tested commit, builds API/Web/migration images on the Linux runner labelled `quroerp-deploy`, then migrates and replaces the applications. No registry credentials or host .NET installation are needed. QuroGrid restores from nuget.org in package mode.

## Server setup

The runner needs Docker Engine with Linux containers, Compose v2 with `up --wait`, Bash, curl and flock. Its service user must have Docker access. Use a trusted deployment runner; do not send untrusted PR jobs to this host.

1. Create `/opt/quroerp/production`, owned by the runner user, with mode `0700`.
2. Copy `deploy/deploy.env.example` to `/opt/quroerp/production/deploy.env`, set mode `0600`, and fill all values. Generate independent database and JWT secrets with `openssl rand -hex 32`. `DB_CONNECTION` must contain the same password as `POSTGRES_PASSWORD`. Do not commit the populated file or print expanded Compose configuration.
3. Put a valid certificate and private key in `/opt/quroerp/production/tls/tls.pfx`; set `TLS_PASSWORD` to its export password. The container user (UID 1654) must be able to read the file and traverse the directory. Set `WEB_HOST` to the certificate hostname and point DNS to the server. The Web host terminates HTTPS directly; default host port is 443. API and PostgreSQL have no published ports.
4. Create the GitHub environment `production`. Set its variable `DEPLOY_URL` to the public HTTPS origin, including a nonstandard port if applicable. Optional `DEPLOY_DIRECTORY` overrides `/opt/quroerp/production`. Restrict deployment to `main`; required reviewers, if configured, pause automatic deployment.
5. Merge these files into `main`. A successful `ci` push run triggers deployment. The runner must reach the public URL and trust its certificate. To retry, rerun the failed deployment job in Actions.

This configuration provisions a new PostgreSQL 17 container with a persistent volume. An existing database or a reverse proxy needs an adjusted configuration before activation. Keep `deploy.env` and TLS files outside the checkout because Actions cleans its workspace.

## First installation and operations

Schema and Seed migrations run automatically, without Demo data. Company setup and administrator creation are explicit installation steps. After images have been built and migrations applied, use the deployment log's image tag:

```bash
export IMAGE_PREFIX=quroerp
export IMAGE_TAG=<commit-sha>-<run-id>-<attempt>
docker compose --project-name quroerp-production --env-file /opt/quroerp/production/deploy.env -f deploy/compose.yaml run --rm migrate --set-country KR --company-name 'Your company'
docker compose --project-name quroerp-production --env-file /opt/quroerp/production/deploy.env -f deploy/compose.yaml run --rm migrate --create-admin admin
```

Choose `KR` or `US` deliberately before recording transactions. The administrator command prompts for a password; run it in a server terminal, never put the password in Actions logs. Do not repeat it during routine deployments because it resets the account password.

Successful deployments record `compose.yaml` and `images.env` under `/opt/quroerp/production/releases/<tag>` and update `current-release`. Database and Web Data Protection keys persist in named volumes. Keep database backups separately. Never run `down --volumes` on this stack. Retain images needed for rollback.

A migration failure prevents application replacement. A health-check failure fails the workflow and can leave the new applications running. No automatic database rollback is attempted; application rollback requires checking schema compatibility and starting the saved release's API/Web images with `up -d --no-deps --wait api web`. Restarting Web disconnects Blazor sessions and loses in-memory login sessions; this is a single-server deployment, not zero downtime.

Validate shell orchestration with `bash deploy/tests/deploy-test.sh`. It covers migration failure, health failure, unexpected HTTP redirect, and successful release recording. Real container build/startup and database tests require the host runner or existing CI; mocked orchestration tests do not establish production readiness.

References: [GitHub runner labels](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/use-in-a-workflow), [Docker Compose startup order](https://docs.docker.com/compose/how-tos/startup-order/).

---
### 한국어 번역

# 배포

`Deploy` 워크플로는 **main push**의 `ci`가 성공하면 실행됩니다. PR 실행은 배포할 수 없습니다. 테스트한 커밋을 checkout하고 `quroerp-deploy` 라벨의 Linux 러너에서 API/Web/마이그레이션 이미지를 빌드한 뒤 마이그레이션과 애플리케이션 교체를 수행합니다. 레지스트리 인증정보나 호스트의 .NET 설치는 필요하지 않습니다. QuroGrid는 패키지 모드로 nuget.org에서 복원합니다.

## 서버 설정

러너에는 Linux 컨테이너를 사용하는 Docker Engine, `up --wait`를 지원하는 Compose v2, Bash, curl, flock이 필요합니다. 서비스 사용자는 Docker 접근 권한이 있어야 합니다. 신뢰할 수 있는 배포 러너를 사용하고 신뢰할 수 없는 PR 작업을 이 호스트로 보내지 않습니다.

1. 러너 사용자 소유로 `/opt/quroerp/production`을 만들고 권한을 `0700`으로 설정합니다.
2. `deploy/deploy.env.example`을 `/opt/quroerp/production/deploy.env`로 복사하고 권한을 `0600`으로 설정한 뒤 모든 값을 채웁니다. `openssl rand -hex 32`로 DB와 JWT에 사용할 독립적인 비밀 값을 생성합니다. `DB_CONNECTION`에는 `POSTGRES_PASSWORD`와 같은 비밀번호가 들어가야 합니다. 값을 채운 파일을 커밋하거나 확장된 Compose 설정을 출력하지 않습니다.
3. 유효한 인증서와 개인 키를 `/opt/quroerp/production/tls/tls.pfx`에 넣고 `TLS_PASSWORD`를 내보내기 비밀번호로 설정합니다. 컨테이너 사용자(UID 1654)가 파일을 읽고 디렉터리에 접근할 수 있어야 합니다. `WEB_HOST`를 인증서의 호스트 이름으로 설정하고 DNS가 서버를 가리키게 합니다. Web 호스트가 HTTPS를 직접 처리하며 기본 호스트 포트는 443입니다. API와 PostgreSQL은 포트를 공개하지 않습니다.
4. GitHub 환경 `production`을 만듭니다. 환경 변수 `DEPLOY_URL`에 공개 HTTPS origin을 설정하고 비표준 포트라면 포함합니다. 선택 사항인 `DEPLOY_DIRECTORY`로 `/opt/quroerp/production`을 변경할 수 있습니다. 배포를 `main`으로 제한합니다. 필수 검토자를 설정하면 자동 배포가 승인을 기다립니다.
5. 이 파일을 `main`에 병합합니다. `ci` push 실행이 성공하면 배포가 시작됩니다. 러너가 공개 URL에 접근하고 인증서를 신뢰해야 합니다. 재시도하려면 Actions에서 실패한 배포 job을 다시 실행합니다.

이 구성은 영속 볼륨을 사용하는 새 PostgreSQL 17 컨테이너를 만듭니다. 기존 DB나 리버스 프록시를 사용한다면 활성화 전에 구성을 조정해야 합니다. Actions가 작업 공간을 정리하므로 `deploy.env`와 TLS 파일은 checkout 외부에 보관합니다.

## 최초 설치 및 운영

Schema와 Seed 마이그레이션은 Demo 데이터 없이 자동 실행합니다. 회사 설정과 관리자 생성은 명시적인 설치 단계입니다. 이미지 빌드와 마이그레이션 적용 후 배포 로그의 이미지 태그를 사용합니다.

```bash
export IMAGE_PREFIX=quroerp
export IMAGE_TAG=<commit-sha>-<run-id>-<attempt>
docker compose --project-name quroerp-production --env-file /opt/quroerp/production/deploy.env -f deploy/compose.yaml run --rm migrate --set-country KR --company-name 'Your company'
docker compose --project-name quroerp-production --env-file /opt/quroerp/production/deploy.env -f deploy/compose.yaml run --rm migrate --create-admin admin
```

거래 기록 전에 `KR` 또는 `US`를 신중히 선택합니다. 관리자 명령은 비밀번호를 입력받습니다. 서버 터미널에서 실행하고 비밀번호를 Actions 로그에 넣지 않습니다. 계정 비밀번호를 초기화하므로 일반 배포 때 반복하지 않습니다.

배포가 성공하면 `/opt/quroerp/production/releases/<tag>`에 `compose.yaml`과 `images.env`를 기록하고 `current-release`를 갱신합니다. DB와 Web Data Protection 키는 명명된 볼륨에 영속화됩니다. DB 백업은 별도로 유지합니다. 이 스택에서 `down --volumes`를 실행하지 않습니다. 롤백에 필요한 이미지는 보존합니다.

마이그레이션 실패 시 애플리케이션을 교체하지 않습니다. 상태 검사 실패 시 워크플로가 실패하며 새 애플리케이션이 실행 중인 상태로 남을 수 있습니다. DB 자동 롤백은 하지 않습니다. 애플리케이션 롤백은 스키마 호환성을 확인하고 저장한 릴리스의 API/Web 이미지를 `up -d --no-deps --wait api web`으로 시작해야 합니다. Web 재시작은 Blazor 연결을 끊고 메모리 내 로그인 세션을 잃게 합니다. 단일 서버 배포이며 무중단 배포는 아닙니다.

`bash deploy/tests/deploy-test.sh`로 셸 실행 흐름을 검증합니다. 마이그레이션 실패, 상태 검사 실패, 예상하지 않은 HTTP 리디렉션, 성공한 릴리스 기록을 확인합니다. 실제 컨테이너 빌드/실행과 DB 테스트에는 호스트 러너 또는 기존 CI가 필요합니다. 모의 실행 흐름 테스트만으로 운영 준비가 검증되지는 않습니다.

참고: [GitHub 러너 라벨](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/use-in-a-workflow), [Docker Compose 시작 순서](https://docs.docker.com/compose/how-tos/startup-order/).
