# BwDependencyScanner V2

Version 2 du scanner .NET 8 pour dépôt TIBCO BusinessWorks.

## Nouvelles capacités

- Analyse transitive des dépendances à partir des fichiers contenant un message donné.
- Distinction entre `Global`, `Shared` et `JobShared` variables.
- Classification des références de message : `Input`, `Output`, `Fault`, `Operation`, `Unknown`.
- Filtrage optionnel par sous-dossier ou package.
- Export CSV en plus de la sortie JSON.
- Graphe d'arêtes de dépendance `Process` / `SharedResource`.

## Exemple

```bash
dotnet run --project src/BwDependencyScanner.Cli/BwDependencyScanner.Cli.csproj "C:\RepoBW" "CreateOrderMessage" "Order" "report.csv"
```
