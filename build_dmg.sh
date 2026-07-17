#!/bin/bash
set -e

echo "=== 1. Kompilace projektu v Release režimu ==="
dotnet publish src/MiniEngine/MiniEngine.csproj -c Release -r osx-arm64 --self-contained -p:PublishReadyToRun=true

PUBLISH_DIR="src/MiniEngine/bin/Release/net10.0/osx-arm64/publish"
APP_DIR="src/MiniEngine/bin/Release/net10.0/osx-arm64/MiniEngine.app"

echo "=== 2. Vytváření struktury macOS .app balíčku ==="
rm -rf "$APP_DIR"
mkdir -p "$APP_DIR/Contents/MacOS"
mkdir -p "$APP_DIR/Contents/Resources"

echo "=== 3. Kopírování spustitelných souborů a assetů ==="
cp -R "$PUBLISH_DIR"/* "$APP_DIR/Contents/MacOS/"

# Vytvoření Info.plist pro macOS
cat <<EOF > "$APP_DIR/Contents/Info.plist"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key>
    <string>MiniEngine</string>
    <key>CFBundleIdentifier</key>
    <string>cz.miniengine.editor.v2</string>
    <key>CFBundleName</key>
    <string>MiniEngine</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0.0</string>
    <key>CFBundleSignature</key>
    <string>????</string>
    <key>CFBundleVersion</key>
    <string>1.0.0</string>
    <key>LSMinimumSystemVersion</key>
    <string>10.15</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
EOF

# Nastavení spustitelných práv
chmod +x "$APP_DIR/Contents/MacOS/MiniEngine"

# Ad-hoc podepsání aplikace (nezbytné pro spuštění z Finderu na Apple Silicon)
echo "=== Kódový podpis aplikace (Ad-hoc codesign) ==="
codesign --force --deep --sign - "$APP_DIR"

echo "=== 4. Příprava složky pro DMG instalátor ==="
DMG_TEMP="src/MiniEngine/bin/Release/net10.0/osx-arm64/dmg_temp"
rm -rf "$DMG_TEMP"
mkdir -p "$DMG_TEMP"

# Kopírování aplikace
cp -R "$APP_DIR" "$DMG_TEMP/"

# Vytvoření symlinku na složku /Applications
ln -s /Applications "$DMG_TEMP/Applications"

echo "=== 5. Sestavení DMG obrazu (Disk Image) ==="
OUT_DMG="MiniEngine_Installer.dmg"
rm -f "$OUT_DMG"

hdiutil create -volname "MiniEngine Installer" -srcfolder "$DMG_TEMP" -ov -format UDZO "$OUT_DMG"

# Vyčištění dočasných souborů
rm -rf "$DMG_TEMP"

echo "=== HOTOVO! Instalátor byl vytvořen jako: $OUT_DMG ==="
