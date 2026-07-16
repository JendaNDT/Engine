# MiniEngine

Lehký nativní 3D herní engine s editorem. Jedna codebase pro macOS (Apple Silicon) a Windows x64.

Stack: C# / .NET 10, Raylib-cs 7.0.1, ImGui.NET + rlImGui-cs, BepuPhysics v2, vlastní sparse-set ECS.

## Spuštění (vývoj)

```
cd src/MiniEngine
dotnet run
```

Detaily pro macOS (včetně řešení potíží s cimgui): `NAVOD-SPUSTENI.md`

## Hotové buildy

GitHub Actions (`.github/workflows/build.yml`) staví NativeAOT publish pro **win-x64** a **osx-arm64**.
Spouští se ručně (Actions → build → Run workflow) nebo tagem `v*`.
Výsledné buildy najdeš v sekci **Artifacts** u doběhlého běhu.

## Editor v kostce

- `1`/`2`/`3` — gizmo: posun / rotace / měřítko; Cmd (Ctrl) při tažení = přichytávání
- WASD/šipky — kamera (Shift = rychleji), Alt+tažení = rozhlížení, kolečko = zoom
- `F` — přiletět k výběru, `R` — reset kamery
- klik ve viewportu — výběr, Cmd/Ctrl+D — duplikovat
- hierarchie: přetažení entity na jinou v Hierarchy panelu = převěšení
- Fyzika: START/STOP v toolbaru; Uložit/Načíst scénu v Hierarchy

## Stav projektu

Průběžný stav, známé bugy a klíčová rozhodnutí: `PROJECT_STATUS.md`
