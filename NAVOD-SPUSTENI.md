# MiniEngine – Jak to spustit na Macu

## 1. Nainstaluj .NET 10 SDK (jednorázově)

Otevři [dot.net](https://dotnet.microsoft.com/download/dotnet/10.0) a stáhni **SDK** instalátor pro **macOS Arm64** (.pkg). Nainstaluj standardním způsobem.

Ověření instalace v Terminálu:

```bash
dotnet --version
```

Mělo by vypsat verzi např. `10.0.100` nebo novější.

## 2. Spusť engine

V Terminálu přejdi do složky s projektem a spusť:

```bash
cd ~/Desktop/Engine/MiniEngine/src/MiniEngine
dotnet run
```

První spuštění chvíli trvá (stahují se balíčky a kompiluje se kód). Poté se otevře okno **MiniEngine**.

## 3. Ovládání (trackpad-friendly)

Nápověda k ovládání je zobrazena přímo v pravém horním rohu 3D viewportu v panelu **Help Overlay** (lze minimalizovat kliknutím na **Skrýt nápovědu** nebo rozbalit tlačítkem **?**).

* **Pravá myš + WASD / šipky** = Pohyb letící kamery, **E/Q** = nahoru/dolů, **Shift** = zrychlení
* **Scroll na trackpadu / kolečko** = Přiblížení (Zoom)
* **Alt (Option) + tažení prstem** = Rozhlížení kamery
* **Kliknutí levým tlačítkem** = Výběr objektu (žluté ohraničení), klik do prázdna = zrušit výběr
* **W / E / R** = Změna režimu Gizma: **W** = Posun, **E** = Rotace, **R** = Měřítko (Scale)
* **F** = Zaměření kamery (Focus) na vybraný objekt (rovněž dostupné tlačítkem **Focus** v inspektoru)
* **Cmd+D** = Duplikovat vybraný objekt
* **Delete** = Smazat vybraný objekt
* **Drag & Drop**:
  * Přetáhni libovolný model `.glb` z dolního **Asset Browseru** přímo do 3D viewportu pro spawn na zemi pod kurzorem.
  * Přetáhni entitu na druhou v panelu **Hierarchy** pro vytvoření hierarchické vazby.

## 3b. Resetování rozvržení oken

Pokud se panely v editoru rozloží nesprávně (např. vlivem načtení staré konfigurace v souboru `imgui.ini`), můžeš jejich rozvržení okamžitě vrátit do výchozího stavu pomocí tlačítka **Reset rozvržení** v horní liště (Toolbar).

Pokud by tlačítko nepomohlo, je možné smazat konfigurační soubor `imgui.ini` ručně a spustit engine znovu:

```bash
cd ~/Desktop/Engine/MiniEngine/src/MiniEngine
rm -f imgui.ini
dotnet run
```

Panely se otevřou ve výchozím rozvržení: Hierarchy vlevo, Viewport uprostřed, Inspector/Svetlo vpravo, široký spodní panel (Asset Browser, Profiler, Node Editor) dole. Rozvržení si můžeš libovolně upravit přetažením panelů za jejich záhlaví.

## 3c. Ukládání a načítání scény

V panelu Hierarchy slouží tlačítka **Uložit scenu** a **Nacist scenu** k serializaci celé scény (včetně světel, částic, zvuků a chování objektů) do souboru `scene.json` ve složce `src/MiniEngine/`.
Uložená scéna je plně přenositelná a AOT safe.

## 4. Možné potíže (Troubleshooting)

* **`DllNotFoundException: cimgui`**
  Známé specifikum NuGet balíčku ImGui.NET na macOS. Vyřešíš jej zkopírováním knihovny `libcimgui.dylib` z `bin/Debug/net10.0/runtimes/osx-arm64/native/` přímo do složky `bin/Debug/net10.0/` vedle spustitelného souboru `MiniEngine.dll`. Poté spusť znovu `dotnet run`.
