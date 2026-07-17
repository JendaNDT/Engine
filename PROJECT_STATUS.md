# MiniEngine – Project Status
*Naposledy aktualizováno: 17. 7. 2026 (Stabilizace, Prefaby, Behavior Node Graph, Duplikace a UI/UX)*

## 🎯 Co to je
Nativní C# 3D herní engine s editorem, plně multiplatformní (Windows x64 a macOS Apple Silicon ARM64) s plnou podporou kompilace **Native AOT** pro maximální výkon a nulové spouštěcí závislosti.
* **Stack**: C# / .NET 10, Raylib-cs (7.0.1), ImGui.NET + rlImGui-cs, BepuPhysics v2, vlastní sparse-set ECS.

---

## ✅ Hotové funkce (Fáze 1 - 19)

### 1. Jádro a Architektura (ECS, Transformace & Hierarchie)
* **Vlastní sparse-set ECS**: Zcela bezalokační, Native AOT safe, bez reflexe za běhu.
* **TransformSystem se stamp rekurzí**: Správné vyhodnocování rodičovství (Parent/Child) bez ohledu na pořadí v poli. Cykly v hierarchii jsou bezpečně odmítnuty.
* **Převody lokálních a světových souřadnic**: Editor pracuje ve světovém prostoru (`TransformHierarchy`), rotace se ukládají jako kvaterniony, přičemž v UI jsou reprezentovány Eulerovými úhly s live invalidací cache.
* **Smazání rodiče**: Převěsí děti automaticky na prarodiče, aby nedošlo k rozbití scény a nechtěnému smazání celého podstromu (editor nemá undo).

### 2. Rendering, Osvětlení a Stíny
* **Blinn-Phong Shader**: Směrové světlo (slunce) s nastavitelným azimutem/elevací, barvou a silou + ambientní osvětlení scény.
* **Shadow Mapping**: Stínové mapy s 3x3 PCF (Percentage-Closer Filtering) filtrem a dynamickým biasem proti depth acne (stínovým pruhům).
* **Filmový Post-processing (Fáze 11 & 14)**:
  * **Bloom (Záře)**: Jednoprůchodový jasový filtr rozostřující světlé pixely (např. oheň).
  * **Vignette (Vinětace)**: Filmové zatmavení rohů.
  * **ACES Tonemapping**: Převod HDR hodnot jasu do LDR spektra obrazovky, zabraňující plochým přepalům.
  * **Barevné korekce**: Posuvníky pro kontrast a saturaci (odbarvení do černobílé Noir až po plné barvy).
* **Equirectangular Skybox (Fáze 9)**: Vykreslování 360° panoramatické oblohy z 2D textury bez nutnosti alokovat drahé OpenGL cubemapy. Načtena prémiová textura západu slunce s hvězdami.
* **Tint a Textury (Fáze 6)**: Možnost přepsat barvu nebo albedo texturu pro každý 3D model individuálně s ref-counted cache v `AssetManager`.
* **Částicový systém (Fáze 7)**: Bezalokační emitor částic simulující až 2000 částic na CPU s předvolbami pro Oheň, Kouř a Jiskry.

### 3. Fyzika a Kolize
* **BepuPhysics v2.4 Integrace**: Spouštění a zastavování fyzikální simulace z toolbaru.
* **Convex Hull Kolize (Fáze 2)**: Automatický výpočet přesných fyzikálních kolizních obálek pro libovolné načtené 3D modely (např. donut).
* **Character Controller (Fáze 4)**: Fyzikální kapsle reprezentující hráče s uzamčenou rotací, pohybem WASD vůči kameře a skákáním.

### 4. Audio systém (Spatial 3D Audio - Fáze 8)
* **Raylib Audio Device**: Inicializace a korektní uzavření zvukového sub-systému.
* **Prostorový 3D zvuk**: Hlasitost (roll-off útlum na dálku) a stereofonní panning (směr do levého/pravého ucha) se přepočítávají za chodu podle polohy a úhlu kamery. Zvuky lze smyčkovat a vypínat.

### 5. Skriptování chování (Behavior - Fáze 10)
* **Dynamické chování**: Komponenta `BehaviorComponent` umožňuje přiřadit k objektům předdefinované pohyby:
  * **Rotátor** (točení kolem zadané osy)
  * **Ping-Pong** (pohyb tam a zpět)
  * **Obíhač** (kruhová oběžná dráha)
  * **Sinusové houpání** (vznášení se)
