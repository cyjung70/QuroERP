# 기존 Directory.Build.props / .targets에 추가할 줄

## Directory.Build.props (저장소 루트)
```xml
<Import Project="$(MSBuildThisFileDirectory)build/QuroGrid.props" />
```

## Directory.Build.targets (저장소 루트, 없으면 새로 만든다)
```xml
<Project>
  <Import Project="$(MSBuildThisFileDirectory)build/QuroGrid.targets" />
</Project>
```

## src/QuroErp.Web/QuroErp.Web.csproj · tests/QuroErp.Web.Tests/QuroErp.Web.Tests.csproj
```xml
<PropertyGroup>
  <UsesQuroGrid>true</UsesQuroGrid>
</PropertyGroup>
```

## .claude/settings.json (QuroERP)
`setup/claude-settings.json.example`의 `permissions` 블록을 병합한다.
Windows에서 `/WRK`가 C 드라이브라면 deny 경로는 `Edit(//c/WRK/QuroGrid/**)`, additionalDirectories는 `C:\\WRK\\QuroGrid`.

## CI
```bash
dotnet build -p:ContinuousIntegrationBuild=true   # QuroGridSource=local이면 빌드 실패
```
CI 러너에도 `/WRK/_nuget` 피드(또는 같은 패키지를 담은 사내 피드)가 있어야 한다.
