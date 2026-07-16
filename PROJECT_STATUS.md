# MiniEngine – Project Status
*Naposled aktualizováno: 16. 7. 2026 (šestnáctá iterace – příprava Windows buildu)*

## 🎯 Co to je
Vlastní lehký nativní 3D herní engine s editorem, jedna codebase pro Windows x64 a macOS Apple Silicon.
Stack: C# / .NET 10, Raylib-cs (7.0.1), ImGui.NET + rlImGui-cs, BepuPhysics v2, vlastní sparse-set ECS.

## ⏭️ Příští krok
**Projít `NAVOD-GITHUB.md`** – založit GitHub repo přes GitHub Desktop, spustit workflow „build" (Actions → Run workflow), stáhnout artifact `MiniEngine-win-x64` a otestovat na Windows.

## ✅ Hotovo
- Technický rozbor architektury (`Claude Deep research/DEEP_RESEARCH.md`)
- Projekt založený ve správné struktuře (`src/MiniEngine/Core|Engine|Editor|Assets|Physics`)
- `Program.cs` – vstupní bod s `[STAThread]` (povinný pro Windows AOT), dřív úplně chyběl
- Boilerplate: ECS, Transform systém, editor viewport, free kamera, asset manager, Bepu wrapper
- **První úspěšná kompilace** – .NET 10, 0 chyb, 0 warningů (ověřeno v Linux sandboxu, 16. 7. 2026)
- Rizika ze statusu ověřena kompilátorem: `DockSpaceOverViewport()` i `GetScreenToWorldRayEx()` v zamčených verzích existují
- CI matrix workflow pro win-x64 + osx-arm64 (`.github/workflows/build.yml`)
- **Hierarchy panel** – seznam entit, výběr klikem, tlačítka + Krychle / Duplikovat / Smazat
- **Inspector panel** – editace názvu, pozice, rotace (euler cache nad kvaternionem), měřítka, barvy a viditelnosti
- **Name komponenta + EditorSelection** – vybraná entita má ve viewportu žluté ohraničení
- **Opraven parent bug v TransformSystem** – `Parent` je index entity, řeší se sparse lookupem
- Převod kvaternion ↔ eulery ověřen testem: 10 000 náhodných rotací, 100% round-trip
- **✨ PRVNÍ BĚH NA MACU (16. 7. 2026)** – okno, ImGui, 200 entit, 113 FPS
- Oprava alokace 64 B/frame (delegat DrawScene se cachuje) – odhalil to vlastní Stats panel
- Výchozí rozložení panelů (Hierarchy vlevo, Viewport uprostřed, Inspector vpravo, Stats vlevo dole)
- **MVP editor shell OVĚŘEN na Macu** – layout, výběr entit, Inspector, 96 FPS, **0 B alokací/frame** ✅
- **Načítání .glb modelů OVĚŘENO na Macu** – AssetManager → `DrawMesh`, testovací donut se po opravě transpozice vykresluje správně jako prstenec
- Oprava oříznutých popisků v Inspectoru (`PushItemWidth`)
- **Picking OVĚŘEN na Macu** – levý klik do viewportu vybírá entity (AABB u krychlí, přesný mesh test u modelů), nejbližší zásah vyhrává, klik do prázdna odznačí
- **Oprava deformace modelů** – matice do raylib API (DrawMesh, GetRayCollisionMesh) se musí transponovat (row-major vs column-major); donut se rendroval jako obří placka
- **Viditelné ovládání kamery** – toolbar nad viewportem (Reset kamery, Na výběr + nápověda), zoom kolečkem/trackpadem bez držení tlačítka, klávesy F (na výběr) a R (reset)
- **Trackpad-friendly ovládání** – šipky/WASD hýbou kamerou bez držení tlačítek (stačí myš nad viewportem), Alt+tažení = rozhlížení, Alt+klik nevybírá objekty
- **Oprava: Alt+tažení už neodtahuje okno** – `ConfigWindowsMoveFromTitleBarOnly` (okna se přesouvají jen za titulek) + rozhlížení se latchne do puštění tlačítka
- **Ovládání kamery OVĚŘENO na Macu** (Alt+tažení, šipky, zoom, picking na donut)
- **Serializace scény** – tlačítka Uložit/Načíst v Hierarchy, JSON se source-generated kontextem (AOT-safe), modely přes relativní cestu k assets; round-trip ověřen testem (5 entit, dvojitý load)
- **Fyzika (BepuPhysics 2.4) OVĚŘENA na Macu** – tlačítko „Fyzika: START/STOP" v liště; při startu všechna viditelná tělesa dostanou dynamický box + neviditelná podlaha; fixed timestep 60 Hz s interpolací; headless test: pád z 5 m, stohování na tisícinu přesně
- **🏆 MVP KOMPLETNÍ (16. 7. 2026)** – editor + modely + picking + serializace + fyzika, vše běží na Macu
- **Osvětlení OVĚŘENO na Macu (lighting shader, GLSL 330)** – směrové slunce + ambient + Blinn-Phong odlesky; krychle mají stínované stěny, donut objem a odlesk; 104 FPS s 201 entitami
- **Krychle konečně respektují rotaci** – kreslí se přes sdílený mesh s world maticí (DrawCubeV rotaci tiše ignoroval)
- **Panel Světlo OVĚŘEN na Macu** – posuvníky: otočení a výška slunce (azimut/elevace místo XYZ), síla, barva, okolní světlo, odlesky, tlačítko Výchozí; výchozí hodnoty ztlumené proti přepálení
- **Ochrana sdíleného shaderu** – před `UnloadModel` se materiálům vrací default shader, jinak by unload modelu zabil osvětlení celé scény (past z DEEP_RESEARCH 4.1, hrozila při načítání scény)
- Oprava alokace v PhysicsWorld (`.Keys.ToArray()` per krok → paralelní pole)
- **Translate gizmo (šipky X/Y/Z) OVĚŘENO na Macu** – tažení vybraného objektu po world osách; matika „nejbližší bod na ose vůči paprsku myši" ověřena 300 náhodnými případy proti brute force; tažení se latchne do puštění tlačítka; Ctrl/Cmd při tažení = přichytávání na 0.5; vodicí přímka přes scénu; kreslí se přes objekty (vypnutý depth test + flush rlgl batche); za běhu fyziky se skrývá
- **Duplikování entity OVĚŘENO na Macu** – Cmd+D (Mac) / Ctrl+D (Windows) + tlačítko Duplikovat v Hierarchy; kopie Transform + MeshRenderer + Name („… kopie"), posune se o kus vedle a rovnou se vybere
- **Oprava double-free v AssetManageru** – duplikát sdílí ModelHandle, proto nové `AddRef()` při duplikaci + pojistka v `Release()` proti záporném refcountu (dvojí Release by zavolal `UnloadModel` dvakrát → nativní pád)
- **Smazání entity teď uvolňuje model handle** – dřív Smazat v Hierarchy nechal model viset v paměti (refcount nesouhlasil)
- **Rotate + scale gizmo OVĚŘENO na Macu (TransformGizmo, nahrazuje TranslateGizmo)** – tři režimy přepínané klávesami 1/2/3 nebo tlačítky v toolbaru (aktivní je zvýrazněné). Rotace: kružnice kolem world os, tažení po kružnici = otáčení, Cmd = krok 15°, šedý/žlutý paprsek ukazuje odkud kam táhneš. Měřítko: osy v lokální orientaci objektu s koulemi na koncích + středová koule = uniformní scale (tažení do stran), Cmd = krok 0.25, měřítko nikdy neklesne pod 0.01
- **Matika rotace ověřena 4000 náhodnými případy** – konvence násobení kvaternionů, znaménko úhlu (atan2 přes cross·axis), world-space delta, simulace celého tažení po kružnici
- **Oprava známého bugu: euler cache v Inspectoru** – rotate gizmo cache invaliduje (`InvalidateRotationCache`), pole rotace v UI se při tažení aktualizují živě
- **Hierarchie entit s reparentingem OVĚŘENA na Macu** – Hierarchy panel je strom (rozbalování šipkou), přetažení entity na jinou = převěšení, drop zóna dole = zrušení rodiče; převěšení zachovává world pozici/rotaci/měřítko (objekt se ve scéně nehne); cykly se odmítnou
- **TransformSystem přepsán na stamp rekurzi** – vyřešen známý bug „rodič musí ležet v dense poli před dítětem": pořadí už nehraje roli, cyklus v datech (rozbitá scéna) update nezasekne; pořád 0 alokací za frame
- **`Core/TransformHierarchy.cs`** – world↔local převody (WorldRotation, SetWorldPosition/Rotation, WorldScale, Reparent, IsDescendantOf), čistá matika bez raylibu; **testováno na reálném kódu enginu**: 200 stromů se zpřeházeným pořadím, 300 reparentů (world póza zachována), zákaz cyklů, SetWorld* trefí cíl (300 případů) – vše OK
- **Editor převeden na world pózy** – gizmo, žluté ohraničení výběru, picking krychlí, klávesa F i fyzika berou world pozici/rotaci/měřítko; dítě otočeného rodiče se chová správně (dřív by všechno jelo z lokálních souřadnic)
- **Smazání rodiče převěsí děti na prarodiče** – world póza zůstane; kdyby děti visely na smazaném indexu, po recyklaci indexu by se připojily k cizí entitě
- **NativeAOT publish OVĚŘEN v sandboxu (linux-x64)** – 15 MB výstup, nativní binárka; první reálný AOT build projektu vůbec
- **Zneškodněna AOT past v BepuPhysics** – Bepu tvoří TypeProcessory přes `Activator.CreateInstance`, trimmer by je vyhodil a fyzika by na Windows spadla až za běhu; `TrimmerRootAssembly` v csproj to řeší (warning IL2072 při publish zůstává viditelný, ale je neškodný)
- **Repo připraveno k publikaci** – `.gitignore` (bin/obj/imgui.ini/scene.json), `README.md`, `NAVOD-GITHUB.md` (krok za krokem přes GitHub Desktop, bez terminálu)

## 🔄 Rozjeté (nedodělané)
- **Tint u modelů se neaplikuje** – barva v Inspectoru zatím funguje jen u krychlí.
- **Nastavení světla se neukládá do scene.json** – po restartu se vrátí výchozí hodnoty.
- **Fyzika: modely mají box kolizi** – donut se sráží jako krabice; přesná mesh kolize v backlogu.
- **Duplikace je mělká** – nekopíruje potomky (kopie rodiče vznikne bez dětí, jen pod stejným rodičem). Duplikace podstromu v backlogu.
- **Character controller** – z Bepu Demos, až bude potřeba hratelná postava.

## 📝 TODO
### MVP
- Celé ověřit na Windows (GitHub Actions workflow je připravený, chybí repo)

### Backlog
- Duplikace celého podstromu (teď je mělká)
- Plný PBR (metallic-roughness) – teď je Blinn-Phong
- Ukládání nastavení světla do scene.json
- Stínové mapy (vlastní framebuffer přes `rlgl`, ne `LoadRenderTexture`)
- Hot-reload assetů přes `FileSystemWatcher`
- Frustum culling

## 🐛 Známé bugy / rizika
- **Rozbité rozložení z prvního běhu se drží v `imgui.ini`** (vzniká vedle csproj při `dotnet run`). Výchozí layout se aplikuje jen na okna bez uloženého stavu – po updatu je potřeba `imgui.ini` jednou smazat.
- **Nerovnoměrné měřítko rodiče + rotace dítěte = zkosení**, které TRS neumí popsat. Reparent i world scale to řeší po osách (stejný kompromis jako Unity) – world pozice sedí vždy, tvar může u extrémů lehce ujet.
- **Fyzika u zanořených dětí** používá rodičovu world matici z minulého framu – u hlubokých řetězů fyzikálních těles může být dítě o frame pozadu. U plochých scén (dnešní stav) se to neprojeví.
- **Euler cache v Inspectoru: fyzika.** Rotate gizmo už cache invaliduje, ale když rotaci změní běžící fyzika, pole rotace v UI se neaktualizují, dokud nepřepneš výběr. Až to začne vadit, invalidovat i po fyzikálním kroku.
- ImGui.NET měl historicky rozbité RID složky na macOS. Když spadne `DllNotFoundException: cimgui`, zkopíruj `libcimgui.dylib` ručně vedle exe (postup v `NAVOD-SPUSTENI.md`).
- **rlImgui-cs 3.2.0 je stavené proti Raylib-cs 7.x** – Raylib-cs 8.0.0 s ním není kompatibilní. Proto pin na 7.0.1, neupgradovat jednotlivě.

## 🏗️ Klíčová rozhodnutí
- **TransformSystem: stamp rekurze místo topologického řazení** – řadit dense pole při každém reparentu by bylo křehké (swap-remove ho stejně přehazuje); stamp pole řeší pořadí i cykly a stojí jeden int na entitu.
- **Smazání rodiče = děti na prarodiče, ne mazání podstromu** – editor nemá undo, omylem smazaný kořen by vzal celou stavbu. Gizmo a editor obecně pracují s WORLD pózou (přes `TransformHierarchy`), lokální hodnoty se dopočítávají.
- **Násobení kvaternionů v System.Numerics: `a * b` aplikuje NEJDŘÍV b, pak a** – přesně naopak než u `Matrix4x4` (tam `a * b` = napřed a). Empiricky ověřeno testem 16. 7. 2026 – první intuice byla špatně a odhalil to až test. World-space delta rotace = `delta * start`.
- **Rotace kolem WORLD os, měřítko po LOKÁLNÍCH osách** – scale se v LocalMatrix aplikuje před rotací, world osy by u otočeného objektu táhly „šikmo". Režimy gizma na klávesách 1/2/3 (písmena W/E/R zabírá pohyb kamery).
- **Gizmo je vlastní (šipky/kružnice/koule + ray-line matika), ne ImGuizmo** – žádná další nativní závislost; ImGuizmo binárky na macOS ARM = stejné riziko jako historicky rozbité cimgui. Tažení po osách, ne po rovinách – předvídatelnější a stačí to.
- **Držené Cmd/Ctrl vypíná pohyb kamery klávesami** – jinak by Cmd+D při duplikování zároveň cuklo kamerou doprava (D = pohyb). Modifikátor = „zkratkový režim".
- **Ovládání kamery nesmí vyžadovat držení tlačítka myši** – Jenda pracuje na MacBooku s trackpadem. Pohyb klávesami vždy při hoveru nad viewportem, rozhlížení Alt+tažení. Klasika (pravá myš) zůstává jako alternativa.
- **Vlastní sparse-set ECS**, ne Arch/Friflo – nulové riziko, že knihovna rozbije NativeAOT reflexí.
- **Kvaternion je zdroj pravdy pro rotaci**, Eulery jen jako dočasný stav v inspektoru.
- **Komponenty drží handle (int)**, ne raylib struktury `Model`/`Mesh` – ty obsahují nativní pointery, kopie = use-after-free.
- **Vyvíjí se v JIT, publikuje v AOT** (`PublishAot` jen v Release). AOT nedává vyšší FPS, jen rychlejší start a nezávislost na runtime.
- **Cíl jen osx-arm64**, ne universal binary. Intel Macy mimo scope.
- **Fyzika: BepuPhysics v2**, ne Jitter2 – vlastní unmanaged BufferPool = nulový GC tlak.
- **Jeden csproj**, ne Engine/Editor/Game split. Až bude potřeba, řeší se `#if EDITOR`.
- **Alias `using Transform = MiniEngine.Core.Transform`** v Game.cs – Raylib_cs má vlastní typ Transform, bez aliasu kompilace padá na CS0104.
- **Matice do raylib API vždy přes `Matrix4x4.Transpose()`** – System.Numerics je row-major, raylib column-major. DEEP_RESEARCH v sekci 1.2 tvrdí opak („DrawMesh to řeší za tebe") – **empiricky vyvráceno** na Macu. Platí pro DrawMesh, GetRayCollisionMesh i SetShaderValueMatrix.
- **Tenhle soubor (`MiniEngine/PROJECT_STATUS.md`) je teď zdroj pravdy.** Starý status v `Claude Deep research/` je archiv.

## 📁 Stav souborů
- `src/MiniEngine/Program.cs` – vstupní bod (`[STAThread]`)
- `src/MiniEngine/Core/Ecs.cs` – World + sparse-set Store
- `src/MiniEngine/Core/Components.cs` – Transform, MeshRenderer, TransformSystem (stamp rekurze)
- `src/MiniEngine/Core/TransformHierarchy.cs` – world↔local převody, Reparent (bez raylibu, testovatelné)
- `src/MiniEngine/Engine/Game.cs` – herní smyčka, demo scéna (200 krychlí), Stats panel, duplikování/mazání
- `src/MiniEngine/Engine/SceneSerializer.cs` – uložit/načíst scénu (JSON, AOT-safe)
- `src/MiniEngine/Editor/EditorViewport.cs` – RenderTexture + ImGui okno + input routing
- `src/MiniEngine/Editor/EditorCamera.cs` – free fly kamera
- `src/MiniEngine/Editor/EditorSelection.cs` – vybraná entita
- `src/MiniEngine/Editor/TransformGizmo.cs` – posun/rotace/měřítko (režimy 1/2/3), přichytávání přes Cmd
- `src/MiniEngine/Editor/Panels/HierarchyPanel.cs` – strom entit, drag &amp; drop reparenting, přidání/duplikace/smazání
- `src/MiniEngine/Editor/Panels/InspectorPanel.cs` – editace vlastností + euler cache
- `src/MiniEngine/Editor/Panels/LightPanel.cs` – nastavení slunce a okolního světla
- `src/MiniEngine/Assets/AssetManager.cs` – handle-based refcounted cache modelů (AddRef/Release)
- `src/MiniEngine/Physics/PhysicsWorld.cs` – Bepu wrapper (fixed timestep, interpolace, boxy)
- `src/MiniEngine/Rendering/LightingShader.cs` – slunce + ambient + odlesky
- `assets/shaders/lighting.vs`, `lighting.fs` – GLSL 330 (strop pro macOS)
- `NAVOD-SPUSTENI.md` – jak to rozjet na Macu
- `NAVOD-GITHUB.md` – jak založit repo a stáhnout Windows build (GitHub Desktop)
- `README.md`, `.gitignore` – repo hygiena
- `.github/workflows/build.yml` – CI pro win-x64 + osx-arm64
