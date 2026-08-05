# Building from Source

Requires the .NET 8 SDK and Node.js 20+.

```bash
git clone https://github.com/Sportarr/Sportarr.git
cd Sportarr

# Build (automatically builds the frontend if Node.js is available)
dotnet build src/Sportarr.csproj

# Run
dotnet run --project src/Sportarr.csproj
```

The build process automatically:

1. Builds the React frontend (if npm is available)
2. Copies the frontend to wwwroot
3. Compiles the .NET backend

To skip the automatic frontend build (e.g. if you built it separately):

```bash
dotnet build src/Sportarr.csproj -p:SkipFrontendBuild=true
```

Tests live in `tests/Sportarr.Api.Tests`:

```bash
dotnet test tests/Sportarr.Api.Tests/Sportarr.Api.Tests.csproj -p:SkipFrontendBuild=true
```

For codebase layout and conventions, see [Architecture](../ARCHITECTURE.md).
