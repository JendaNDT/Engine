# MiniEngine – jak to spustit na Macu

## 1. Nainstaluj .NET 10 SDK (jednorázově)

Otevři https://dotnet.microsoft.com/download/dotnet/10.0 a stáhni **SDK** installer pro **macOS Arm64** (.pkg). Nainstaluj klikáním.

Ověření – otevři Terminál a napiš:

```bash
dotnet --version
```

Mělo by vypsat něco jako `10.0.xxx`.

## 2. Spusť engine

V Terminálu:

```bash
cd ~/Desktop/Engine/MiniEngine/src/MiniEngine
dotnet run
```

První spuštění chvíli trvá (stahují se balíčky). Pak se otevře okno **MiniEngine**.

## 3. Ovládání (trackpad-friendly)

Nápověda je vidět i přímo v liště nad viewportem. Myš/kurzor stačí mít nad viewportem, nic se nedrží:

- **Šipky nebo W/A/S/D** = pohyb kamery, **E/Q** = nahoru/dolů, **Shift** = rychleji
- **Scroll na trackpadu / kolečko** = zoom
- **Alt (Option) + tažení prstem** = rozhlížení
- **Klik levým** na objekt = výběr (žluté orámování), klik do prázdna = zrušit výběr
- **F** = přiletět k vybranému objektu, **R** = reset kamery (na obojí jsou i tlačítka v liště)
- (Pro myš s tlačítky pořád funguje i klasika: držet pravé tlačítko = rozhlížení)
- V panelu Stats: řádek „Alokace/frame" – měl by být **0 B**

## 3b. Po aktualizaci kódu (16. 7. 2026 – oprava rozložení)

První běh uložil rozbité rozložení oken do souboru `imgui.ini`. Jednou ho smaž a spusť znovu:

```bash
cd ~/Desktop/Engine/MiniEngine/src/MiniEngine
rm -f imgui.ini
dotnet run
```

Panely se otevřou správně: Hierarchy vlevo, velký Viewport uprostřed, Inspector vpravo, Stats vlevo dole. Rozložení si pak můžeš upravit tažením za titulek okna (a přetažením do kraje jiného okna se panely dokují) – uloží se samo.

## 3c. Ukládání scény

V panelu Hierarchy jsou tlačítka **Uložit scenu** a **Nacist scenu**. Scéna se ukládá do souboru `scene.json` ve složce, odkud jsi spustil `dotnet run` (typicky `src/MiniEngine/`). Postav si scénu, ulož, zavři engine, spusť znovu, načti – všechno bude tam, kde jsi to nechal.

## 4. Kdyby něco spadlo

- **`DllNotFoundException: cimgui`** → známý problém ImGui.NET na Macu. Najdi `libcimgui.dylib` v `bin/Debug/net10.0/runtimes/` a zkopíruj ho do `bin/Debug/net10.0/` vedle `MiniEngine.dll`. Pak znovu `dotnet run`.
- Cokoliv jiného → zkopíruj chybovou hlášku z Terminálu a pošli mi ji do chatu.
