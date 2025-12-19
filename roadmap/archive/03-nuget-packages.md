# Prompt: Configurar Publicação NuGet

## Objetivo

Configurar os 6 projetos do ZapJobs para publicação no NuGet.org com versionamento semântico, README por pacote e configuração adequada de metadados.

## Contexto

O ZapJobs possui 6 projetos que devem ser publicados como pacotes NuGet:
1. `ZapJobs.Core` - Abstrações e entidades
2. `ZapJobs` - Implementação principal
3. `ZapJobs.Storage.InMemory` - Storage em memória
4. `ZapJobs.Storage.PostgreSQL` - Storage PostgreSQL
5. `ZapJobs.AspNetCore` - Dashboard web
6. `ZapJobs.Cli` - CLI tool

## Requisitos

### Estrutura de Metadados
Adicionar em cada `.csproj`:

```xml
<PropertyGroup>
  <PackageId>ZapJobs.Core</PackageId>
  <Version>1.0.0</Version>
  <Authors>Your Name</Authors>
  <Company>Your Company</Company>
  <Product>ZapJobs</Product>
  <Description>Database-driven job scheduler for .NET - Core abstractions</Description>
  <PackageTags>jobs;scheduler;background;tasks;cron;hangfire-alternative</PackageTags>
  <PackageProjectUrl>https://github.com/yourusername/zapjobs</PackageProjectUrl>
  <RepositoryUrl>https://github.com/yourusername/zapjobs</RepositoryUrl>
  <RepositoryType>git</RepositoryType>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <PackageReadmeFile>README.md</PackageReadmeFile>
  <PackageIcon>icon.png</PackageIcon>

  <!-- Source Link -->
  <PublishRepositoryUrl>true</PublishRepositoryUrl>
  <EmbedUntrackedSources>true</EmbedUntrackedSources>
  <IncludeSymbols>true</IncludeSymbols>
  <SymbolPackageFormat>snupkg</SymbolPackageFormat>

  <!-- XML Documentation -->
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
  <NoWarn>$(NoWarn);CS1591</NoWarn>
</PropertyGroup>

<ItemGroup>
  <None Include="README.md" Pack="true" PackagePath="\" />
  <None Include="../../icon.png" Pack="true" PackagePath="\" />
</ItemGroup>

<ItemGroup>
  <PackageReference Include="Microsoft.SourceLink.GitHub" Version="8.0.0" PrivateAssets="All" />
</ItemGroup>
```

### README por Pacote

Criar README.md específico para cada pacote:

#### ZapJobs.Core/README.md
```markdown
# ZapJobs.Core

Core abstractions and entities for ZapJobs - the database-driven job scheduler for .NET.

## Installation

```bash
dotnet add package ZapJobs.Core
```

## This Package Contains

- `IJob`, `IJob<T>`, `IJob<TIn, TOut>` - Job interfaces
- `IJobScheduler` - Scheduling API
- `IJobStorage` - Storage abstraction
- `IJobTracker` - Monitoring API
- `JobDefinition`, `JobRun`, `JobLog` - Entities
- `ZapJobsOptions`, `RetryPolicy` - Configuration

## Usage

This is a core package. For a complete solution, install:
- `ZapJobs` - Main implementation
- `ZapJobs.Storage.PostgreSQL` - PostgreSQL storage
```

#### ZapJobs.Cli/README.md (Tool)
```markdown
# ZapJobs CLI

Command-line tool for ZapJobs management.

## Installation

```bash
dotnet tool install --global ZapJobs.Cli
```

## Commands

```bash
# Interactive setup
zapjobs init

# Database migration
zapjobs migrate -c "connection-string"

# Status monitoring
zapjobs status -c "connection-string" --watch
```
```

### Versionamento Centralizado

Criar `Directory.Build.props` na raiz:

```xml
<Project>
  <PropertyGroup>
    <VersionPrefix>1.0.0</VersionPrefix>
    <VersionSuffix></VersionSuffix>
    <Authors>Your Name</Authors>
    <Company>Your Company</Company>
    <Product>ZapJobs</Product>
    <Copyright>Copyright © 2024 Your Name</Copyright>
    <PackageProjectUrl>https://github.com/yourusername/zapjobs</PackageProjectUrl>
    <RepositoryUrl>https://github.com/yourusername/zapjobs</RepositoryUrl>
  </PropertyGroup>
</Project>
```

### CLI como Tool

Para ZapJobs.Cli, adicionar:

```xml
<PropertyGroup>
  <PackAsTool>true</PackAsTool>
  <ToolCommandName>zapjobs</ToolCommandName>
</PropertyGroup>
```

## Arquivos a Criar

```
/
├── Directory.Build.props           # Versionamento centralizado
├── icon.png                        # Ícone do pacote (128x128)
├── LICENSE                         # MIT License
├── src/
│   ├── ZapJobs.Core/
│   │   └── README.md               # README específico
│   ├── ZapJobs/
│   │   └── README.md
│   ├── ZapJobs.Storage.InMemory/
│   │   └── README.md
│   ├── ZapJobs.Storage.PostgreSQL/
│   │   └── README.md
│   ├── ZapJobs.AspNetCore/
│   │   └── README.md
│   └── ZapJobs.Cli/
│       └── README.md
```

## Comandos de Build

```bash
# Build e criar pacotes
dotnet pack -c Release

# Publicar no NuGet (requer API key)
dotnet nuget push "**/*.nupkg" --api-key $NUGET_API_KEY --source https://api.nuget.org/v3/index.json
```

## Critérios de Aceitação

1. [ ] Todos os pacotes têm metadados corretos
2. [ ] READMEs específicos por pacote
3. [ ] Ícone visível no NuGet
4. [ ] Source Link funcionando
5. [ ] XML docs incluídas
6. [ ] Symbol packages (.snupkg) gerados
7. [ ] CLI instalável como tool global
8. [ ] Versionamento centralizado funciona
