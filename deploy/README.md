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

## 2. 개발 웹 접속

**필수 Secret은 `DEV_DB_CONNECTION` 하나입니다.** 인증서와 `DEV_TLS_PASSWORD`는 사용하지 않습니다. `DEV_TLS_DIRECTORY`, `DEV_HTTPS_PORT`, `DEV_WEB_HOST`, `DEV_URL`도 필요 없습니다.

기본 접속 주소는 `http://<개발서버주소>`입니다. 호스트 포트 80을 컨테이너 내부 8080에 연결합니다. 포트를 바꾸려면 선택적 Variable `DEV_HTTP_PORT`만 등록하세요. DB 포트와는 별개이며, 개발 호스트 방화벽에서 TCP 80 접근을 허용해야 합니다. 호스트의 다른 서비스가 이미 80을 사용한다면 포트 충돌을 먼저 해결해야 합니다.

Web 컨테이너는 명시적인 `DevelopmentHttp` 환경으로 실행합니다. 이 환경에서만 HTTPS 리디렉션/HSTS를 끄고, HTTP 요청의 세션·자동 로그인 쿠키를 HTTP에서도 전달할 수 있게 합니다. HttpOnly, SameSite, 인증/권한, 위조 방지 검사는 유지합니다. 일반 `Production`과 `Development` 환경의 기존 HTTPS 기본 동작은 바꾸지 않습니다. `DevelopmentHttp`에서는 상세 개발 오류나 샘플 서비스를 켜지 않습니다.

HTTP 트래픽은 암호화되지 않으므로 이 구성은 신뢰할 수 있는 개발 네트워크에서 사용합니다. 운영 배포에는 기존 HTTPS 구성을 사용해야 합니다. 상태 검사는 컨테이너 내부의 API DB 연결과 Web HTTP 로그인 페이지를 확인하며 외부 접속/DNS까지 검증하지는 않습니다.

## 3. 러너와 자동 관리 파일

- 러너 라벨: `self-hosted`, `linux`, `quroerp-deploy`. 같은 라벨이 여러 서버에 있으면 어느 서버로든 배정될 수 있으므로 개발 호스트의 러너를 지정하세요.
- 필요한 도구: Docker Engine, Compose v2(`up --wait` 지원), Bash, curl, openssl. .NET SDK는 Docker 빌드 이미지에 있습니다.
- 러너 서비스 계정은 GitHub Actions 명령을 실행하는 Linux 사용자입니다. Docker 접근 권한과 해당 계정 홈 디렉터리의 쓰기 권한이 필요합니다. `/opt/quroerp/dev`를 만들거나 권한을 바꿀 필요는 없습니다.
- JWT 서명 키는 DB 비밀번호 해시가 아닙니다. 로그인 토큰 서명에 필요하며 최초 배포 때 러너 홈의 `.local/state/quroerp/dev/jwt-signing.key`에 자동 생성하고 권한 `600`으로 보관합니다. 직접 입력할 필요가 없습니다.
- 이후 배포에서도 같은 키를 사용합니다. 키 파일 손상 시 자동 재발급하지 않고 중단합니다. 기존 수동 키에서 전환하면 기존 로그인 토큰이 무효화될 수 있습니다.
- 성공한 릴리스의 매니페스트와 이미지 태그도 같은 러너 홈 아래 기록합니다. DB 비밀 값은 기록하지 않습니다. Actions가 배포 job을 직렬 실행하므로 별도의 호스트 잠금 파일은 필요하지 않습니다.

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
- 개발 Web의 ASP.NET 환경은 `DevelopmentHttp`, API는 `Production`입니다. 기본 HTTPS 정책의 예외는 개발 Web 컨테이너에만 적용합니다.
- Web 교체 시 Blazor 연결과 메모리 로그인 세션이 끊어집니다. 무중단 배포는 아닙니다.
- `set -x`, `printenv`, 비밀 값 출력, `--quiet` 없는 `docker compose config`를 사용하지 않습니다. Docker 관리자는 컨테이너 환경변수를 볼 수 있으므로 호스트 접근을 제한합니다.
- `bash deploy/tests/deploy-test.sh`로 Secret 누락 차단, 값 전달, 키 재사용, 실패 처리, 비밀 값 기록 방지를 검증합니다. 실제 접속은 설정된 개발 서버에서 확인합니다.
