# Prompt: Configurar GitHub Actions CI/CD

## Objetivo

Criar pipeline completo de CI/CD com GitHub Actions para build, test, e publicação no NuGet.

## Contexto

O projeto precisa de:
- Build automático em PRs
- Testes unitários e de integração
- Publicação no NuGet em releases

## Requisitos

### Workflows Necessários

1. **CI (build + test)** - Em cada push/PR
2. **Release** - Publicação no NuGet em tags

### CI Workflow

```yaml
# .github/workflows/ci.yml
name: CI

on:
  push:
    branches: [master]
  pull_request:
    branches: [master]

env:
  DOTNET_VERSION: '9.0.x'
  DOTNET_NOLOGO: true
  DOTNET_CLI_TELEMETRY_OPTOUT: true

jobs:
  build:
    runs-on: ubuntu-latest

    steps:
    - uses: actions/checkout@v4

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: ${{ env.DOTNET_VERSION }}

    - name: Restore dependencies
      run: dotnet restore

    - name: Build
      run: dotnet build --no-restore --configuration Release

    - name: Run unit tests
      run: dotnet test tests/ZapJobs.Core.Tests tests/ZapJobs.Tests tests/ZapJobs.Storage.InMemory.Tests --no-build --configuration Release --verbosity normal --collect:"XPlat Code Coverage"

    - name: Upload coverage
      uses: codecov/codecov-action@v4
      with:
        files: '**/coverage.cobertura.xml'
        fail_ci_if_error: false

  integration-tests:
    runs-on: ubuntu-latest
    needs: build

    services:
      postgres:
        image: postgres:16-alpine
        env:
          POSTGRES_USER: test
          POSTGRES_PASSWORD: test
          POSTGRES_DB: zapjobs_test
        ports:
          - 5432:5432
        options: >-
          --health-cmd pg_isready
          --health-interval 10s
          --health-timeout 5s
          --health-retries 5

    steps:
    - uses: actions/checkout@v4

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: ${{ env.DOTNET_VERSION }}

    - name: Restore dependencies
      run: dotnet restore

    - name: Build
      run: dotnet build --no-restore --configuration Release

    - name: Run integration tests
      run: dotnet test tests/ZapJobs.IntegrationTests --no-build --configuration Release --verbosity normal
      env:
        POSTGRES_CONNECTION: "Host=localhost;Database=zapjobs_test;Username=test;Password=test"

  pack:
    runs-on: ubuntu-latest
    needs: [build, integration-tests]
    if: github.event_name == 'push' && github.ref == 'refs/heads/master'

    steps:
    - uses: actions/checkout@v4

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: ${{ env.DOTNET_VERSION }}

    - name: Pack
      run: dotnet pack --configuration Release --output ./artifacts

    - name: Upload artifacts
      uses: actions/upload-artifact@v4
      with:
        name: nuget-packages
        path: ./artifacts/*.nupkg
```

### Release Workflow

```yaml
# .github/workflows/release.yml
name: Release

on:
  push:
    tags:
      - 'v*'

env:
  DOTNET_VERSION: '9.0.x'
  DOTNET_NOLOGO: true

jobs:
  release:
    runs-on: ubuntu-latest

    steps:
    - uses: actions/checkout@v4

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: ${{ env.DOTNET_VERSION }}

    - name: Get version from tag
      id: version
      run: echo "VERSION=${GITHUB_REF#refs/tags/v}" >> $GITHUB_OUTPUT

    - name: Restore dependencies
      run: dotnet restore

    - name: Build
      run: dotnet build --no-restore --configuration Release /p:Version=${{ steps.version.outputs.VERSION }}

    - name: Test
      run: dotnet test --no-build --configuration Release --verbosity normal

    - name: Pack
      run: dotnet pack --no-build --configuration Release --output ./artifacts /p:Version=${{ steps.version.outputs.VERSION }}

    - name: Push to NuGet
      run: |
        dotnet nuget push "./artifacts/*.nupkg" \
          --api-key ${{ secrets.NUGET_API_KEY }} \
          --source https://api.nuget.org/v3/index.json \
          --skip-duplicate

    - name: Create GitHub Release
      uses: softprops/action-gh-release@v1
      with:
        files: ./artifacts/*.nupkg
        generate_release_notes: true
      env:
        GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

### Dependabot

```yaml
# .github/dependabot.yml
version: 2
updates:
  - package-ecosystem: "nuget"
    directory: "/"
    schedule:
      interval: "weekly"
    open-pull-requests-limit: 5

  - package-ecosystem: "github-actions"
    directory: "/"
    schedule:
      interval: "weekly"
```

## Arquivos a Criar

```
.github/
├── workflows/
│   ├── ci.yml
│   └── release.yml
├── dependabot.yml
└── CODEOWNERS          # opcional
```

## Secrets Necessários

Configurar no repositório:
- `NUGET_API_KEY` - API key do NuGet.org

## Processo de Release

```bash
# Criar tag e push
git tag v1.0.0
git push origin v1.0.0

# Ou criar release no GitHub UI
```

## Critérios de Aceitação

1. [ ] CI roda em cada push/PR
2. [ ] Testes unitários e integração passam
3. [ ] Coverage report no Codecov
4. [ ] Pack gera artifacts corretamente
5. [ ] Release publica no NuGet automaticamente
6. [ ] GitHub Release criado com binários
7. [ ] Dependabot atualiza dependências