* **Fyzikální servo-řízení (Velocity tracking)**: Pokud má objekt fyzikální tělo, systém namísto teleportace nastavuje jeho lineární a úhlovou rychlost (`diff / dt`). Tělesa se plynule pohybují podle skriptu, ale plně kolidují s okolním světem.
* **Automatický reset**: Při kliknutí na "STOP" se všechny objekty automaticky vrátí (snepnou) na své výchozí pozice, aby nedocházelo k driftování scény.

### 6. Komfortní Editor a UX (Fáze 12, 13, 15 - 19)
* **Drag & Drop modelů (Fáze 15)**: Přetažení `.glb` modelu z Asset Browseru přímo do 3D viewportu. Spawne se na průsečíku s rovinou země Y=0.
* **Filtrování a Hledání (Fáze 16)**: Asset Browser obsahuje textové hledání a Combo Box pro filtrování typů souborů (Modely, Textury, Zvuky, Shadery) včetně tematických ikon.
* **Focus na objekt (Fáze 17)**: Tlačítko **Focus** v inspektoru a klávesa `F` plynule zaměří kameru na vybraný objekt na základě jeho měřítka.
* **Průmyslové zkratky Gizma (Fáze 18)**: Přepínání režimů Gizma (Posun, Rotace, Měřítko) standardními klávesami `W`, `E`, `R`. Zkratky se automaticky zablokují při letu kamerou, což eliminuje konflikty.
* **Profiler panel (Fáze 12)**: Záložka vedle statistik zobrazuje live liniové grafy pro FPS, Frame Time (ms), paměťové alokace za snímek a počet částic.
* **Kompletní serializace (Fáze 6, 7, 8, 10)**: Ukládání a načítání celé scény (včetně textur, částic, zvuků a behavior skriptů) do `scene.json` (AOT safe).
* **Dropdown výběr assetů (Fáze 13)**: Ruční přepisování cest k souborům v inspektoru nahrazeno Combo Boxy se seznamy reálně nalezených souborů na disku. Tlačítko pro obnovení seznamu souborů za chodu.
* **Help Overlay (Fáze 19)**: Minimalizovatelný překryvný panel s nápovědou ovládání kamery a klávesových zkratek v pravém horním rohu viewportu.

### 7. Audit, stabilizace a optimalizace (Fáze 20 - 36)
* **Zero-Allocation Undo/Redo (Fáze 21)**:
  - Optimalizovaný vzor Command Pattern (`IEditorCommand`, `CommandHistory`, `EntitySnapshot`, `CompositeCommand`) zaznamenávající pouze konkrétní rozdíly komponent, čímž eliminuje GC alokace v hlavní smyčce.
* **ACES PBR Vykreslování & CSM (Fáze 22, 24)**:
  - Cook-Torrance GGX specular BRDF, ACES Filmic tonemapping, albedo/normal/metallic-roughness textury integrované do standardního rendering řetězce.
  - Kaskádové stíny (CSM) se stínovým atlasem **4096x4096px** rozděleným na 3 kaskády, se stabilizací shadow kamer pro eliminaci chvění stínů a PCF filtrací.
* **Dědičnost a propojení prefabů (Fáze 23)**:
  - Komponenta `PrefabLink` ukládající relativní cestu k blueprintu a přepsané vlastnosti. Propagace a rehydratace instancí prefabu pomocí DFS vyhodnocení.
  - Tlačítka **Revert to Prefab** a **Apply to Prefab** v inspektoru.
* **Vizuální editor logiky - Behavior Node Graph (Fáze 25)**:
  - Grafický editor v `NodeEditorPanel.cs` pro navrhování logiky s interpretovaným `BehaviorGraph.cs`, napojeným na trigger zóny, kolize a start/update eventy.
* **Optimalizace rozvržení a UX (Fáze 26)**:
  - Globální Toolbar (Play/Stop, Undo/Redo, tlačítko pro Reset rozvržení oken), Stats overlay, sloučený panel Inspector + Světlo na pravé straně.
  - Široký spodní panel pro Asset Browser + Profiler + Node Editor, a vyhledávací lišta v panelu Hierarchy.
