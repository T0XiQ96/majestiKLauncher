# majestiK Launcher

**English** | [Deutsch](#deutsch)

Mod launcher for **Project Zomboid (Build 42)** – built for **permanent, fixed server mod data**.

## What is it for?

Normally Project Zomboid loads a server's mods through the Steam Workshop. When a mod author updates a mod, the version changes in the middle of operation: server and players no longer match, mods break, and the game says *"File doesn't match the one on the server"*.

The majestiK Launcher fixes this with a **fixed mod package**:

- The **server operator ("DEV")** bundles the mods in a tested state into one package (ZIP) and publishes it.
- **Players** install exactly this package with one click. It does **not** change by itself when a mod is updated on the Workshop – everybody has the same state as the server.
- When the operator publishes a new package, the launcher **detects the update** ("Update needed") and installs it with one click.
- The launcher also sets up **ZombieBuddy** (Java mods), RAM, the server address and the Horse mod menu entry.
- The launcher **updates itself**: while it is open it checks for new launcher versions (at start and every 30 minutes) and can replace itself with one click.

## Download

➡️ **[majestiKLauncher_Download.zip (latest version)](https://github.com/T0XiQ96/majestiKLauncher/releases/latest/download/majestiKLauncher_Download.zip)**

Unzip into its own folder (e.g. `C:\majestiKLauncher`) and start `majestiKLauncher.exe`. Requires Windows 10/11 (.NET Framework 4.x is preinstalled) and Project Zomboid via Steam.

> Windows SmartScreen may warn on the first start (unknown publisher): "More info" → "Run anyway".

## How it works – for players

1. Start `majestiKLauncher.exe`.
2. Choose your server in the **Server** list at the top. Servers are preconfigured by the operator – players cannot add or delete servers.
3. Click **Install / Update**. The launcher downloads the package, extracts the mods to `Zomboid\mods`, sets up ZombieBuddy and optionally the RAM. (Servers that use a Steam collection: "Open collection in Steam" → "Subscribe to all".)
4. Click **Play** – the game starts via Steam and the server address is copied to the clipboard (in-game: Join → paste).
5. Later: when a new package exists you see **"Update needed"** – install again. Done.

Other features: **ZombieBuddy prompt** (if ZombieBuddy / Java mods are detected but not set up, the launcher offers the latest version + B42 fix), **Install ZombieBuddy only** (choose version, original or compatibility fix), **custom logo per server**, **Deutsch / English** switch (top right).

**What the launcher changes:** only `%UserProfile%\Zomboid\mods` (incl. `default.txt`), `ProjectZomboid64.json` (with backup) and – for ZombieBuddy – `ZombieBuddy.jar` / `zbNative.dll` in the game folder.

## For server operators

Start **`Launcher DEV.bat`** (included in the download; or the exe with `--dev`). The **DEV** tab lets you

- build the mod package from your Workshop mods (load order, dependency check, own mods),
- pack it as a ZIP with checksum,
- create the manifest (version, download link, server address, logo),
- update the server INI (`Mods=`, `Map=`),
- manage the servers your players see (add / remove / logos),
- **create a ready-to-share player package (ZIP)** with one button: exe, `launcher.cfg`, `servers.json`, logos and manifests. Players unzip, start, install.

Full step-by-step tutorial (including how to assemble the player package): [DEV_TUTORIAL_EN.txt](DEV_TUTORIAL_EN.txt) · [DEV_TUTORIAL_DE.txt](DEV_TUTORIAL_DE.txt)

### Server list for players (`servers.json`)

The launcher fetches a server list at every start (default: `servers.json` in this repository; override with `serverListUrl` in `launcher.cfg`):

```json
{ "servers": [ { "name": "My Server", "url": "https://.../manifest.json", "logoUrl": "https://.../logo.png" } ] }
```

`url` is either a `manifest.json` (mod package server) or a Steam Workshop collection link.

### Launcher updates

`latest_version.txt` in this repository (`main` branch):

```
1.2.0
https://github.com/T0XiQ96/majestiKLauncher/releases/latest/download/majestiKLauncher.exe
```

To release a new version: raise `AssemblyVersion` in the source, compile, create a new release with `majestiKLauncher.exe` and `majestiKLauncher_Download.zip`, then change line 1 of `latest_version.txt`.

### Build from source

`majestiKLauncher.cs` is the complete source (C# 5, WinForms), compiled with the `csc` from .NET Framework 4.x:

```
csc -target:winexe -out:majestiKLauncher.exe -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Web.Extensions.dll -r:System.IO.Compression.dll -r:System.IO.Compression.FileSystem.dll -resource:logo.jpg,logo.jpg -resource:zbNative.dll,zbNative.dll majestiKLauncher.cs
```

(`logo.jpg` and `zbNative.dll` are embedded resources and are not part of the repository.)

---

# Deutsch

Mod-Launcher für **Project Zomboid (Build 42)** – gebaut für **dauerhafte, feste Server-Moddaten**.

## Wofür ist der Launcher?

Normalerweise lädt Project Zomboid die Mods eines Servers über den Steam-Workshop. Aktualisiert ein Mod-Autor seinen Mod, ändert sich die Version mitten im Betrieb: Server und Spieler passen nicht mehr zusammen, Mods gehen kaputt, das Spiel meldet *„File doesn't match the one on the server"*.

Der majestiK Launcher löst das mit einem **festen Mod-Paket**:

- Der **Server-Betreiber („DEV")** packt die Mods in einem getesteten Stand zu einem Paket (ZIP) und veröffentlicht es.
- **Spieler** installieren genau dieses Paket per Klick. Es ändert sich **nicht** von selbst, wenn ein Mod im Workshop aktualisiert wird – alle haben denselben Stand wie der Server.
- Veröffentlicht der Betreiber ein neues Paket, **erkennt der Launcher das Update** („Aktualisieren nötig") und installiert es per Klick.
- Der Launcher richtet außerdem **ZombieBuddy** (Java-Mods), RAM, Server-Adresse und den Horse-Mod-Eintrag fürs Hauptmenü ein.
- Der Launcher **aktualisiert sich selbst**: solange er offen ist, prüft er (beim Start und alle 30 Minuten) auf neue Launcher-Versionen und kann sich per Klick ersetzen.

## Download

➡️ **[majestiKLauncher_Download.zip (neueste Version)](https://github.com/T0XiQ96/majestiKLauncher/releases/latest/download/majestiKLauncher_Download.zip)**

In einen eigenen Ordner entpacken (z. B. `C:\majestiKLauncher`) und `majestiKLauncher.exe` starten. Voraussetzung: Windows 10/11 (.NET Framework 4.x ist vorinstalliert) und Project Zomboid über Steam.

> Windows SmartScreen kann beim ersten Start warnen (unbekannter Herausgeber): „Weitere Informationen" → „Trotzdem ausführen".

## So funktioniert es – für Spieler

1. `majestiKLauncher.exe` starten.
2. Oben in der Liste **Server** deinen Server wählen. Die Server sind vom Betreiber vorkonfiguriert – Spieler können keine Server hinzufügen oder löschen.
3. **Installieren / Aktualisieren** klicken. Der Launcher lädt das Paket, entpackt die Mods nach `Zomboid\mods`, richtet ZombieBuddy und auf Wunsch den RAM ein. (Server mit Steam-Kollektion: „Kollektion in Steam öffnen" → „Alle abonnieren".)
4. **Spielen** klicken – das Spiel startet über Steam, die Server-Adresse liegt in der Zwischenablage (im Spiel: Join → einfügen).
5. Später: gibt es ein neues Paket, siehst du **„Aktualisieren nötig"** – einfach erneut installieren. Fertig.

Weitere Funktionen: **ZombieBuddy-Rückfrage** (erkennt der Launcher ZombieBuddy / Java-Mods ohne Einrichtung, bietet er die neueste Version + B42-Fix an), **Nur ZombieBuddy installieren** (Version wählen, Original oder Compatibility-Fix), **eigenes Logo pro Server**, Umschalter **Deutsch / English** (oben rechts).

**Was der Launcher ändert:** nur `%UserProfile%\Zomboid\mods` (inkl. `default.txt`), `ProjectZomboid64.json` (mit Backup) und – bei ZombieBuddy – `ZombieBuddy.jar` / `zbNative.dll` im Spielordner.

## Für Server-Betreiber

**`Launcher DEV.bat`** starten (liegt im Download; oder die exe mit `--dev`). Im Tab **DEV** kannst du

- das Mod-Paket aus deinen Workshop-Mods bauen (Ladereihenfolge, Abhängigkeitsprüfung, eigene Mods),
- es als ZIP mit Prüfsumme packen,
- das Manifest erzeugen (Version, Download-Link, Server-Adresse, Logo),
- die Server-INI aktualisieren (`Mods=`, `Map=`),
- die Server verwalten, die deine Spieler sehen (hinzufügen / entfernen / Logos),
- per Knopf ein **fertiges Spieler-Paket (ZIP)** erstellen: exe, `launcher.cfg`, `servers.json`, Logos und Manifeste. Spieler entpacken, starten, installieren.

Komplettes Schritt-für-Schritt-Tutorial (inkl. Zusammenstellen des Spieler-Pakets): [DEV_TUTORIAL_DE.txt](DEV_TUTORIAL_DE.txt) · [DEV_TUTORIAL_EN.txt](DEV_TUTORIAL_EN.txt)

---

*majestiK Launcher by T0X.IQ* – [Steam Workshop](https://steamcommunity.com/id/majestiKTox/myworkshopfiles/?appid=108600)
