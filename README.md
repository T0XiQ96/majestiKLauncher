# majestiK Launcher

Mod-Launcher für **Project Zomboid (Build 42)** mit **mehreren Servern**: Mods installieren, aktualisieren und ZombieBuddy einrichten – per Klick.

## Download

➡️ **[majestiKLauncher_Download.zip (neueste Version)](https://github.com/T0XiQ96/majestiKLauncher/releases/latest/download/majestiKLauncher_Download.zip)**

ZIP entpacken (am besten in einen eigenen Ordner, z. B. `C:\majestiKLauncher`) und `majestiKLauncher.exe` starten. Voraussetzung: Windows 10/11 (.NET Framework 4.x ist vorinstalliert), Project Zomboid über Steam.

> Windows SmartScreen kann beim ersten Start warnen (unbekannter Herausgeber): „Weitere Informationen“ → „Trotzdem ausführen“.

## Funktionen

- **Server-Dropdown** – Very Far Away, Project Viewpoint, … eigene Server mit „+ Server hinzufügen“ eintragen und wieder löschen
- **Steam-Kollektion als Server** – Link einfügen, der Launcher öffnet sie in Steam („Alle abonnieren“)
- **Download-Link als Server** – Link zu einer `manifest.json`, der Launcher lädt das komplette Mod-Paket und installiert es
- **Nur ZombieBuddy installieren** – Version wählen (GitHub-Releases), Original oder Compatibility-Fix, mit Hinweis auf die nötigen Steam-Startoptionen
- **Eigenes Logo pro Server** (oder vom Betreiber per Manifest)
- **Selbst-Update** – erkennt neue Versionen, solange der Launcher offen ist, und installiert sie per Klick
- **DEV-Tab für Betreiber** (`majestiKLauncher.exe --dev`) – Mod-Paket bauen, ZIP packen, Manifest erzeugen, pro Server eigene Einstellungen

## So benutzt du ihn

1. `majestiKLauncher.exe` starten.
2. Oben den Server wählen oder „+ Server hinzufügen“.
3. „Installieren / Aktualisieren“ klicken (bei Kollektionen: „Kollektion in Steam öffnen“ und dort alles abonnieren).
4. „Spielen“ – der Launcher startet das Spiel über Steam und kopiert die Server-Adresse in die Zwischenablage.

Eine ausführliche Anleitung steht im Launcher unter „Info / Anleitung“.

## Was der Launcher ändert

Nur `%UserProfile%\Zomboid\mods`, `ProjectZomboid64.json` (mit Backup) und – bei ZombieBuddy – `ZombieBuddy.jar` / `zbNative.dll` im Spielordner.

## Updates (für Betreiber des Launchers)

Der Launcher liest `latest_version.txt` aus diesem Repository (`main`-Branch):

```
1.1.0
https://github.com/T0XiQ96/majestiKLauncher/releases/latest/download/majestiKLauncher.exe
```

Neue Version veröffentlichen: Versionsnummer im Quelltext erhöhen (`AssemblyVersion`), neu kompilieren, einen neuen Release mit `majestiKLauncher.exe` und `majestiKLauncher_Download.zip` anlegen und die Zahl in `latest_version.txt` anpassen.

## Selbst bauen

`majestiKLauncher.cs` ist der komplette Quelltext (C# 5, WinForms). Kompiliert mit dem `csc` aus .NET Framework 4.x:

```
csc -target:winexe -out:majestiKLauncher.exe -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Web.Extensions.dll -r:System.IO.Compression.dll -r:System.IO.Compression.FileSystem.dll -resource:logo.jpg,logo.jpg -resource:zbNative.dll,zbNative.dll majestiKLauncher.cs
```

(`logo.jpg` und `zbNative.dll` sind eingebettete Ressourcen und nicht im Repo enthalten.)

---

*majestiK Launcher by T0X.IQ* – [Steam-Workshop-Seite](https://steamcommunity.com/id/majestiKTox/myworkshopfiles/?appid=108600)