* **Fyzikální a Audio stabilita (Fáze 27, 28)**:
  - ECS `OnRemoved` callback uvolňující tělesa z BepuPhysics simulace (`Untrack`), což zabraňuje únikům těles a duchům ve scéně.
  - Raylib sound aliasy zamezující konfliktům hlasitosti a pitchů. Prevence `NaN` ve fyzikálních a audio systémech.
* **Asset Garbage Collection & ECS optimalizace (Fáze 29, 30)**:
  - Periodické čištění nepoužívaných textur a zvuků z RAM/VRAM každé 2 sekundy (`CollectUnusedAssets`).
  - Free-list pro recyklaci modelových indexů, uvolnění referencí stringů v generic `Store<T>.RemoveAt`.
* **UX, zkratky a AOT opravy (Fáze 31 - 36)**:
  - SpawnRate limitace částic (nekonečný cyklus), přejmenování hierarchie (Escape), viewport zkratky (Delete, Backspace).
  - AOT serializační warningy a typově bezpečné JSON operace (`SceneJsonContext`).
  - Zacyklení hierarchie (IsDescendantOf), frame overflow ochrana čítače `_frame`.
  - Hluboká duplikace všech komponent s remapováním cílů v `DuplicateSelected`.
  - Zabezpečení myši (Mouse Lockout) na ztrátu fokusu a `transforms.Has` validace.
  - OnChanged v `LightPanel.cs`, deserializační validace v `SceneSerializer.cs`, podpora triggerů bez Name.
  - Inkrementální Toast IDs proti problikávání, ImGuiTheme tabs styling, ukotvená drop-zóna.

---

## 📁 Stav souborů a struktura projektu
* `src/MiniEngine/Program.cs` – Vstupní bod s `[STAThread]`
* `src/MiniEngine/Core/Ecs.cs` – Bezalokační World + Store
* `src/MiniEngine/Core/Components.cs` – ECS data (Transform, MeshRenderer, Emitter, Audio, Behavior)
* `src/MiniEngine/Core/TransformHierarchy.cs` – Matematické local-to-world a world-to-local transformace
* `src/MiniEngine/Core/BehaviorSystem.cs` – Logika pohybu a fyzikální servo-řízení rychlosti
* `src/MiniEngine/Engine/Game.cs` – Hlavní cyklus, inicializace a zprostředkování všech panelů editoru
* `src/MiniEngine/Engine/SceneSerializer.cs` – Serializátor (AOT-compatible JSON)
* `src/MiniEngine/Audio/AudioSystem.cs` – Výpočty 3D prostorového útlumu a panování
* `src/MiniEngine/Rendering/LightingShader.cs` – Správa osvětlovacího shaderu a FBO pro stínové mapy
* `src/MiniEngine/Rendering/SkyboxRenderer.cs` – Načítání 360° panorámat a rendering oblohy
* `src/MiniEngine/Rendering/PostProcessing.cs` – Správa Bloom, Vignette, kontrastu a saturace
* `src/MiniEngine/Rendering/ParticleSystem.cs` – Bezalokační simulátor částicových poolů na CPU
* `src/MiniEngine/Editor/EditorViewport.cs` – Viewport panel, drag-drop cíle, help overlay
* `src/MiniEngine/Editor/EditorCamera.cs` – Letová a zaostřovací free kamera
* `src/MiniEngine/Editor/TransformGizmo.cs` – Osy a manipulace posunu, rotace a měřítka
* `src/MiniEngine/Editor/Panels/HierarchyPanel.cs` – Strom entit s drag & drop reparentingem
* `src/MiniEngine/Editor/Panels/InspectorPanel.cs` – Inspector panel se skenerem assetů a rozbalovacími nabídkami
* `src/MiniEngine/Editor/Panels/LightPanel.cs` – Panel světel a post-processing posuvníků
* `src/MiniEngine/Editor/Panels/ProfilerPanel.cs` – Panel diagnostiky výkonu s liniovými grafy
* `src/MiniEngine/Assets/AssetManager.cs` – Ref-counted keš pro 3D modely, textury a zvuky
* `src/MiniEngine/Physics/PhysicsWorld.cs` – BepuPhysics wrapper s podporou Convex Hull kolizí
* `assets/shaders/` – Zdrojové soubory pro osvětlení, oblohu a post-processing shadery
