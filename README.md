# MiniEngine

Lehký nativní 3D herní engine s editorem. Jedna codebase pro macOS (Apple Silicon ARM64) a Windows x64.
Plně kompatibilní s Native AOT kompilací pro nulové závislosti za běhu.

## Stack
* **Jazyk**: C# / .NET 10
* **Grafika**: Raylib-cs 7.0.1 (OpenGL 3.3)
* **UI**: Dear ImGui (ImGui.NET) + rlImGui-cs
* **Fyzika**: BepuPhysics v2.4 (Convex Hull kolize)
* **Architektura**: Vlastní sparse-set ECS (Zero-Allocation Loop)

## Spuštění (vývoj)

```bash
cd src/MiniEngine
dotnet run
```

*Detaily pro macOS (včetně případného řešení libcimgui.dylib): `NAVOD-SPUSTENI.md`*

## Hotové buildy

GitHub Actions (`.github/workflows/build.yml`) automaticky sestavuje NativeAOT publish pro **win-x64** a **osx-arm64**.
Workflow lze spustit ručně (Actions → build → Run workflow) nebo vytvořením tagu `v*`.
Výsledné binární soubory jsou k dispozici v sekci **Artifacts** u dokončeného běhu workflow.

## Ovládání editoru

* **W / E / R** — Přepnutí režimu Gizma: Posun (Translate) / Rotace (Rotate) / Měřítko (Scale)
* **Pravé tlačítko myši + WASD** — Volný let kamery (Shift = turbo rychlost)
* **Alt + Levá myš** — Rozhlížení kamery
* **Kolečko myši** — Přiblížení / Zoom kamery
* **F** — Zaostření kamery (Focus) na vybraný objekt (lze spustit i tlačítkem v inspektoru)
* **Reset kamery** — Tlačítko v liště viewportu pro návrat kamery na výchozí pozici
* **Kliknutí levým tlačítkem** — Výběr objektu ve 3D scéně, klik do prázdna odznačí výběr
* **Cmd+D / Ctrl+D** — Duplikace vybraného objektu
* **Delete** — Smazání vybraného objektu
* **Přetažení (Drag & Drop)**:
  * Přetažením 3D modelu (`.glb`) z Asset Browseru do 3D viewportu jej spawnete do scény na pozici pod kurzorem.
  * Přetažením entit přes sebe v panelu Hierarchy měníte jejich hierarchické vazby (rodič/dítě).

## Dokumentace
* **Stav projektu**: Podrobný přehled dokončených fází, architektury a struktury souborů najdete v `PROJECT_STATUS.md`.
* **Průvodce spuštěním**: Detailní kroky pro macOS najdete v `NAVOD-SPUSTENI.md`.
* **GitHub návod**: Jak založit repo a spouštět buildy najdete v `NAVOD-GITHUB.md`.
