# 개발 서버 자동 배포

`dev` push → GitHub 호스팅 Ubuntu에서 CI → 성공 시 `quroerp-deploy` 자체 러너에서 Docker 빌드 → 외부 개발 DB 마이그레이션 → API/Web 교체 순서입니다. PR에서는 배포하지 않습니다. 기존 CI는 `workflow_call`로 재사용하며 CI의 일회용 PostgreSQL에는 실제 개발 DB 인증정보를 전달하지 않습니다.

## 1. DB 설정: GitHub Secret 하나

GitHub 저장소 **Settings → Environments → dev → Environment secrets**에 다음 항목을 추가합니다. 저장소의 **Settings → Secrets and variables → Actions → Secrets**에 등록해도 사용할 수 있으며, 같은 이름이 있으면 dev 환경 값이 우선합니다.

| 종류 | 이름 | 값 |
| --- | --- | --- |
| Secret | `DEV_DB_CONNECTION` | 외부 개발 PostgreSQL의 전체 연결 문자열 |

```text
Host=<DB_HOST>;Port=<DB_PORT>;Database=<DB_NAME>;Username=<DB_USER>;Password=<DB_PASSWORD>
```

실제 비밀번호는 Secret 값으로만 입력하고 Variables·YAML·커밋·로그에는 쓰지 않습니다. DB 이름은 계정명과 별개이며 기존 개발 전용 DB를 지정해야 합니다. DB 계정에는 마이그레이션에 필요한 스키마/테이블 생성·변경 권한이 필요합니다. DB 서버의 TLS 정책에 맞는 연결 옵션도 이 Secret에 포함합니다.

주입 순서는 **Secret DEV_DB_CONNECTION → 배포 단계 환경변수 DB_CONNECTION → Compose → API/마이그레이션의 ConnectionStrings__QuroErp**입니다. 비밀 값은 이미지 빌드에 전달하지 않습니다. 값에 포함된 달러 기호 등도 셸 코드로 실행하지 않고 환경변수로 그대로 전달합니다.

**`DEV_URL`과 호스트 `deploy.env`는 더 이상 사용하지 않습니다.** `/opt/quroerp/dev/deploy.env`를 만들 필요가 없으며, 기존 파일이 있어도 읽지 않습니다. `DEV_DEPLOY_DIRECTORY`도 사용하지 않습니다.

## 2. 기존 Web HTTPS 설정

DB 연결과 웹 HTTPS는 별개입니다. 기존 Web은 HTTPS 로그인 쿠키를 사용하므로 인증서 설정은 유지합니다. 이번 변경은 HTTP로 전환하거나 인증을 해제하지 않습니다.

| 종류 | 이름 | 값과 기본값 |
| --- | --- | --- |
| Secret | `DEV_TLS_PASSWORD` | 서버 `tls.pfx`의 내보내기 비밀번호. 필수입니다. |
| Variable | `DEV_TLS_DIRECTORY` | 인증서 디렉터리. 생략하면 `/opt/quroerp/dev/tls`. |
| Variable | `DEV_HTTPS_PORT` | 개발 웹의 호스트 포트. 생략하면 `8443`. DB 포트가 아닙니다. |
| Variable | `DEV_WEB_HOST` | 선택 사항. 지정 시 인증서와 일치하는 웹 호스트 이름. 생략 시 개발 Web은 모든 Host 헤더를 허용합니다. |

위 항목은 `dev` 환경 또는 저장소의 Secrets/Variables에 등록합니다. 유효한 인증서와 개인 키를 `DEV_TLS_DIRECTORY/tls.pfx`에 준비하고, 컨테이너 UID 1654에 필요한 읽기 및 경로 접근 권한을 부여합니다. 인증서는 이미지나 저장소에 넣지 않습니다. 외부 접속을 위해 개발 웹 포트의 방화벽 설정은 필요합니다.

상태 검사는 컨테이너 내부의 API DB 연결과 Web HTTPS 로그인 페이지를 확인합니다. 공개 URL, DNS, 외부 인증서 신뢰 상태는 검사하지 않으므로 외부 접속까지 검증했다고 간주하지 않습니다.

## 3. 러너와 자동 관리 파일

