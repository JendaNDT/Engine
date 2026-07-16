# Návod: GitHub repo + první Windows build

Cíl: dostat MiniEngine na GitHub a nechat GitHub Actions postavit spustitelnou
Windows verzi (a bonusem i macOS verzi). Nepotřebuješ žádné příkazy v terminálu —
všechno jde přes aplikaci **GitHub Desktop**.

## 1. Účet a aplikace (jednorázově)

1. Pokud nemáš účet, založ si ho na **github.com** (Sign up, zdarma).
2. Stáhni **GitHub Desktop** z **desktop.github.com**, nainstaluj a přihlas se
   (File → Options / Preferences → Sign in to GitHub.com).

## 2. Vytvoření repozitáře ze složky MiniEngine

1. V GitHub Desktop: **File → Add Local Repository…** → Choose →
   vyber složku `/Users/jenda/Desktop/Engine/MiniEngine`.
2. Desktop ohlásí, že to ještě není repozitář → klikni na
   **„create a repository here instead"**.
3. V dialogu:
   - **Name:** nech `MiniEngine`
   - **Git ignore:** None (vlastní `.gitignore` už ve složce je)
   - **License:** None (můžeš doplnit později)
   → **Create Repository**.
4. Desktop sám udělá první commit. Kdyby vlevo dole čekaly „changes",
   napiš do pole zprávu (třeba „První commit") a klikni **Commit to main**.

## 3. Publikace na GitHub

1. Nahoře klikni **Publish repository**.
2. Volba **„Keep this code private"**:
   - **odškrtnout = veřejné repo (doporučuji)** — GitHub Actions jsou pak úplně
     zdarma bez limitů,
   - zaškrtnuté = soukromé — taky OK, ale macOS buildy se počítají 10× proti
     limitu 2000 minut/měsíc zdarma; na pár buildů měsíčně to bohatě stačí.
3. **Publish repository**.

## 4. První build

1. V GitHub Desktop: **Repository → View on GitHub** (otevře repo v prohlížeči).
2. Záložka **Actions**. Pokud se GitHub zeptá, jestli workflows povolit, povol je.
3. Vlevo klikni na workflow **build** → vpravo tlačítko **Run workflow** →
   zelené **Run workflow**.
4. Počkej cca 10–20 minut (staví se Windows i Mac verze najednou).
   Zelená fajfka = hotovo. Červený křížek = pošli mi text chyby, opravíme.

## 5. Stažení a spuštění na Windows

1. Klikni na doběhlý běh → dole je sekce **Artifacts**.
2. Stáhni **MiniEngine-win-x64** (zip) a přenes na windowsový počítač.
3. Rozbal celý zip do složky a spusť **MiniEngine.exe**.
4. Windows SmartScreen bude možná varovat (nepodepsaná aplikace) →
   **More info / Další informace** → **Run anyway / Přesto spustit**.

## Příště (běžný cyklus)

1. Po změnách v kódu: GitHub Desktop → napsat zprávu → **Commit to main** → **Push origin**.
2. Na webu: Actions → build → **Run workflow** → stáhnout artifact.

Poznámka: `scene.json` a `imgui.ini` se do repa záměrně neukládají (jsou v
`.gitignore`) — každý stroj si drží vlastní rozložení oken a uloženou scénu.
