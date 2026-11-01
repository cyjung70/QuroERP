# 개발 서버 자동 배포

`dev` push → GitHub 호스팅 Ubuntu에서 기존 CI 실행 → 성공하면 `quroerp-deploy` 자체 러너에서 Docker 빌드 → 외부 개발 DB 마이그레이션 → API/Web 교체 → HTTPS 검사 순서입니다. PR에서는 배포하지 않습니다.

`main`을 되돌리거나 수정하지 않고 그 상태에서 `dev`를 만들었습니다. 이 브랜치의 `.github/workflows/deploy.yml`은 `push: dev`를 사용하므로 변경을 main에 다시 반영하지 않아도 실행됩니다. 기존 CI는 `workflow_call`로 재사용합니다. CI의 PostgreSQL은 일회용 테스트 DB이며 실제 개발 DB 인증정보를 받지 않습니다.

## 1. 러너 설정

- 라벨: `self-hosted`, `linux`, `quroerp-deploy`를 모두 가진 러너를 선택합니다.
- 러너 서비스 계정에 Docker Engine, Compose v2(`up --wait` 지원), Bash, curl, flock, openssl 실행 권한이 필요합니다.
- .NET SDK는 Docker 빌드 이미지 안에서 사용하므로 호스트에 별도 설치하지 않습니다. QuroGrid는 nuget.org 패키지로 복원합니다.
- GitHub/컨테이너 이미지 저장소/NuGet로 나가는 연결, 컨테이너에서 외부 DB로 나가는 연결이 가능해야 합니다. DB 방화벽/접근 정책도 개발 호스트를 허용해야 합니다.
- 동일 라벨의 러너가 여러 호스트에 있으면 어느 호스트에서든 배포될 수 있습니다. 개발 서버에 붙은 러너에 이 라벨을 지정하세요.
- 운영 워크플로가 같은 라벨을 계속 사용하면 같은 호스트에 배정될 수 있습니다. 개발 컨테이너·이미지·볼륨·설정 경로·배포 잠금은 별도로 구분했습니다.

### Docker 소켓 권한 오류

`permission denied ... /var/run/docker.sock`는 GitHub 토큰 권한이 아니라 러너 서비스 계정의 Docker 접근 권한 문제입니다. systemd 서비스로 설치한 러너는 호스트 터미널에서 다음 순서로 확인합니다. `cd` 경로는 실제 러너 설치 위치로 바꿉니다.

```bash
cd /path/to/actions-runner
service_name=$(cat .service)
runner_user=$(systemctl show "$service_name" -p User --value)
printf '러너 서비스: %s / 계정: %s\n' "$service_name" "$runner_user"
```

출력된 계정이 실제 러너 서비스 계정인지 확인한 뒤 실행합니다. 계정이 비어 있으면 진행하지 말고 서비스 설치 상태를 먼저 확인합니다.

```bash
test -n "$runner_user" && sudo usermod -aG docker "$runner_user"
sudo systemctl restart "$service_name"
sudo -u "$runner_user" -H docker info
```