- 러너 라벨: `self-hosted`, `linux`, `quroerp-deploy`. 같은 라벨이 여러 서버에 있으면 어느 서버로든 배정될 수 있으므로 개발 호스트의 러너를 지정하세요.
- 필요한 도구: Docker Engine, Compose v2(`up --wait` 지원), Bash, curl, flock, openssl. .NET SDK는 Docker 빌드 이미지에 있습니다.
- 러너 서비스 계정은 GitHub Actions 명령을 실행하는 Linux 사용자입니다. Docker 접근 권한과 `/opt/quroerp/dev` 쓰기 권한이 필요합니다.
- JWT 서명 키는 DB 비밀번호 해시가 아닙니다. 로그인 토큰 서명에 필요하며 최초 배포 때 `/opt/quroerp/dev/jwt-signing.key`에 자동 생성하고 권한 `600`으로 보관합니다. 직접 입력할 필요가 없습니다.
- 이후 배포에서도 같은 키를 사용합니다. 키 파일 손상 시 자동 재발급하지 않고 중단합니다. 기존 수동 키에서 전환하면 기존 로그인 토큰이 무효화될 수 있습니다.
- 설정 파일 대신 호스트에 남는 것은 JWT 키, 배포 잠금, 성공한 릴리스의 매니페스트/이미지 태그입니다. 릴리스 기록에는 DB/TLS 비밀 값을 넣지 않습니다.

Docker 소켓 권한 오류는 GitHub 토큰 권한과 별개입니다. 호스트의 실제 러너 설치 디렉터리에서 확인합니다.

```bash
cd /path/to/actions-runner
service_name=$(cat .service)
runner_user=$(systemctl show "$service_name" -p User --value)
printf '러너 계정: %s\n' "$runner_user"
```

계정이 올바른지 확인한 후 다음을 실행합니다. 계정이 비어 있으면 먼저 서비스 설치 상태를 확인하세요.

```bash
test -n "$runner_user" && sudo usermod -aG docker "$runner_user"
sudo systemctl restart "$service_name"
sudo -u "$runner_user" -H docker info
```

그룹 변경은 이미 실행 중인 러너 서비스에 적용되지 않으므로 재시작이 필요합니다. 소켓에 `chmod 666`을 주지 않습니다. Docker 그룹은 호스트 관리 권한이므로 신뢰할 러너 계정에만 부여합니다.

## 4. 배포와 최초 설치

- 이미지: `quroerp-dev-api`, `quroerp-dev-web`, `quroerp-dev-migrate`. 태그는 커밋 SHA·실행 ID·재시도 번호입니다. 서버에 직접 빌드하므로 이미지 레지스트리 인증은 필요 없습니다.
- Compose 프로젝트: `quroerp-dev`. API는 내부 HTTP 8080으로만 통신하며 호스트 포트를 공개하지 않습니다. Web 키는 별도 영속 볼륨에 보관합니다.
- 실제 DB 컨테이너를 만들지 않습니다. 외부 개발 DB에 Schema/Seed만 적용합니다. DB·계정 생성, Demo 데이터, 회사 국가 및 관리자 초기 설정은 자동 실행하지 않습니다.
- 최초 회사 설정과 관리자 생성은 Database 콘솔의 `--set-country`, `--create-admin`으로 별도 실행합니다. 수동 Compose 실행 시 GitHub Secrets가 터미널에 자동 주입되지 않으므로 필요한 환경변수를 별도로 안전하게 설정해야 합니다.
- 마이그레이션 실패 시 API/Web 교체 전에 중단합니다. `up --wait` 상태 검사 실패 시 배포는 실패하지만 새 컨테이너가 실행 중일 수 있습니다. DB 자동 롤백은 없습니다.
- 개발 서버에서도 ASP.NET 환경은 `Production`입니다. 상세 오류와 샘플 화면을 공개하지 않으며 Git 브랜치 이름과는 별개입니다.
- Web 교체 시 Blazor 연결과 메모리 로그인 세션이 끊어집니다. 무중단 배포는 아닙니다.
- `set -x`, `printenv`, 비밀 값 출력, `--quiet` 없는 `docker compose config`를 사용하지 않습니다. Docker 관리자는 컨테이너 환경변수를 볼 수 있으므로 호스트 접근을 제한합니다.
- `bash deploy/tests/deploy-test.sh`로 Secret 누락 차단, 값 전달, 키 재사용, 실패 처리, 비밀 값 기록 방지를 검증합니다. 실제 접속은 설정된 개발 서버에서 확인합니다.
