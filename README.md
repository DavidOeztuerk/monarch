# Monarch

Monarch is a local-first orchestration layer for AI coding agents: it keeps an append-only ledger of quests, evidence and decisions, and enforces policy gates around agent work. It ships as a single Native-AOT command line binary (`monarch`) built on .NET 10. This repository currently contains only the solution skeleton.

## Build, test, publish

```sh
dotnet build -c Release
dotnet test -c Release --no-build
dotnet publish src/Monarch.Cli -c Release -r osx-arm64 -o artifacts/osx-arm64
./artifacts/osx-arm64/monarch --version
```