로그인한 셸에만 `newgrp docker`를 실행해도 이미 실행 중인 러너 서비스에는 적용되지 않으므로 서비스 재시작이 필요합니다. `run.sh`로 직접 실행 중인 러너는 해당 계정의 로그인 세션을 새로 시작한 뒤 러너를 재실행합니다. 소켓에 `chmod 666`을 주지 않습니다. Docker 그룹은 호스트 root 수준의 권한을 제공하므로 신뢰할 러너 계정에만 부여합니다. [Docker 공식 안내](https://docs.docker.com/engine/install/linux-postinstall/)

## 2. GitHub dev 환경


저장소 **Settings → Environments → New environment → dev**에서 설정합니다. Deployment branches는 `dev`만 허용하세요. Required reviewers를 추가하면 승인을 기다리고, 없으면 검증 성공 후 자동 배포합니다.

GitHub에는 비밀 값 대신 **Variable `DEV_URL`만** 설정합니다. 값은 실제 개발 웹 HTTPS 주소입니다. 예: `https://erp-dev.example.com:8443`. DB 주소가 아닙니다.

DB·TLS 설정은 고정 파일 `/opt/quroerp/dev/deploy.env`에서 읽습니다. JWT 서명 키는 `/opt/quroerp/dev/jwt-signing.key`에 자동 생성합니다. `DEV_DB_CONNECTION`, `DEV_JWT_SIGNING_KEY`, `DEV_TLS_PASSWORD` Secrets와 `DEV_DEPLOY_DIRECTORY` Variable은 사용하지 않습니다. 기존 항목이 있어도 이 워크플로에 전달되지 않으며 삭제해도 됩니다.

## 3. 개발 호스트 설정 파일

러너 계정으로 다음을 실행합니다. `deploy.env`와 인증서는 checkout 외부에 보관합니다. Actions checkout 정리 대상이 아니어야 합니다.

**러너 서비스 계정**은 GitHub Actions 명령을 실행하는 Linux 사용자입니다. 파일 소유자가 이 사용자이고 권한이 `600`이면 러너가 파일을 읽고 쓸 수 있습니다. `ls -l /opt/quroerp/dev/deploy.env`로 소유자를 확인합니다. `systemctl show "$(cat /path/to/actions-runner/.service)" -p User --value`로 실제 서비스 계정을 확인할 수 있습니다. 접속한 계정과 다르면 확인한 계정으로 `sudo chown <러너계정> /opt/quroerp/dev /opt/quroerp/dev/deploy.env`를 실행합니다. 디렉터리에도 쓰기 권한이 있어야 자동 키·잠금·릴리스 기록을 만들 수 있습니다.

```bash
install -d -m 700 /opt/quroerp/dev
install -m 600 deploy/deploy.env.example /opt/quroerp/dev/deploy.env
```

이미 설정 파일이 있으면 위 `install` 명령으로 덮어쓰지 말고 기존 파일을 편집합니다.

| 서버 파일 항목 | 설정 방법 |
| --- | --- |
| `DB_CONNECTION` | 필수. `Host=<DB_HOST>;Port=<DB_PORT>;Database=<DB_NAME>;Username=<DB_USER>;Password=<DB_PASSWORD>` 형식의 전체 연결 문자열. DB 이름은 계정명과 별개입니다. |
| `TLS_PASSWORD` | 필수. `tls.pfx`를 내보낼 때 지정한 비밀번호. |
| `TLS_DIRECTORY` | `tls.pfx`를 보관할 절대 경로. 기본 `/opt/quroerp/dev/tls`. |
| `WEB_HOST` | 실제 개발 웹 도메인. 인증서 이름과 일치해야 합니다. 프로토콜·포트·경로를 붙이지 않습니다. |
| `HTTPS_PORT` | 호스트에서 공개할 웹 포트. 기본 `8443`. `DEV_URL`에도 동일한 포트를 사용합니다. |

**배포 스크립트는 설정 경로를 변경할 수 없으며, 위 다섯 항목과 JWT의 상속 환경변수를 제거한 후 서버 설정을 읽습니다.** 파일을 `source`하거나 `eval`하지 않고 Compose의 dotenv 형식으로 읽습니다. 필수 값이 없으면 배포를 중단합니다.

`JWT_SIGNING_KEY`는 직접 입력하지 않습니다. DB 비밀번호 해시가 아니라 로그인 토큰 서명 키이며, 현재 API 시작에 필요합니다. 최초 배포 때 OpenSSL로 생성하여 `/opt/quroerp/dev/jwt-signing.key`에 권한 `600`으로 보관하고 이후 그대로 재사용합니다. 이 파일은 삭제하거나 Git에 커밋하지 않습니다. 파일이 손상되면 자동 재발급하지 않고 배포를 중단합니다. 기존에 수동 JWT 키로 실행한 서버는 자동 키로 전환할 때 기존 로그인 토큰이 무효화될 수 있습니다.

값에 `$` 등 Compose 확장 문자가 있으면 dotenv의 작은따옴표 인용을 사용하세요. 완성된 연결 문자열 전체를 작은따옴표로 감쌀 수 있습니다. 파일은 러너 소유 및 권한 `0600`으로 유지합니다. Docker 관리 권한이 있는 사용자는 컨테이너 환경변수를 볼 수 있으므로 호스트 권한을 제한합니다. DB의 TLS 정책에 맞춰 필요한 경우 `SSL Mode=VerifyFull`과 신뢰할 CA 구성을 추가합니다.

유효한 인증서와 개인 키를 `TLS_DIRECTORY/tls.pfx`에 넣고 컨테이너 사용자 UID 1654에 필요한 읽기/경로 접근 권한을 부여합니다. 상위 디렉터리의 `0700` 권한까지 고려해 필요한 ACL/소유권을 설정하세요. 개발 웹 DNS와 방화벽에서 `HTTPS_PORT`를 열고 러너에서도 `DEV_URL`에 접속할 수 있게 합니다. 사설 CA면 러너에 CA 신뢰를 설정합니다.

## 4. 외부 DB와 최초 설치

새 PostgreSQL 컨테이너와 DB 볼륨은 만들지 않습니다. 개발 전용 DB와 계정 권한을 먼저 준비하세요. 마이그레이션에는 스키마/테이블/시퀀스 생성 및 변경 권한이 필요합니다. 운영 DB 연결 문자열을 등록하면 안 됩니다.

배포는 Schema → Seed만 적용하며 Demo 데이터, DB/사용자 생성, 회사 국가 설정, 관리자 생성/비밀번호 초기화는 자동 실행하지 않습니다. 최초 회사·관리자 설정은 기존 Database 콘솔의 `--set-country`, `--create-admin`으로 수행합니다. 업무 데이터가 있는 DB는 마이그레이션 전에 백업을 확보하세요.

최초 회사·관리자 설정도 같은 서버 파일을 사용합니다. 실제 비밀번호를 명령줄에 넣지 않습니다. 먼저 상속된 설정을 제거하고 배포한 이미지 태그를 지정합니다.

```bash
unset DB_CONNECTION JWT_SIGNING_KEY TLS_PASSWORD TLS_DIRECTORY WEB_HOST HTTPS_PORT
export JWT_SIGNING_KEY="$(cat /opt/quroerp/dev/jwt-signing.key)"
export IMAGE_PREFIX=quroerp-dev
export IMAGE_TAG='<commit-sha>-<run-id>-<attempt>'
docker compose --project-name quroerp-dev --env-file /opt/quroerp/dev/deploy.env -f deploy/compose.yaml run --rm migrate --set-country KR --company-name '회사명'
docker compose --project-name quroerp-dev --env-file /opt/quroerp/dev/deploy.env -f deploy/compose.yaml run --rm migrate --create-admin admin
unset JWT_SIGNING_KEY
```

회사 국가는 실제 업무에 맞게 KR/US를 선택합니다. 관리자 명령은 비밀번호를 대화형으로 받고 기존 계정이면 비밀번호를 초기화하므로 일반 배포에서 반복하지 않습니다.

## 5. 배포·실패·검증

- 이미지 이름: `quroerp-dev-api`, `quroerp-dev-web`, `quroerp-dev-migrate`. 태그: 커밋 SHA + 실행 ID + 재시도 번호. 서버에서 빌드하므로 레지스트리 인증은 필요 없습니다.
- Compose 프로젝트: `quroerp-dev`. Web 키 볼륨: `quroerp-dev_web-keys`. API 호스트 포트와 DB 컨테이너 포트는 공개하지 않습니다.
- ASP.NET 환경은 개발 서버에서도 `Production`입니다. 상세 오류와 샘플 화면을 공개하지 않기 위한 설정이며 Git 브랜치 dev와는 별개입니다.
- 호스트 `deploy.lock`으로 수동/자동 배포를 직렬화합니다. 마이그레이션 실패 시 API/Web 교체를 중단합니다.
- 상태 검사 실패 시 워크플로는 실패하지만 새 애플리케이션이 실행 중일 수 있습니다. 자동 DB 롤백은 없으며 main 이력도 되돌리지 않습니다.
- 성공한 매니페스트와 이미지 태그만 `releases/<tag>`와 `current-release`에 기록합니다. 비밀 값은 기록하지 않습니다.
- Web 교체 시 Blazor 연결 및 메모리 로그인 세션은 끊어집니다. 단일 서버 배포이며 무중단 배포는 아닙니다.
- `set -x`, `printenv`, 비밀 값 `echo`, `--quiet` 없는 `docker compose config`를 사용하지 않습니다. 볼륨 삭제나 전역 Docker 정리도 자동 실행하지 않습니다.
- `bash deploy/tests/deploy-test.sh`는 고정 경로, 상속 환경변수 차단, 외부 DB 흐름, 실패 시 교체 중단, 성공 기록을 검증합니다. 실제 외부 DB 접속과 컨테이너 실행은 설정을 완료한 개발 러너에서 확인해야 합니다.

참고: [GitHub 재사용 워크플로](https://docs.github.com/en/actions/how-tos/reuse-automations/reuse-workflows), [GitHub Secrets](https://docs.github.com/en/actions/how-tos/write-workflows/choose-what-workflows-do/use-secrets).
