// majestiK Launcher - Project Zomboid Mod Launcher (mehrere Server, Steam-Kollektionen, ZombieBuddy)
// Ziel: .NET Framework 4.x (auf Windows 10/11 vorinstalliert), WinForms.
// Kompiliert mit: mcs -target:winexe -sdk:4.5 -r:System.Windows.Forms.dll -r:System.Drawing.dll
//                 -r:System.Web.Extensions.dll -r:System.IO.Compression.dll -r:System.IO.Compression.FileSystem.dll

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.7.2", FrameworkDisplayName = ".NET Framework 4.7.2")]
[assembly: System.Reflection.AssemblyTitle("majestiK Launcher")]
[assembly: System.Reflection.AssemblyVersion("1.2.1.0")]

namespace VFALauncher
{
    // ------------------------------------------------------------------ Server-Liste (mehrere Server im Dropdown)
    class ServerEntry
    {
        public string Name = "", Url = "", Kind = "manifest", Slug = "", Logo = "", LogoUrl = ""; public bool Remote;
        public bool IsCollection { get { return Kind == "collection"; } }
        public override string ToString() { return Name + (IsCollection ? "  (Steam-Kollektion)" : ""); }
        public static bool LooksLikeSteam(string url) { return url.IndexOf("steamcommunity.com", StringComparison.OrdinalIgnoreCase) >= 0 || url.StartsWith("steam://", StringComparison.OrdinalIgnoreCase); }
        public static string MakeSlug(string name, IEnumerable<string> used)
        {
            var b = System.Text.RegularExpressions.Regex.Replace(name.Trim(), @"[^\w\-]+", "_").Trim('_');
            if (b == "") b = "server";
            var slug = b; int i = 2;
            while (used.Contains(slug, StringComparer.OrdinalIgnoreCase)) slug = b + "_" + (i++);
            return slug;
        }
    }

    static class ServerStore
    {
        static string FilePath { get { return Path.Combine(Util.BaseDataDir, "servers.json"); } }

        // Standard-Server (aus launcher.cfg) + vom Nutzer hinzugefuegte
        public static List<ServerEntry> Load(Dictionary<string, object> cfg)
        {
            var res = new List<ServerEntry>();
            if (!File.Exists(FilePath))
            {   // Erster Start: Vorgabe-Server aus launcher.cfg uebernehmen (danach ist alles frei aenderbar/loeschbar)
                var url = Util.S(cfg, "manifestUrl");
                if (url != "") res.Add(new ServerEntry { Name = Util.S(cfg, "title", "Very Far Away"), Url = url, Kind = "manifest", Slug = "" });
                try { Save(res, ""); } catch { }
                return res;
            }
            foreach (var d in Util.L2(Util.ReadJson(FilePath), "servers"))
            {
                var e = new ServerEntry { Name = Util.S(d, "name"), Url = Util.S(d, "url"), Kind = Util.S(d, "kind", "manifest"), Slug = Util.S(d, "slug"), Logo = Util.S(d, "logo"), LogoUrl = Util.S(d, "logoUrl"), Remote = Util.S(d, "remote").Equals("true", StringComparison.OrdinalIgnoreCase) };
                if (e.Name != "" && e.Url != "") res.Add(e);
            }
            return res;
        }
        public static string Selected() { return Util.S(Util.ReadJson(FilePath), "selected", ""); }
        public static void Save(List<ServerEntry> all, string selectedSlug)
        {
            var list = new List<object>();
            foreach (var e in all)
                list.Add(new Dictionary<string, object> { { "name", e.Name }, { "url", e.Url }, { "kind", e.Kind }, { "slug", e.Slug }, { "logo", e.Logo }, { "logoUrl", e.LogoUrl }, { "remote", e.Remote } });
            Util.WriteJson(FilePath, new Dictionary<string, object> { { "selected", selectedSlug }, { "servers", list } });
        }
    }

    // Dialog: Server hinzufuegen
    class AddServerForm : Form
    {
        protected override void OnLoad(EventArgs e) { base.OnLoad(e); Loc.Apply(this); }
        public TextBox tName, tUrl, tLogo;
        public AddServerForm()
        {
            Text = "Server hinzufuegen"; Width = 640; Height = 390; StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            Controls.Add(new Label { Text = "Name des Servers (z.B. Project Viewpoint):", AutoSize = true, Location = new Point(14, 14) });
            tName = new TextBox { Location = new Point(14, 36), Width = 596 };
            Controls.Add(new Label { Text = "Link:", AutoSize = true, Location = new Point(14, 70) });
            tUrl = new TextBox { Location = new Point(14, 92), Width = 596 };
            Controls.Add(new Label
            {
                Text = "Zwei Arten von Links:\r\n" +
                       "  - Steam-Workshop-Kollektion (steamcommunity.com/...): Der Launcher oeffnet die Kollektion in Steam,\r\n" +
                       "    dort abonnierst du alles auf einmal ('Alle abonnieren').\r\n" +
                       "  - Download-Link zu einer manifest.json (wie beim Very-Far-Away-Server): Der Launcher laedt\r\n" +
                       "    das Mod-Paket von dort und installiert es.",
                AutoSize = true, Location = new Point(14, 128)
            });
            Controls.Add(new Label { Text = "Logo-Link (Bild, optional - wird fuer alle Spieler angezeigt):", AutoSize = true, Location = new Point(14, 206) });
            tLogo = new TextBox { Location = new Point(14, 228), Width = 596 };
            Controls.Add(tLogo);
            var ok = new Button { Text = "Hinzufuegen", Location = new Point(400, 292), Width = 110, Height = 32, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Abbrechen", Location = new Point(520, 292), Width = 90, Height = 32, DialogResult = DialogResult.Cancel };
            Controls.AddRange(new Control[] { tName, tUrl, ok, cancel }); AcceptButton = ok; CancelButton = cancel;
        }
    }

    // ------------------------------------------------------------------ Sprache (DE / EN)
    // Alle Texte im Programm sind deutsch. Ist Englisch gewaehlt, werden Texte (Beschriftungen, Log, Meldungen) ueber diese
    // Phrasen-Tabelle uebersetzt (laengste Phrase zuerst). Der DEV-Tab bleibt deutsch.
    static class Loc
    {
        public static bool En;
        static string LangFile { get { return Path.Combine(Util.BaseDataDir, "lang.txt"); } }
        public static void Init()
        {
            string l = "";
            try { if (File.Exists(LangFile)) l = File.ReadAllText(LangFile).Trim().ToLowerInvariant(); } catch { }
            if (l == "") l = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "de" : "en";
            En = l == "en";
        }
        public static void Save(bool en) { try { File.WriteAllText(LangFile, en ? "en" : "de"); } catch { } }

        static List<KeyValuePair<string, string>> pairs;
        static readonly string[] P = {
            // --- Tabs, Hauptfenster
            "Info / Anleitung", "Info / Guide",
            "Spielen", "Play",
            "Lade Manifest ...", "Loading manifest ...",
            "Server-Adresse kopieren", "Copy server address",
            "Installieren / Aktualisieren", "Install / Update",
            "ZIP manuell waehlen ...", "Choose ZIP manually ...",
            "Abbrechen", "Cancel",
            "Mod-Einstellungen des Kurators uebernehmen (Backup wird angelegt)", "Apply the curator's mod settings (a backup is created)",
            "Download-ZIP nach der Installation behalten", "Keep the downloaded ZIP after installing",
            "RAM fuer das Spiel (GB, 0 = nicht aendern):", "RAM for the game (GB, 0 = don't change):",
            "Nur ZombieBuddy installieren ...", "Install ZombieBuddy only ...",
            "Server:", "Server:",
            "Bereit.", "Ready.",
            "  (Steam-Kollektion)", "  (Steam collection)",
            " (Steam-Kollektion)", " (Steam collection)",
            "Eigenes Logo entfernen", "Remove custom logo",
            "launcher_info.txt fehlt neben der EXE.", "launcher_info.txt is missing next to the EXE.",
            // --- Server hinzufuegen / entfernen (DEV)
            "Server hinzufuegen", "Add server",
            "+ Server hinzufuegen", "+ Add server",
            "Name des Servers (z.B. Project Viewpoint):", "Server name (e.g. Project Viewpoint):",
            "Link:", "Link:",
            "Zwei Arten von Links:\r\n", "Two kinds of links:\r\n",
            "  - Steam-Workshop-Kollektion (steamcommunity.com/...): Der Launcher oeffnet die Kollektion in Steam,\r\n", "  - Steam Workshop collection (steamcommunity.com/...): the launcher opens the collection in Steam,\r\n",
            "    dort abonnierst du alles auf einmal ('Alle abonnieren').\r\n", "    where you subscribe to everything at once ('Subscribe to all').\r\n",
            "  - Download-Link zu einer manifest.json (wie beim Very-Far-Away-Server): Der Launcher laedt\r\n", "  - Download link to a manifest.json (like the Very Far Away server): the launcher downloads\r\n",
            "    das Mod-Paket von dort und installiert es.", "    the mod package from there and installs it.",
            "Logo-Link (Bild, optional - wird fuer alle Spieler angezeigt):", "Logo link (image, optional - shown to all players):",
            "Hinzufuegen", "Add",
            "Bitte Name und Link eingeben.", "Please enter a name and a link.",
            "Der Link muss mit https:// beginnen.", "The link must start with https://.",
            "Einen Server mit diesem Namen gibt es schon.", "A server with this name already exists.",
            "Das ist kein Mod-Paket-Manifest (kein 'packUrl'/'version').", "This is not a mod package manifest (no 'packUrl'/'version').",
            "Der Link ist als Manifest nicht lesbar:\r\n", "The link cannot be read as a manifest:\r\n",
            "\r\n\r\nTrotzdem speichern?", "\r\n\r\nSave anyway?",
            "Server hinzugefuegt: ", "Server added: ",
            " (Steam-Kollektion - Spieler werden dorthin geleitet)", " (Steam collection - players are sent there)",
            " (Download-Link)", " (download link)",
            "Server entfernen", "Remove server",
            "' aus der Liste entfernen?\r\n(Bereits installierte Mods bleiben im Spielordner.)", "' from the list?\r\n(Mods that are already installed stay in the game folder.)",
            "Zuerst einen Server auswaehlen oder hinzufuegen.", "Please select or add a server first.",
            "Logo fuer '", "Logo for '",
            "' waehlen", "' - choose",
            "' gesetzt.", "' set.",
            "Das Bild konnte nicht geladen werden: ", "The image could not be loaded: ",
            "Eigenes Logo entfernt (es gilt wieder das Logo aus dem Manifest bzw. das Standard-Logo).", "Custom logo removed (the manifest logo or the default logo is used again).",
            "Serverliste vom Betreiber aktualisiert (", "Server list updated by the operator (",
            " Server).", " servers).",
            // --- Server-Wechsel, Manifest
            "Server gewaehlt: ", "Server selected: ",
            "Kollektion in Steam oeffnen", "Open collection in Steam",
            "Steam-Workshop-Kollektion: dort alles abonnieren, Steam laedt die Mods herunter.", "Steam Workshop collection: subscribe to everything there, Steam downloads the mods.",
            "Kollektion geoeffnet: ", "Collection opened: ",
            "In der Kollektion unten/oben auf 'Alle abonnieren' klicken, Steam laedt dann alle Mods herunter (Steam > Downloads).", "In the collection, click 'Subscribe to all' - Steam then downloads all mods (Steam > Downloads).",
            "Danach Mods im Spiel (Mods-Menue) bzw. ueber die Server-Mod-Liste aktivieren, ZombieBuddy bei Bedarf ueber 'Nur ZombieBuddy installieren ...'.", "Then enable the mods in the game (Mods menu) or via the server mod list; install ZombieBuddy if needed via 'Install ZombieBuddy only ...'.",
            "Kein Server vorhanden - oben auf '+ Server hinzufuegen' klicken und Name + Link eintragen.", "No server available - the operator has not configured one yet.",
            "FEHLER: ", "ERROR: ",
            "Fehler: ", "Error: ",
            "Manifest nicht erreichbar: ", "Manifest not reachable: ",
            "noch nicht installiert", "not installed yet",
            "neues Paket", "new package",
            " Mods kommen wieder aus dem Paket - Paket wird neu installiert.", " mods come from the package again - the package will be reinstalled.",
            " Mods kommen wieder aus dem Paket", " mods come from the package again",
            " Mods wechseln auf den Steam-Workshop", " mods switch to the Steam Workshop",
            "Aktuelles Paket: ", "Current package: ",
            "     Installiert: ", "     Installed: ",
            "   (aktuell)", "   (up to date)",
            "   -> Aktualisieren noetig: ", "   -> Update needed: ",
            "Neu installieren", "Reinstall",
            "Server: ", "Server: ",
            // --- Installation
            "Kein Download-Link im Manifest. Du kannst die ZIP auch manuell waehlen.", "No download link in the manifest. You can also choose the ZIP manually.",
            "Project Zomboid wurde nicht gefunden. Ist es ueber Steam installiert?", "Project Zomboid was not found. Is it installed via Steam?",
            "Project-Zomboid-Installation nicht gefunden.", "Project Zomboid installation not found.",
            "Zomboid-Ordner: ", "Zomboid folder: ",
            "Project Zomboid laeuft noch - bitte zuerst beenden.", "Project Zomboid is still running - please close it first.",
            "Paket ist aktuell - gleiche nur die Workshop-Mods ab (kein Download noetig).", "Package is up to date - only syncing the Workshop mods (no download needed).",
            "ZombieBuddy pruefen ...", "Checking ZombieBuddy ...",
            "Freier Speicher auf ", "Free space on ",
            "Paket bereits heruntergeladen, verwende Cache.", "Package already downloaded, using the cache.",
            "Lade Paket ", "Downloading package ",
            "Pruefe SHA256 ...", "Verifying SHA256 ...",
            "Pruefsumme stimmt nicht - Download beschaedigt oder Link veraltet. Bitte erneut versuchen.", "Checksum mismatch - download corrupted or link outdated. Please try again.",
            "Pruefsumme stimmt nicht", "Checksum mismatch",
            "ZombieBuddy einrichten ...", "Setting up ZombieBuddy ...",
            "WARNUNG - nicht im Paket enthalten (Server-Admin muss das Paket neu bauen): ", "WARNING - not in the package (the server admin must rebuild the package): ",
            "manuell ", "manual ",
            "Download-ZIP geloescht.", "Downloaded ZIP deleted.",
            "FERTIG! ", "DONE! ",
            " Mod-Ordner installiert.", " mod folders installed.",
            "Wichtig: Falls du die Very-Far-Away-Collection auf Steam abonniert hast, deabonniere sie bitte selbst", "Important: if you subscribed to the Very Far Away collection on Steam, please unsubscribe from it yourself",
            "(vorher gern als Backup in eine eigene Kollektion packen) - sonst kann der Server mit", "(feel free to save it as your own collection first) - otherwise the server may kick you with",
            "\"File doesn't match the one on the server\" kicken. Details im Tab 'Info / Anleitung'.", "\"File doesn't match the one on the server\". Details in the 'Info / Guide' tab.",
            " Mods laedt der Server ueber den Steam-Workshop.\r\n\r\n", " mods are loaded by the server through the Steam Workshop.\r\n\r\n",
            "Du musst dafuer NICHTS abonnieren: Beim Beitreten laedt Project Zomboid diese Mods automatisch ueber Steam herunter ", "You do NOT need to subscribe to anything: when joining, Project Zomboid downloads these mods automatically via Steam ",
            "(Steam muss laufen; beim ersten Mal bzw. nach Mod-Updates kann das ein paar Minuten dauern).\r\n\r\n", "(Steam must be running; the first time and after mod updates this can take a few minutes).\r\n\r\n",
            "Kommt beim Beitreten \"Workshop item version is different\" oder \"File doesn't match\": Spiel komplett beenden, ", "If you get \"Workshop item version is different\" or \"File doesn't match\" when joining: close the game completely, ",
            "Steam die Updates fertig laden lassen und neu verbinden.", "let Steam finish the updates and reconnect.",
            "Mods ueber den Steam-Workshop", "Mods via the Steam Workshop",
            "Spiel wird ueber Steam gestartet (Kollektion: Mods vorher dort abonnieren, im Spiel unter 'Mods' aktivieren) ...", "Starting the game via Steam (collection: subscribe to the mods there first, enable them in the game under 'Mods') ...",
            "Deine Installation passt nicht zum Server (", "Your installation does not match the server (",
            ").\r\nJetzt aktualisieren?", ").\r\nUpdate now?",
            "Aktualisieren noetig", "Update needed",
            "Server-Adresse in der Zwischenablage: ", "Server address in the clipboard: ",
            "Spiel wird ueber Steam gestartet ...", "Starting the game via Steam ...",
            " abgeschlossen.", " finished.",
            "Entferne ", "Removing ",
            " alte Mod-Ordner ...", " old mod folders ...",
            "Entpacke ", "Extracting ",
            " Mod-Ordner nach ", " mod folders to ",
            " Mod-Ordner aus Zomboid\\mods entfernt - diese Mods laedt das Spiel jetzt ueber den Steam-Workshop des Servers.", " mod folders removed from Zomboid\\mods - the game now loads these mods through the server's Steam Workshop.",
            "Keine Kurator-Konfiguration im Paket.", "No curator configuration in the package.",
            "Kurator-Einstellungen: ", "Curator settings: ",
            " Dateien nach ", " files to ",
            " kopiert", " copied",
            " (Backup von ", " (backup of ",
            "Hauptmenue-Mods: ", "Main menu mods: ",
            " steht schon in default.txt.", " is already in default.txt.",
            " fuers Hauptmenue eingetragen (Pferde-Animationen). Backup in _launcher_backup\\...\\mods.", " added for the main menu (horse animations). Backup in _launcher_backup\\...\\mods.",
            "WARNUNG - default.txt hat keinen mods-Block, Horse-Eintrag nicht gesetzt: ", "WARNING - default.txt has no mods block, Horse entry not set: ",
            "WARNUNG - default.txt (Horse): ", "WARNING - default.txt (Horse): ",
            "WARNUNG - ", "WARNING - ",
            "ZombieBuddy.jar oder zbNative.dll fehlt im Paket.", "ZombieBuddy.jar or zbNative.dll is missing in the package.",
            "ProjectZomboid64.json war schon passend.", "ProjectZomboid64.json was already correct.",
            "ProjectZomboid64.json nicht gefunden in ", "ProjectZomboid64.json not found in ",
            "ProjectZomboid64.json angepasst (", "ProjectZomboid64.json patched (",
            "zbNative.dll aus dem Ordner: ", "zbNative.dll from folder: ",
            "zbNative.dll (integriert, neben dem Launcher)", "zbNative.dll (built in, next to the launcher)",
            "zbNative.dll (im Launcher eingebettet)", "zbNative.dll (embedded in the launcher)",
            " nicht ladbar (", " cannot be loaded (",
            "), nehme die integrierte.", "), using the built-in one.",
            "Keine Java-Mods gefunden.", "No Java mods found.",
            "ZombieBuddy-Freigaben: ", "ZombieBuddy approvals: ",
            " Java-Mods geprueft, ", " Java mods checked, ",
            " neu freigegeben.", " newly approved.",
            "Berechne SHA256 ...", "Calculating SHA256 ...",
            // --- Verbindung / Download
            "Link liefert eine Webseite statt einer Datei", "Link returns a web page instead of a file",
            "Der Link liefert eine Webseite statt einer Datei. Bitte einen Direkt-Download-Link verwenden.", "The link returns a web page instead of a file. Please use a direct download link.",
            "Verbindung abgebrochen bei ", "Connection lost at ",
            "Download-Versuch ", "Download attempt ",
            " fehlgeschlagen: ", " failed: ",
            "Unerwartete Dateigroesse: ", "Unexpected file size: ",
            "Pruefe ", "Checking ",
            "unbekannter Fehler", "unknown error",
            "Launcher-Fehler (Details in VFALauncher_crash.log)", "Launcher error (details in VFALauncher_crash.log)",
            // --- Launcher-Update
            "Launcher-Update verfuegbar: v", "Launcher update available: v",
            " (du hast v", " (you have v",
            ") - hier klicken", ") - click here",
            "Launcher-Update gefunden: v", "Launcher update found: v",
            "Es ist kein Download-Link hinterlegt. Bitte die Workshop-Seite des Launchers oeffnen.", "No download link is set. Please open the launcher's page.",
            "Die neue Launcher-Version herunterladen, installieren und neu starten?\r\n(Server-Liste, Logos und launcher.cfg bleiben erhalten.)", "Download, install and restart with the new launcher version?\r\n(Server list, logos and launcher.cfg are kept.)",
            "Die heruntergeladene Datei ist keine gueltige Launcher-exe.", "The downloaded file is not a valid launcher exe.",
            "Update fehlgeschlagen: ", "Update failed: ",
            // --- Sprache
            "Sprache / Language", "Language",
            // --- ZombieBuddy-Fenster
            "ZombieBuddy installieren", "Install ZombieBuddy",
            "Version (GitHub-Releases von zed-0xff/ZombieBuddy):", "Version (GitHub releases of zed-0xff/ZombieBuddy):",
            "Versionen neu laden", "Reload versions",
            "Jar-Variante:", "Jar variant:",
            "Original (Jar + DLL aus dem gewaehlten GitHub-Release)", "Original (jar + DLL from the selected GitHub release)",
            "Compatibility-Fix (Workshop ", "Compatibility fix (Workshop ",
            "Lade Versionen ...", "Loading versions ...",
            " (Vorabversion)", " (pre-release)",
            " Version(en) mit Jar + DLL gefunden.", " version(s) with jar + DLL found.",
            "GitHub nicht erreichbar: ", "GitHub not reachable: ",
            "Keine Version gewaehlt (GitHub nicht erreichbar?).", "No version selected (GitHub not reachable?).",
            "Der Compatibility-Fix wurde nicht gefunden. Bitte Workshop-Item ", "The compatibility fix was not found. Please subscribe to Workshop item ",
            " in Steam abonnieren (oder den Mod 'ZombieBuddyFix' nach Zomboid\\mods legen) und erneut versuchen.", " in Steam (or put the mod 'ZombieBuddyFix' into Zomboid\\mods) and try again.",
            "Fix-Mod gefunden: ", "Fix mod found: ",
            "Im Fix-Mod liegt keine zbNative.dll und GitHub ist nicht erreichbar.", "The fix mod contains no zbNative.dll and GitHub is not reachable.",
            "zbNative.dll fehlt im Fix-Mod - lade die neueste vom GitHub ...", "zbNative.dll is missing in the fix mod - downloading the latest from GitHub ...",
            "Lade ", "Downloading ",
            "Unerwartete Dateigroessen (Jar ", "Unexpected file sizes (jar ",
            " Bytes) - abgebrochen.", " bytes) - aborted.",
            "=== FERTIG: ZombieBuddy (", "=== DONE: ZombieBuddy (",
            ") ist installiert ===", ") is installed ===",
            "ProjectZomboid64.json wurde mit -agentlib:zbNative gepatcht (Sicherung: ProjectZomboid64.json.vfa-backup). Damit startet der normale Steam-Start (Play) schon mit ZombieBuddy.", "ProjectZomboid64.json was patched with -agentlib:zbNative (backup: ProjectZomboid64.json.vfa-backup). The normal Steam start (Play) now already starts with ZombieBuddy.",
            "NOCH ZU TUN / WICHTIG:", "STILL TO DO / IMPORTANT:",
            "1) Steam-Startoptionen (nur noetig fuer den 'Alternate Launch' oder wenn du ZombieBuddy zusaetzlich ueber Steam erzwingen willst):", "1) Steam launch options (only needed for the 'Alternate Launch' or if you also want to force ZombieBuddy through Steam):",
            "   Steam > Bibliothek > Project Zomboid > Rechtsklick > Eigenschaften > Allgemein > Startoptionen, dort eintragen:", "   Steam > Library > Project Zomboid > right-click > Properties > General > Launch options, enter:",
            "   (Ohne Rueckfrage bei Java-Mods: -agentlib:zbNative=policy=allow-all --   |   nie neue Jars: -agentlib:zbNative=policy=deny-new --)", "   (Without asking for Java mods: -agentlib:zbNative=policy=allow-all --   |   never new jars: -agentlib:zbNative=policy=deny-new --)",
            "   Dieses Programm aendert Steams Startoptionen NICHT selbst (dafuer muesste Steam beendet werden).", "   This program does NOT change Steam's launch options itself (Steam would have to be closed for that).",
            "2) Mods mit Java-Teil (mod.info: javaJarFile) brauchen den Mod 'ZombieBuddy' in der Mod-Liste (require=\\ZombieBuddy).", "2) Mods with a Java part (mod.info: javaJarFile) need the mod 'ZombieBuddy' in the mod list (require=\\ZombieBuddy).",
            "3) Beim ersten Start fragt ZombieBuddy bei jedem Java-Mod nach Freigabe - 'Ja' und 'dauerhaft' waehlen (gespeichert in %USERPROFILE%\\.zombie_buddy\\mod_approvals.json).", "3) On first start ZombieBuddy asks for approval for every Java mod - choose 'Yes' and 'permanent' (stored in %USERPROFILE%\\.zombie_buddy\\mod_approvals.json).",
            "4) Nach einem Spiel-Update pruefen, ob ProjectZomboid64.json noch gepatcht ist (sonst hier erneut installieren).", "4) After a game update, check that ProjectZomboid64.json is still patched (otherwise install again here).",
            "5) Rueckgaengig: ProjectZomboid64.json.vfa-backup nach ProjectZomboid64.json zurueckkopieren; ZombieBuddy.jar und zbNative.dll im Spielordner loeschen.", "5) Undo: copy ProjectZomboid64.json.vfa-backup back to ProjectZomboid64.json; delete ZombieBuddy.jar and zbNative.dll in the game folder.",
            "Installieren", "Install",
            "Schliessen", "Close",
            "Neu laden", "Reload",
            "ZombieBuddy (Java-Mods) wurde erkannt, ist aber nicht eingerichtet.\r\nJetzt installieren? (neueste Version + B42-Fix)", "ZombieBuddy (Java mods) was detected but is not set up.\r\nInstall it now? (latest version + B42 fix)",
            "ZombieBuddy-Installation beendet - bitte 'Spielen' erneut klicken.", "ZombieBuddy installation finished - please click 'Play' again.",
            "Workshop-Seite des Fixes wird in Steam geoeffnet - bitte abonnieren, danach hier erneut 'Installieren' klicken.", "Opening the fix's Workshop page in Steam - please subscribe, then click 'Install' here again.",
        };

        static void Build()
        {
            pairs = new List<KeyValuePair<string, string>>();
            for (int i = 0; i + 1 < P.Length; i += 2) pairs.Add(new KeyValuePair<string, string>(P[i], P[i + 1]));
            pairs.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
        }

        public static string T(string s)
        {
            if (!En || string.IsNullOrEmpty(s)) return s;
            if (pairs == null) Build();
            foreach (var kv in pairs)
                if (s.IndexOf(kv.Key, StringComparison.Ordinal) >= 0) s = s.Replace(kv.Key, kv.Value);
            return s;
        }

        // Beschriftungen eines Fensters uebersetzen; spaetere Text-Aenderungen (Label.Text = ...) werden mituebersetzt
        static readonly HashSet<Control> hooked = new HashSet<Control>();
        public static void Apply(Control root)
        {
            if (!En) return;
            Hook(root);
            foreach (Control c in root.Controls) Apply(c);
        }
        static void Hook(Control c)
        {
            if (c is TextBox || c is RichTextBox || c is ComboBox || c is NumericUpDown) return;
            if (!hooked.Add(c)) return;
            var t = T(c.Text); if (t != c.Text) c.Text = t;
            c.TextChanged += (s, e) => { var x = T(c.Text); if (x != c.Text) c.Text = x; };
        }

        // MessageBox mit Uebersetzung
        public static DialogResult Show(string text, string caption) { return MessageBox.Show(T(text), T(caption)); }
        public static DialogResult Show(IWin32Window o, string text, string caption) { return MessageBox.Show(o, T(text), T(caption)); }
        public static DialogResult Show(IWin32Window o, string text, string caption, MessageBoxButtons b) { return MessageBox.Show(o, T(text), T(caption), b); }
        public static DialogResult Show(IWin32Window o, string text, string caption, MessageBoxButtons b, MessageBoxIcon i) { return MessageBox.Show(o, T(text), T(caption), b, i); }
        public static DialogResult Show(string text, string caption, MessageBoxButtons b, MessageBoxIcon i) { return MessageBox.Show(T(text), T(caption), b, i); }
    }

    // ------------------------------------------------------------------ Hilfen
    static class Util
    {
        public static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 200 };
        public const string UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

        public static string ExeDir { get { return AppDomain.CurrentDomain.BaseDirectory; } }
        // Server-Profil: "" = Standard-Server (alter Datenordner), sonst eigener Unterordner (eigener Zustand/Cache pro Server)
        public static string Profile = "";
        public static string BaseDataDir
        {
            get
            {
                var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VFALauncher");
                Directory.CreateDirectory(d);
                return d;
            }
        }
        public static string DataDir
        {
            get
            {
                var d = Profile == "" ? BaseDataDir : Path.Combine(BaseDataDir, "profiles", Profile);
                Directory.CreateDirectory(d);
                return d;
            }
        }
        public static string ZomboidDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Zomboid"); } }

        public static Dictionary<string, object> ReadJson(string path)
        {
            if (!File.Exists(path)) return new Dictionary<string, object>();
            var txt = File.ReadAllText(path, Encoding.UTF8).Trim();
            if (txt.Length > 0 && txt[0] == '\uFEFF') txt = txt.Substring(1);
            if (txt.Length == 0) return new Dictionary<string, object>();
            return Json.Deserialize<Dictionary<string, object>>(txt) ?? new Dictionary<string, object>();
        }
        public static void WriteJson(string path, object obj)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, Pretty(Json.Serialize(obj)), new UTF8Encoding(false));
        }
        public static string S(Dictionary<string, object> d, string key, string def = "")
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null) return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
            return def;
        }
        public static Dictionary<string, object> D(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v)) return v as Dictionary<string, object>;
            return null;
        }
        public static List<Dictionary<string, object>> L2(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || !(v is System.Collections.IEnumerable)) return new List<Dictionary<string, object>>();
            return ((System.Collections.IEnumerable)v).OfType<Dictionary<string, object>>().ToList();
        }

        public static List<string> L(Dictionary<string, object> d, string key)
        {
            object v;
            var res = new List<string>();
            if (d != null && d.TryGetValue(key, out v) && v is System.Collections.IEnumerable && !(v is string))
                foreach (var x in (System.Collections.IEnumerable)v) res.Add(Convert.ToString(x));
            return res;
        }

        // Simple JSON pretty printer (JavaScriptSerializer writes one line)
        public static string Pretty(string json)
        {
            var sb = new StringBuilder(); int ind = 0; bool q = false, esc = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (q)
                {
                    sb.Append(c);
                    if (esc) esc = false;
                    else if (c == '\\') esc = true;
                    else if (c == '"') q = false;
                    continue;
                }
                if (c == '"') { q = true; sb.Append(c); continue; }
                switch (c)
                {
                    case '{': case '[':
                        sb.Append(c); sb.Append('\n'); ind++; sb.Append(new string(' ', ind * 2)); break;
                    case '}': case ']':
                        sb.Append('\n'); ind--; sb.Append(new string(' ', ind * 2)); sb.Append(c); break;
                    case ',':
                        sb.Append(c); sb.Append('\n'); sb.Append(new string(' ', ind * 2)); break;
                    case ':':
                        sb.Append(": "); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        public static string Sha256File(string path, Action<long> progress = null)
        {
            using (var sha = SHA256.Create())
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            {
                var buf = new byte[1 << 20]; int n; long tot = 0;
                while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                {
                    sha.TransformBlock(buf, 0, n, null, 0); tot += n;
                    if (progress != null) progress(tot);
                }
                sha.TransformFinalBlock(buf, 0, 0);
                return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
            }
        }
        public static string Sha256String(string s)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-", "").ToLowerInvariant();
        }
        public static string Size(long b)
        {
            if (b > 1L << 30) return (b / (double)(1L << 30)).ToString("0.00") + " GB";
            if (b > 1L << 20) return (b / (double)(1L << 20)).ToString("0.0") + " MB";
            return (b / 1024.0).ToString("0") + " KB";
        }
        public static void CopyDir(string src, string dst, Action<string> onFile = null)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src))
            {
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
                if (onFile != null) onFile(f);
            }
            foreach (var d in Directory.GetDirectories(src))
                CopyDir(d, Path.Combine(dst, Path.GetFileName(d)), onFile);
        }
        public static void DeleteDir(string path)
        {
            if (!Directory.Exists(path)) return;
            foreach (var f in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(path, true);
        }
        public static List<string> ReadList(string path)
        {
            return File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList();
        }
    }

    // ------------------------------------------------------------------ Steam / PZ finden
    static class SteamInfo
    {
        public static string SteamPath()
        {
            foreach (var key in new[] { @"Software\Valve\Steam" })
            {
                using (var k = Registry.CurrentUser.OpenSubKey(key))
                {
                    var p = k == null ? null : k.GetValue("SteamPath") as string;
                    if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) return p.Replace('/', '\\');
                }
            }
            foreach (var key in new[] { @"SOFTWARE\WOW6432Node\Valve\Steam", @"SOFTWARE\Valve\Steam" })
            {
                using (var k = Registry.LocalMachine.OpenSubKey(key))
                {
                    var p = k == null ? null : k.GetValue("InstallPath") as string;
                    if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) return p;
                }
            }
            var def = @"C:\Program Files (x86)\Steam";
            return Directory.Exists(def) ? def : null;
        }

        public static List<string> Libraries()
        {
            var res = new List<string>();
            var steam = SteamPath();
            if (steam == null) return res;
            res.Add(steam);
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                {
                    var p = m.Groups[1].Value.Replace("\\\\", "\\");
                    if (Directory.Exists(p) && !res.Any(x => string.Equals(Path.GetFullPath(x).TrimEnd('\\'), Path.GetFullPath(p).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
                        res.Add(p);
                }
            }
            return res;
        }

        public static string PZInstallDir()
        {
            foreach (var lib in Libraries())
            {
                var p = Path.Combine(lib, "steamapps", "common", "ProjectZomboid");
                if (File.Exists(Path.Combine(p, "ProjectZomboid64.exe")) || File.Exists(Path.Combine(p, "ProjectZomboid64.json"))) return p;
            }
            return null;
        }

        public static string WorkshopDir()
        {
            string best = null; int bestCount = -1;
            foreach (var lib in Libraries())
            {
                var p = Path.Combine(lib, "steamapps", "workshop", "content", "108600");
                if (!Directory.Exists(p)) continue;
                int n = Directory.GetDirectories(p).Length;
                if (n > bestCount) { best = p; bestCount = n; }
            }
            return best;
        }
    }

    // ------------------------------------------------------------------ Downloads (direkt + gofile)
    class Downloader
    {
        public Action<string> Log = s => { };
        public Action<long, long> Progress = (a, b) => { };
        public string GofileSalt = "12af056dacea0b";
        public CancellationToken Cancel;

        static Downloader()
        {
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)12288; }
            catch { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; }
            ServicePointManager.DefaultConnectionLimit = 8;
        }

        public static string GetString(string url, Dictionary<string, string> headers = null, string method = "GET")
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.UserAgent = Util.UA;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            req.Timeout = 60000;
            if (headers != null) foreach (var h in headers) SetHeader(req, h.Key, h.Value);
            if (method == "POST") { req.ContentLength = 0; }
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }

        public static void GetFile(string url, string target)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = Util.UA; req.Timeout = 60000; req.AllowAutoRedirect = true;
            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                if ((resp.ContentType ?? "").StartsWith("text/html")) throw new Exception("Link liefert eine Webseite statt einer Datei");
                using (var fs = File.Create(target)) resp.GetResponseStream().CopyTo(fs);
            }
        }

        static void SetHeader(HttpWebRequest req, string k, string v)
        {
            switch (k.ToLowerInvariant())
            {
                case "user-agent": req.UserAgent = v; break;
                case "referer": req.Referer = v; break;
                case "accept": req.Accept = v; break;
                default: req.Headers[k] = v; break;
            }
        }

        // Wandelt bekannte Share-Links in direkte Links um
        public string Resolve(string url, out Dictionary<string, string> headers)
        {
            headers = new Dictionary<string, string>();
            var m = Regex.Match(url, @"gofile\.io/d/([A-Za-z0-9]+)");
            if (m.Success) return ResolveGofile(m.Groups[1].Value, headers);

            m = Regex.Match(url, @"swisstransfer\.com/d(?:l)?/([0-9a-fA-F-]{36})");
            if (m.Success) return ResolveSwissTransfer(m.Groups[1].Value);

            m = Regex.Match(url, @"pixeldrain\.com/u/([A-Za-z0-9]+)");
            if (m.Success) return "https://pixeldrain.com/api/file/" + m.Groups[1].Value + "?download";

            m = Regex.Match(url, @"drive\.google\.com/file/d/([A-Za-z0-9_-]+)");
            if (m.Success) return "https://drive.usercontent.google.com/download?id=" + m.Groups[1].Value + "&export=download&confirm=t";

            if (url.Contains("dropbox.com")) return Regex.Replace(url, @"([?&])dl=0", "$1dl=1") + (url.Contains("dl=") ? "" : (url.Contains("?") ? "&dl=1" : "?dl=1"));
            return url;
        }

        string ResolveSwissTransfer(string link)
        {
            Log("SwissTransfer: Link aufloesen ...");
            var html = GetString("https://www.swisstransfer.com/dl/" + link).Replace("\\\"", "\"").Replace("\\/", "/");
            var files = Regex.Matches(html, "\"id\":\"([0-9a-fA-F-]{36})\",\"path\":\"([^\"]+)\",\"size\":(\\d+)").Cast<Match>()
                .Select(x => new { Id = x.Groups[1].Value, Name = x.Groups[2].Value, Size = long.Parse(x.Groups[3].Value) }).ToList();
            if (files.Count == 0) throw new Exception("SwissTransfer: keine Datei gefunden (Link abgelaufen oder Download-Limit erreicht?)");
            var f = files.Where(x => x.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.Size).FirstOrDefault() ?? files.OrderByDescending(x => x.Size).First();
            Log("SwissTransfer: " + f.Name + " (" + Util.Size(f.Size) + ")");
            var json = GetString("https://www.swisstransfer.com/api/1/links/" + link + "/files/" + f.Id,
                new Dictionary<string, string> { { "Accept", "application/json" }, { "X-Requested-With", "XMLHttpRequest" }, { "Referer", "https://www.swisstransfer.com/dl/" + link } });
            var res = Util.Json.Deserialize<Dictionary<string, object>>(json);
            var url = Util.S(Util.D(res, "data"), "url");
            if (url == "") throw new Exception("SwissTransfer: keine Download-Adresse erhalten: " + json);
            return url;
        }

        string ResolveGofile(string code, Dictionary<string, string> headers)
        {
            Log("gofile: Gast-Token holen ...");
            var acc = Util.Json.Deserialize<Dictionary<string, object>>(GetString("https://api.gofile.io/accounts",
                new Dictionary<string, string> { { "Origin", "https://gofile.io" } }, "POST"));
            var token = Util.S(Util.D(acc, "data"), "token");
            if (token == "") throw new Exception("gofile: kein Token erhalten");

            Exception last = null;
            foreach (var off in new[] { 0, -1, 1 })
            {
                try
                {
                    long window = (long)Math.Floor((DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds / 14400.0) + off;
                    var wt = Util.Sha256String(Util.UA + "::en-US::" + token + "::" + window + "::" + GofileSalt);
                    var json = GetString("https://api.gofile.io/contents/" + code + "?contentFilter=&page=1&pageSize=1000&sortField=createTime&sortDirection=-1",
                        new Dictionary<string, string> {
                            { "Authorization", "Bearer " + token }, { "X-Website-Token", wt }, { "X-BL", "en-US" },
                            { "Accept", "*/*" }, { "Origin", "https://gofile.io" }, { "Referer", "https://gofile.io/" } });
                    var res = Util.Json.Deserialize<Dictionary<string, object>>(json);
                    if (Util.S(res, "status") != "ok") throw new Exception("gofile-Status: " + Util.S(res, "status"));
                    var data = Util.D(res, "data");
                    var files = new List<Dictionary<string, object>>();
                    if (Util.S(data, "type") == "file") files.Add(data);
                    var ch = Util.D(data, "children");
                    if (ch != null) foreach (var kv in ch) { var f = kv.Value as Dictionary<string, object>; if (f != null && Util.S(f, "type") == "file") files.Add(f); }
                    var pick = files.Where(f => Util.S(f, "name").EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                                    .OrderByDescending(f => long.Parse(Util.S(f, "size", "0"))).FirstOrDefault()
                               ?? files.OrderByDescending(f => long.Parse(Util.S(f, "size", "0"))).FirstOrDefault();
                    if (pick == null) throw new Exception("gofile: keine Datei im Ordner gefunden");
                    headers["Cookie"] = "accountToken=" + token;
                    headers["Referer"] = "https://gofile.io/";
                    Log("gofile: Datei " + Util.S(pick, "name") + " (" + Util.Size(long.Parse(Util.S(pick, "size", "0"))) + ")");
                    return Util.S(pick, "link");
                }
                catch (Exception e) { last = e; }
            }
            throw new Exception("gofile-Link konnte nicht aufgeloest werden (" + (last == null ? "" : last.Message) + "). " +
                                "Evtl. hat gofile seinen Schutz geaendert - DEV kann im Manifest 'gofileSalt' anpassen oder einen Direktlink nutzen.");
        }

        // Download mit Fortsetzen (Range), falls der Server es kann
        public void Download(string url, string target)
        {
            var part = target + ".part";
            for (int attempt = 1; attempt <= 8; attempt++)
            {
                Cancel.ThrowIfCancellationRequested();
                long have = File.Exists(part) ? new FileInfo(part).Length : 0;
                try
                {
                    Dictionary<string, string> headers;
                    var direct = Resolve(url, out headers);
                    var req = (HttpWebRequest)WebRequest.Create(direct);
                    req.UserAgent = Util.UA;
                    req.Timeout = 60000; req.ReadWriteTimeout = 120000;
                    req.AllowAutoRedirect = true;
                    foreach (var h in headers) SetHeader(req, h.Key, h.Value);
                    if (have > 0) req.AddRange(have);
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    {
                        if (resp.ContentType != null && resp.ContentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
                            throw new Exception("Der Link liefert eine Webseite statt einer Datei. Bitte einen Direkt-Download-Link verwenden.");
                        bool append = have > 0 && resp.StatusCode == HttpStatusCode.PartialContent;
                        if (!append) have = 0;
                        long total = resp.ContentLength > 0 ? resp.ContentLength + have : -1;
                        using (var rs = resp.GetResponseStream())
                        using (var fs = new FileStream(part, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                        {
                            var buf = new byte[1 << 20]; int n; long done = have; var sw = Stopwatch.StartNew();
                            while ((n = rs.Read(buf, 0, buf.Length)) > 0)
                            {
                                Cancel.ThrowIfCancellationRequested();
                                fs.Write(buf, 0, n); done += n;
                                if (sw.ElapsedMilliseconds > 250) { Progress(done, total); sw.Restart(); }
                            }
                            Progress(done, total);
                            if (total > 0 && done < total) throw new IOException("Verbindung abgebrochen bei " + Util.Size(done));
                        }
                    }
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(part, target);
                    return;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e)
                {
                    Log("Download-Versuch " + attempt + " fehlgeschlagen: " + e.Message);
                    if (attempt == 8) throw;
                    Thread.Sleep(3000 * attempt);
                }
            }
        }
    }

    // ------------------------------------------------------------------ Installation (Spieler)
    class Installer
    {
        static readonly char Sep = Path.DirectorySeparatorChar;
        public Action<string> Log = s => { };
        public Action<string, long, long> Progress = (t, a, b) => { };
        public CancellationToken Cancel;

        public string StatePath { get { return Path.Combine(Util.DataDir, "state.json"); } }

        public Dictionary<string, object> LoadState() { return Util.ReadJson(StatePath); }

        // ZIP entpacken: Ordner auf oberster Ebene -> Zomboid\mods, "_launcher" -> DataDir
        public List<string> InstallPack(string zipPath, bool applyCuratorConfig)
        {
            var modsDir = Path.Combine(Util.ZomboidDir, "mods");
            Directory.CreateDirectory(modsDir);
            var state = LoadState();
            var old = Util.L(state, "installedFolders");
            var launcherDir = Path.Combine(Util.DataDir, "pack_launcher");

            using (var zip = ZipFile.OpenRead(zipPath))
            {
                // Wurzel bestimmen (falls alles in einem Unterordner liegt, z.B. "MODS/")
                string prefix = "";
                var tops = zip.Entries.Select(e => e.FullName.Replace('\\', '/').Split('/')[0]).Distinct().ToList();
                if (tops.Count == 1 && zip.Entries.Any(e => e.FullName.Replace('\\', '/').StartsWith(tops[0] + "/_launcher/")))
                    prefix = tops[0] + "/";

                var newFolders = zip.Entries.Select(e => e.FullName.Replace('\\', '/'))
                    .Where(n => n.StartsWith(prefix) && n.Length > prefix.Length)
                    .Select(n => n.Substring(prefix.Length).Split('/'))
                    .Where(p => p.Length > 1 && p[0] != "_launcher")
                    .Select(p => p[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                // alte Pack-Ordner + Ordner, die neu kommen, entfernen (saubere Versionen)
                var remove = old.Concat(newFolders).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                Log("Entferne " + remove.Count + " alte Mod-Ordner ...");
                int i = 0;
                foreach (var f in remove)
                {
                    Cancel.ThrowIfCancellationRequested();
                    var p = Path.Combine(modsDir, f);
                    if (Directory.Exists(p)) Util.DeleteDir(p);
                    Progress("Aufraeumen", ++i, remove.Count);
                }
                Util.DeleteDir(launcherDir);

                Log("Entpacke " + newFolders.Count + " Mod-Ordner nach " + modsDir + " ...");
                var entries = zip.Entries.Where(e => e.FullName.Replace('\\', '/').StartsWith(prefix)).ToList();
                long total = entries.Sum(e => e.Length), done = 0;
                var modsFull = Path.GetFullPath(modsDir).TrimEnd(Sep) + Sep;
                var launchFull = Path.GetFullPath(launcherDir).TrimEnd(Sep) + Sep;
                foreach (var e in entries)
                {
                    Cancel.ThrowIfCancellationRequested();
                    var rel = e.FullName.Replace('\\', '/').Substring(prefix.Length);
                    if (rel.Length == 0) continue;
                    string dest;
                    if (rel.StartsWith("_launcher/")) dest = Path.GetFullPath(Path.Combine(launcherDir, rel.Substring(10).Replace('/', Sep)));
                    else dest = Path.GetFullPath(Path.Combine(modsDir, rel.Replace('/', Sep)));
                    if (!dest.StartsWith(modsFull, StringComparison.OrdinalIgnoreCase) && !dest.StartsWith(launchFull, StringComparison.OrdinalIgnoreCase))
                        continue; // Schutz gegen ../ im Archiv
                    if (rel.EndsWith("/")) { Directory.CreateDirectory(dest); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    e.ExtractToFile(dest, true);
                    done += e.Length;
                    Progress("Entpacken", done, total);
                }

                state["installedFolders"] = newFolders;
                state["removedForWorkshop"] = new List<string>();
                Util.WriteJson(StatePath, state);

                if (applyCuratorConfig) ApplyZomboidConfig(launcherDir);
                return newFolders;
            }
        }

        // Abgleich Manifest <-> PC: welche Ordner muessen weg (kommen jetzt ueber Workshop),
        // welche fehlen (kamen frueher ueber Workshop, jetzt wieder aus dem Paket)
        public void WorkshopDiff(Dictionary<string, object> wsBlock, out List<string> toRemove, out List<string> toRestore)
        {
            var set = new HashSet<string>(Util.L(wsBlock, "folders"), StringComparer.OrdinalIgnoreCase);
            var state = LoadState();
            var modsDir = Path.Combine(Util.ZomboidDir, "mods");
            toRemove = Util.L(state, "installedFolders").Where(f => set.Contains(f) && Directory.Exists(Path.Combine(modsDir, f))).ToList();
            toRestore = Util.L(state, "removedForWorkshop").Where(f => !set.Contains(f)).ToList();
        }

        // Java-Mods, die der Steam-Workshop geladen hat, in ZombieBuddy freigeben
        public void ApproveWorkshopJavaMods(List<string> wids)
        {
            var ws = SteamInfo.WorkshopDir();
            if (ws == null || wids.Count == 0) return;
            var dirs = wids.Select(w => Path.Combine(ws, w, "mods")).Where(Directory.Exists).SelectMany(Directory.GetDirectories).ToList();
            if (dirs.Count > 0) ApproveJavaModDirs(dirs, true);
        }

        // Mods, die laut Manifest ueber den Workshop kommen, aus Zomboid\mods entfernen (nur vom Launcher installierte Ordner)
        public int RemoveWorkshopFolders(Dictionary<string, object> wsBlock)
        {
            var folders = Util.L(wsBlock, "folders");
            if (folders.Count == 0) return 0;
            var modsDir = Path.Combine(Util.ZomboidDir, "mods");
            var state = LoadState();
            var installed = Util.L(state, "installedFolders");
            var set = new HashSet<string>(folders, StringComparer.OrdinalIgnoreCase);
            int n = 0;
            var removed = Util.L(state, "removedForWorkshop");
            foreach (var f in installed.Where(set.Contains).ToList())
            {
                var p = Path.Combine(modsDir, f);
                if (Directory.Exists(p)) { Util.DeleteDir(p); n++; }
                installed.Remove(f);
                if (!removed.Contains(f, StringComparer.OrdinalIgnoreCase)) removed.Add(f);
            }
            if (n > 0 || installed.Count != Util.L(state, "installedFolders").Count)
            {
                state["installedFolders"] = installed;
                state["removedForWorkshop"] = removed;
                Util.WriteJson(StatePath, state);
            }
            if (n > 0) Log(n + " Mod-Ordner aus Zomboid\\mods entfernt - diese Mods laedt das Spiel jetzt ueber den Steam-Workshop des Servers.");
            return n;
        }

        // Kurator-Konfiguration (Lua\*.ini, Sandbox Presets, ...) nach %UserProfile%\Zomboid kopieren - mit Backup
        public void ApplyZomboidConfig(string launcherDir)
        {
            var src = Path.Combine(launcherDir, "zomboid");
            if (!Directory.Exists(src)) { Log("Keine Kurator-Konfiguration im Paket."); return; }
            var backup = Path.Combine(Util.ZomboidDir, "_launcher_backup", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            int n = 0, b = 0;
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                var rel = f.Substring(src.Length).TrimStart(Sep);
                var dst = Path.Combine(Util.ZomboidDir, rel);
                if (File.Exists(dst))
                {
                    var bk = Path.Combine(backup, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(bk));
                    File.Copy(dst, bk, true); b++;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.Copy(f, dst, true); n++;
            }
            Log("Kurator-Einstellungen: " + n + " Dateien nach " + Util.ZomboidDir + " kopiert" + (b > 0 ? " (Backup von " + b + " Dateien: " + backup + ")" : "") + ".");
        }

        // Mods, die schon im Hauptmenue aktiv sein muessen (Pferde-Animationen werden beim Spielstart geladen,
        // ein Server-Beitritt ist zu spaet). Nur diese Mod-IDs werden in %UserProfile%\Zomboid\mods\default.txt eingetragen.
        static readonly string[] MenuMods = { "Horse" };

        public void EnsureMenuMods()
        {
            var packIds = Util.L(Util.ReadJson(Path.Combine(Util.DataDir, "pack_launcher", "pack.json")), "mods")
                .Select(m => m.TrimStart('\\').Trim()).ToList();
            var need = MenuMods.Where(m => packIds.Contains(m, StringComparer.OrdinalIgnoreCase)).ToList();
            if (need.Count == 0) return;

            var path = Path.Combine(Util.ZomboidDir, "mods", "default.txt");
            string txt = File.Exists(path) ? File.ReadAllText(path) : "VERSION = 1,\r\n\r\nmods\r\n{\r\n}\r\n\r\nmaps\r\n{\r\n}\r\n";
            string nl = txt.Contains("\r\n") ? "\r\n" : "\n";
            var block = Regex.Match(txt, @"(^|\n)([ \t]*mods[ \t]*\r?\n?[ \t]*\{)(.*?)(\r?\n[ \t]*\})", RegexOptions.Singleline);
            if (!block.Success) { Log("WARNUNG - default.txt hat keinen mods-Block, Horse-Eintrag nicht gesetzt: " + path); return; }

            var add = need.Where(m => !Regex.IsMatch(block.Groups[3].Value, @"^[ \t]*mod[ \t]*=[ \t]*" + Regex.Escape(m) + @"[ \t]*,", RegexOptions.Multiline | RegexOptions.IgnoreCase)).ToList();
            if (add.Count == 0) { Log("Hauptmenue-Mods: " + string.Join(", ", need) + " steht schon in default.txt."); return; }

            if (File.Exists(path))
            {
                var bk = Path.Combine(Util.ZomboidDir, "_launcher_backup", DateTime.Now.ToString("yyyyMMdd_HHmmss"), "mods", "default.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(bk));
                File.Copy(path, bk, true);
            }
            var g = block.Groups[4];
            var ins = string.Concat(add.Select(m => nl + "    mod = " + m + ","));
            txt = txt.Substring(0, g.Index) + ins + txt.Substring(g.Index);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, txt, new UTF8Encoding(false));
            Log("default.txt: " + string.Join(", ", add) + " fuers Hauptmenue eingetragen (Pferde-Animationen). Backup in _launcher_backup\\...\\mods.");
        }

        // ZombieBuddy: Jar + DLL ins Spielverzeichnis, ProjectZomboid64.json patchen, RAM setzen
        public void SetupZombieBuddy(string pzDir, int ramGb)
        {
            if (pzDir == null || !Directory.Exists(pzDir)) throw new Exception("Project-Zomboid-Installation nicht gefunden.");
            var modsDir = Path.Combine(Util.ZomboidDir, "mods");
            string jar = null;
            foreach (var cand in new[] { "ZombieBuddyFix", "ZombieBuddy" })
            {
                var p = Path.Combine(modsDir, cand, "libs", "ZombieBuddy.jar");
                if (File.Exists(p)) { jar = p; break; }
            }
            var dll = FindZbNative(modsDir);
            if (jar == null || dll == null) throw new Exception("ZombieBuddy.jar oder zbNative.dll fehlt im Paket.");

            foreach (var pair in new[] { new[] { jar, "ZombieBuddy.jar" }, new[] { dll, "zbNative.dll" } })
            {
                var dst = Path.Combine(pzDir, pair[1]);
                if (string.Equals(Path.GetFullPath(dst), Path.GetFullPath(pair[0]), StringComparison.OrdinalIgnoreCase)) continue;
                if (!File.Exists(dst) || Util.Sha256File(dst) != Util.Sha256File(pair[0]))
                {
                    File.Copy(pair[0], dst, true);
                    Log(pair[1] + " -> " + pzDir);
                }
            }
            var newJar = Path.Combine(pzDir, "ZombieBuddy.jar.new");
            if (File.Exists(newJar)) File.Delete(newJar); // sonst ersetzt zbNative die gepatchte Jar wieder

            PatchLauncherJson(pzDir, ramGb);
        }

        // Nur-ZombieBuddy-Installation (Dialog "ZombieBuddy installieren"): gewaehlte Jar + DLL ins Spielverzeichnis, JSON patchen
        public void InstallZbFiles(string pzDir, string jar, string dll, int ramGb)
        {
            if (pzDir == null || !Directory.Exists(pzDir)) throw new Exception("Project-Zomboid-Installation nicht gefunden.");
            foreach (var pair in new[] { new[] { jar, "ZombieBuddy.jar" }, new[] { dll, "zbNative.dll" } })
            {
                var dst = Path.Combine(pzDir, pair[1]);
                File.Copy(pair[0], dst, true);
                Log(pair[1] + " (" + new FileInfo(dst).Length + " Bytes, SHA256 " + Util.Sha256File(dst).Substring(0, 12) + "...) -> " + pzDir);
            }
            var newJar = Path.Combine(pzDir, "ZombieBuddy.jar.new");
            if (File.Exists(newJar)) File.Delete(newJar);
            if (!PatchLauncherJson(pzDir, ramGb)) Log("ProjectZomboid64.json war schon passend.");
        }

        // Vom DEV festgelegte zbNative.dll aus dem Manifest ("zombieBuddy": {version, dllUrl, dllSha256})
        public Dictionary<string, object> ZbPin;

        // Reihenfolge: 1) im Ordner (Paket / Mod-Ordner)  2) integriert (DEV-Vorgabe aus Manifest, DLL neben der exe, eingebettete DLL)
        public string FindZbNative(string modsDir)
        {
            foreach (var p in new[] {
                Path.Combine(Util.DataDir, "pack_launcher", "zbNative.dll"),
                Path.Combine(modsDir, "ZombieBuddyFix", "libs", "zbNative.dll"),
                Path.Combine(modsDir, "ZombieBuddy", "libs", "zbNative.dll") })
                if (File.Exists(p)) { Log("zbNative.dll aus dem Ordner: " + p); return p; }

            var url = Util.S(ZbPin, "dllUrl");
            if (url != "")
            {
                var ver = Util.S(ZbPin, "version", "pinned");
                var sha = Util.S(ZbPin, "dllSha256").ToLowerInvariant();
                var dst = Path.Combine(Util.DataDir, "zb", Regex.Replace(ver, @"[^\w.\-]", "_"), "zbNative.dll");
                try
                {
                    if (!File.Exists(dst) || (sha != "" && Util.Sha256File(dst) != sha))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(dst));
                        Downloader.GetFile(url, dst + ".tmp");
                        if (sha != "" && Util.Sha256File(dst + ".tmp") != sha) { File.Delete(dst + ".tmp"); throw new Exception("Pruefsumme stimmt nicht"); }
                        if (File.Exists(dst)) File.Delete(dst);
                        File.Move(dst + ".tmp", dst);
                    }
                    Log("zbNative.dll (DEV-Vorgabe " + ver + ")");
                    return dst;
                }
                catch (Exception ex) { Log("WARNUNG - zbNative.dll " + ver + " nicht ladbar (" + ex.Message + "), nehme die integrierte."); }
            }
            var side = Path.Combine(Util.ExeDir, "zombiebuddy", "zbNative.dll");
            if (File.Exists(side)) { Log("zbNative.dll (integriert, neben dem Launcher)"); return side; }
            using (var st = typeof(Installer).Assembly.GetManifestResourceStream("zbNative.dll"))
                if (st != null)
                {
                    var dst = Path.Combine(Util.DataDir, "zb", "embedded", "zbNative.dll");
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    using (var fs = File.Create(dst)) st.CopyTo(fs);
                    Log("zbNative.dll (im Launcher eingebettet)");
                    return dst;
                }
            return null;
        }

        public bool PatchLauncherJson(string pzDir, int ramGb)
        {
            var path = Path.Combine(pzDir, "ProjectZomboid64.json");
            if (!File.Exists(path)) throw new Exception("ProjectZomboid64.json nicht gefunden in " + pzDir);
            var cfg = Util.ReadJson(path);
            var args = Util.L(cfg, "vmArgs");
            var before = string.Join("|", args);
            args.RemoveAll(a => Regex.IsMatch(a, @"^-agentlib:zbNative(=.*)?$") || Regex.IsMatch(a, @"^-javaagent:.*ZombieBuddy\.jar"));
            args.Insert(0, "-agentlib:zbNative");
            if (ramGb > 0)
            {
                args.RemoveAll(a => a.StartsWith("-Xmx"));
                args.Insert(1, "-Xmx" + ramGb + "g");
            }
            if (string.Join("|", args) == before) return false;
            var bak = path + ".vfa-backup";
            if (!File.Exists(bak)) File.Copy(path, bak);
            cfg["vmArgs"] = args;
            Util.WriteJson(path, cfg);
            Log("ProjectZomboid64.json angepasst (-agentlib:zbNative" + (ramGb > 0 ? ", -Xmx" + ramGb + "g" : "") + ").");
            return true;
        }

        // Alle Java-Mods im Paket vorab in ZombieBuddy freigeben (mod_approvals.json)
        public void ApproveJavaMods(IEnumerable<string> folders)
        {
            var modsDir = Path.Combine(Util.ZomboidDir, "mods");
            ApproveJavaModDirs(folders.Select(f => Path.Combine(modsDir, f)), false);
        }

        public void ApproveJavaModDirs(IEnumerable<string> roots, bool quiet)
        {
            var found = new List<KeyValuePair<string, string>>(); // modId -> jarPath
            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (var info in Directory.GetFiles(root, "mod.info", SearchOption.AllDirectories))
                {
                    if (info.Contains(Sep + "41" + Sep)) continue;
                    string id = null, jarRel = null;
                    foreach (var line in File.ReadAllLines(info))
                    {
                        var t = line.Trim();
                        if (t.StartsWith("id=")) id = t.Substring(3).Trim();
                        else if (t.StartsWith("javaJarFile=")) jarRel = t.Substring(12).Trim();
                    }
                    if (id == null || string.IsNullOrEmpty(jarRel)) continue;
                    foreach (var rel in jarRel.Split(',', ';'))
                    {
                        var jp = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(info), rel.Trim().Replace('/', Sep)));
                        if (File.Exists(jp)) found.Add(new KeyValuePair<string, string>(id, jp));
                    }
                }
            }
            if (found.Count == 0) { if (!quiet) Log("Keine Java-Mods gefunden."); return; }

            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".zombie_buddy", "mod_approvals.json");
            var doc = Util.ReadJson(path);
            object modsObj; doc.TryGetValue("mods", out modsObj);
            int added = 0;
            var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            var hashes = found.Select(f => new KeyValuePair<string, string>(f.Key, Util.Sha256File(f.Value))).Distinct().ToList();

            if (modsObj is Dictionary<string, object>)
            {   // altes Format: id -> { sha -> bool }
                var mods = (Dictionary<string, object>)modsObj;
                foreach (var h in hashes)
                {
                    var entry = mods.ContainsKey(h.Key) ? mods[h.Key] as Dictionary<string, object> : null;
                    if (entry == null) { entry = new Dictionary<string, object>(); mods[h.Key] = entry; }
                    if (!entry.ContainsKey(h.Value)) { entry[h.Value] = true; added++; }
                }
            }
            else
            {   // Format v2: Liste von {id, jar_hash, decision}
                var list = new List<object>();
                if (modsObj is System.Collections.IEnumerable) foreach (var o in (System.Collections.IEnumerable)modsObj) list.Add(o);
                foreach (var h in hashes)
                {
                    bool exists = list.OfType<Dictionary<string, object>>().Any(o => Util.S(o, "id") == h.Key && Util.S(o, "jar_hash") == h.Value);
                    if (exists) continue;
                    list.Add(new Dictionary<string, object> { { "id", h.Key }, { "jar_hash", h.Value }, { "decision", true }, { "time", now } });
                    added++;
                }
                doc["mods"] = list;
                if (!doc.ContainsKey("formatVersion")) doc["formatVersion"] = 2;
            }
            Util.WriteJson(path, doc);
            Log("ZombieBuddy-Freigaben: " + hashes.Count + " Java-Mods geprueft, " + added + " neu freigegeben.");
        }
    }


    // ------------------------------------------------------------------ Pruefung (mod.info: id / require / incompatible)
    class ModInfo { public string Id, Name, Folder, Wid, Path; public List<string> Require = new List<string>(), Incompatible = new List<string>(); public bool JavaJar; }

    static class Checker
    {
        static readonly Regex VerDir = new Regex(@"^42(\.\d+)*$");

        static List<string> SplitIds(string v)
        {
            return v.Split(',', ';').Select(x => x.Trim().TrimStart('\\').Trim().Trim('\u200e', '\u200f')).Where(x => x.Length > 0).ToList();
        }

        // B42 liest die mod.info aus dem hoechsten passenden 42.x-Ordner, sonst 42, sonst common/Wurzel
        public static string PickModInfo(string modDir)
        {
            var cands = Directory.GetDirectories(modDir).Where(d => VerDir.IsMatch(Path.GetFileName(d)) && File.Exists(Path.Combine(d, "mod.info")))
                .OrderByDescending(d => Path.GetFileName(d).Split('.').Select(x => int.Parse(x)).Aggregate(0L, (a, x) => a * 1000 + x) * (long)Math.Pow(1000, 4 - Path.GetFileName(d).Split('.').Length))
                .ToList();
            if (cands.Count > 0) return Path.Combine(cands[0], "mod.info");
            foreach (var p in new[] { Path.Combine(modDir, "common", "mod.info"), Path.Combine(modDir, "mod.info") })
                if (File.Exists(p)) return p;
            return null;
        }

        public static ModInfo Parse(string path, string folder, string wid)
        {
            var m = new ModInfo { Path = path, Folder = folder, Wid = wid };
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var k = line.Substring(0, eq).Trim().ToLowerInvariant(); var v = line.Substring(eq + 1).Trim();
                if (k == "id") { while (v.StartsWith("id=")) v = v.Substring(3); m.Id = v.Trim('\u200e', '\u200f').Trim(); }
                else if (k == "name") m.Name = v;
                else if (k == "require") m.Require.AddRange(SplitIds(v));
                else if (k == "incompatible") m.Incompatible.AddRange(SplitIds(v));
                else if (k == "javajarfile" && v.Length > 0) m.JavaJar = true;
            }
            return m;
        }

        // mods: Liste (Ordnerpfad, Workshop-ID)
        public static List<ModInfo> Scan(IEnumerable<KeyValuePair<string, string>> modDirs)
        {
            var res = new List<ModInfo>();
            foreach (var kv in modDirs)
            {
                var p = PickModInfo(kv.Key);
                if (p == null) continue;
                var mi = Parse(p, Path.GetFileName(kv.Key), kv.Value);
                if (!string.IsNullOrEmpty(mi.Id)) res.Add(mi);
            }
            return res;
        }

        public static List<string> Report(List<ModInfo> all, List<string> enabled, List<string> maps, string modsRootForMaps)
        {
            var r = new List<string>();
            var byId = all.GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var en = new HashSet<string>(enabled, StringComparer.OrdinalIgnoreCase);
            int problems = 0;

            r.Add("PRUEFBERICHT " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            r.Add("Gefundene Mods (mod.info): " + all.Count + ", aktiviert (modid.txt): " + enabled.Count);
            r.Add("");

            r.Add("[1] Mod-IDs aus modid.txt, die in keinem Mod-Ordner existieren:");
            foreach (var id in enabled.Where(x => !byId.ContainsKey(x))) { r.Add("   FEHLT: " + id); problems++; }
            r.Add("");

            r.Add("[2] Fehlende Abhaengigkeiten (require=) aktivierter Mods:");
            foreach (var id in enabled.Where(byId.ContainsKey))
                foreach (var req in byId[id][0].Require.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (en.Contains(req)) continue;
                    var where = byId.ContainsKey(req) ? "vorhanden in Ordner '" + byId[req][0].Folder + "' (WID " + byId[req][0].Wid + "), aber NICHT aktiviert" : "NICHT VORHANDEN (Workshop-Item fehlt)";
                    r.Add("   " + id + " braucht " + req + " -> " + where); problems++;
                }
            r.Add("");

            r.Add("[3] Inkompatible aktivierte Mods (incompatible=):");
            foreach (var id in enabled.Where(byId.ContainsKey))
                foreach (var inc in byId[id][0].Incompatible)
                    if (en.Contains(inc)) { r.Add("   " + id + " ist inkompatibel mit " + inc); problems++; }
            r.Add("");

            r.Add("[4] Doppelte Mod-IDs in verschiedenen Ordnern (Server/Client koennen die falsche Version laden):");
            foreach (var g in byId.Where(g => g.Value.Count > 1 && en.Contains(g.Key)))
            { r.Add("   " + g.Key + ": " + string.Join(", ", g.Value.Select(m => m.Folder + " (WID " + m.Wid + ")"))); problems++; }
            r.Add("");

            r.Add("[5] Reihenfolge: Abhaengigkeit steht NACH dem Mod, der sie braucht:");
            var pos = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < enabled.Count; i++) if (!pos.ContainsKey(enabled[i])) pos[enabled[i]] = i;
            foreach (var id in enabled.Where(byId.ContainsKey))
                foreach (var req in byId[id][0].Require)
                    if (pos.ContainsKey(req) && pos[req] > pos[id]) r.Add("   Hinweis: " + id + " (#" + (pos[id] + 1) + ") vor " + req + " (#" + (pos[req] + 1) + ")");
            r.Add("");

            if (modsRootForMaps != null && maps != null)
            {
                r.Add("[6] Map-Ordner aus mapid.txt, die in keinem aktivierten Mod liegen:");
                var mapDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var id in enabled.Where(byId.ContainsKey))
                {
                    var modDir = Path.GetDirectoryName(byId[id][0].Path);
                    var top = byId[id][0].Path; // .../<Folder>/<42.x>/mod.info
                    var folderRoot = Directory.GetParent(modDir).FullName;
                    if (string.Equals(Path.GetFileName(modDir), byId[id][0].Folder, StringComparison.OrdinalIgnoreCase)) folderRoot = modDir;
                    foreach (var sub in new[] { "common", Path.GetFileName(modDir), "" })
                    {
                        var md = Path.Combine(folderRoot, sub, "media", "maps");
                        if (Directory.Exists(md)) foreach (var d in Directory.GetDirectories(md)) mapDirs.Add(Path.GetFileName(d));
                    }
                }
                foreach (var m in maps.Where(m => !m.Equals("Muldraugh, KY", StringComparison.OrdinalIgnoreCase) && !mapDirs.Contains(m))) { r.Add("   FEHLT: " + m); problems++; }
                foreach (var m in mapDirs.Where(m => !maps.Contains(m, StringComparer.OrdinalIgnoreCase))) r.Add("   Hinweis: Map-Ordner '" + m + "' ist in einem aktivierten Mod, steht aber nicht in mapid.txt");
                r.Add("");
            }

            r.Add("[7] Java-Mods (brauchen ZombieBuddy): " + string.Join(", ", all.Where(m => m.JavaJar && en.Contains(m.Id)).Select(m => m.Id)));
            r.Add("");
            r.Insert(2, problems == 0 ? "ERGEBNIS: keine Probleme gefunden." : "ERGEBNIS: " + problems + " Punkte pruefen (Details unten).");
            return r;
        }
    }

    // ------------------------------------------------------------------ Paket bauen (DEV)
    class PackBuilder
    {
        static readonly char Sep = Path.DirectorySeparatorChar;
        public Action<string> Log = s => { };
        public Action<string, long, long> Progress = (t, a, b) => { };
        public CancellationToken Cancel;
        public const string ZB_ORIGINAL = "3619862853";

        // Workshop-IDs, die der Server ueber WorkshopItems= laedt -> nicht ins Paket
        public HashSet<string> WorkshopOnly = new HashSet<string>();
        public const string ZB_FIX = "3809837933";

        // Weitere Workshop-Items: numerische Ordner im Workshop-Ordner mit Unterordner mods, die NICHT in der Packliste stehen
        public bool IncludeExtra = false;
        public static List<string> ExtraWids(string workshopDir, List<string> packWids)
        {
            var res = new List<string>();
            if (string.IsNullOrEmpty(workshopDir) || !Directory.Exists(workshopDir)) return res;
            var have = new HashSet<string>(packWids ?? new List<string>());
            foreach (var d in Directory.GetDirectories(workshopDir))
            {
                var n = Path.GetFileName(d);
                if (n.Length == 0 || !n.All(char.IsDigit) || have.Contains(n)) continue;
                if (Directory.Exists(Path.Combine(d, "mods"))) res.Add(n);
            }
            return res.OrderBy(x => x, StringComparer.Ordinal).ToList();
        }
        public static List<string> EffWids(string workshopDir, List<string> wids, bool inc)
        {
            return inc ? wids.Concat(ExtraWids(workshopDir, wids)).ToList() : wids;
        }
        // Mod-IDs der Extra-Items (aus mod.info) hinter die Liste haengen (ohne Duplikate)
        public static List<string> EffMods(string workshopDir, List<string> packWids, List<string> mods, bool inc)
        {
            if (!inc) return mods;
            var dirs = new List<KeyValuePair<string, string>>();
            foreach (var w in ExtraWids(workshopDir, packWids))
                foreach (var md in Directory.GetDirectories(Path.Combine(workshopDir, w, "mods"))) dirs.Add(new KeyValuePair<string, string>(md, w));
            var res = new List<string>(mods);
            var have = new HashSet<string>(mods.Select(m => m.TrimStart('\\').Trim()), StringComparer.OrdinalIgnoreCase);
            foreach (var mi in Checker.Scan(dirs)) if (have.Add(mi.Id)) res.Add(mi.Id);
            return res;
        }

        // Eigene Mods (Ordner "Eigene Mods"): Unterordner mit mod.info
        public static List<string> OwnModFolders(string eigeneModsDir)
        {
            if (string.IsNullOrEmpty(eigeneModsDir) || !Directory.Exists(eigeneModsDir)) return new List<string>();
            return Directory.GetDirectories(eigeneModsDir).Where(d => Checker.PickModInfo(d) != null).OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Mod-IDs der eigenen Mods fuer Mods= : VFA_Fixes zuerst, VFA_Deutsch zuletzt, dazwischen alphabetisch
        public static List<string> OwnModIds(string eigeneModsDir)
        {
            var ids = new List<string>();
            foreach (var d in OwnModFolders(eigeneModsDir))
            {
                var mi = Checker.Parse(Checker.PickModInfo(d), Path.GetFileName(d), "eigen");
                if (!string.IsNullOrEmpty(mi.Id) && !ids.Contains(mi.Id, StringComparer.OrdinalIgnoreCase)) ids.Add(mi.Id);
            }
            Func<string, int> rank = id => id.Equals("VFA_Fixes", StringComparison.OrdinalIgnoreCase) ? 0 : id.Equals("VFA_Deutsch", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
            return ids.OrderBy(rank).ThenBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public List<string> BuildModsFolder(string workshopDir, List<string> wids, string outDir, string zomboidCfgDir, string modIdFile, string mapFile, string steamcmd, string eigeneModsDir = null)
        {
            var packWidsOrig = new List<string>(wids);
            if (IncludeExtra)
            {
                var ex = ExtraWids(workshopDir, wids);
                Log("Weitere Workshop-Items (nicht in der Packliste) werden mitgepackt: " + ex.Count + (ex.Count > 0 ? " (" + string.Join(", ", ex) + ")" : ""));
                wids = wids.Concat(ex).ToList();
            }
            if (WorkshopOnly.Contains(ZB_FIX)) Log("HINWEIS - ZombieBuddy-Fix (" + ZB_FIX + ") bleibt trotz Haken im Paket (die Jar wird fuer die Einrichtung gebraucht).");
            var skipped = wids.Where(w => WorkshopOnly.Contains(w) && w != ZB_FIX).ToList();
            if (skipped.Count > 0) Log(skipped.Count + " Workshop-Items kommen ueber WorkshopItems= und werden nicht ins Paket gelegt.");
            wids = wids.Where(w => !skipped.Contains(w)).ToList();
            var missing = wids.Where(w => !Directory.Exists(Path.Combine(workshopDir, w, "mods"))).ToList();
            if (missing.Count > 0 && !string.IsNullOrEmpty(steamcmd) && File.Exists(steamcmd))
            {
                Log(missing.Count + " Items fehlen lokal - lade per SteamCMD ...");
                var scDir = Path.GetDirectoryName(steamcmd);
                foreach (var chunk in missing.Select((w, i) => new { w, i }).GroupBy(x => x.i / 25).Select(g => g.Select(x => x.w).ToList()))
                {
                    var args = "+login anonymous " + string.Join(" ", chunk.Select(w => "+workshop_download_item 108600 " + w)) + " +quit";
                    RunProcess(steamcmd, args, scDir);
                }
                var scWs = Path.Combine(scDir, "steamapps", "workshop", "content", "108600");
                foreach (var w in missing.ToList())
                {
                    if (Directory.Exists(Path.Combine(scWs, w, "mods")))
                    {
                        Util.CopyDir(Path.Combine(scWs, w), Path.Combine(workshopDir, w));
                        missing.Remove(w);
                    }
                }
            }
            if (missing.Count > 0) Log("WARNUNG - nicht gefunden (abonnieren oder SteamCMD angeben): " + string.Join(", ", missing));

            Directory.CreateDirectory(outDir);
            var launcherDir = Path.Combine(outDir, "_launcher");
            // alten Inhalt leeren (nur Mod-Ordner + _launcher)
            foreach (var d in Directory.GetDirectories(outDir)) Util.DeleteDir(d);

            var owner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var conflicts = new List<string>();
            int cnt = 0;
            foreach (var w in wids)
            {
                Cancel.ThrowIfCancellationRequested();
                Progress("Kopieren (" + w + ")", ++cnt, wids.Count);
                if (cnt % 25 == 0) Log("Kopiert: " + cnt + " / " + wids.Count + " Workshop-Items");
                var mods = Path.Combine(workshopDir, w, "mods");
                if (!Directory.Exists(mods)) continue;
                if (w == ZB_ORIGINAL)
                {   // nur die DLL aus dem Original - der Mod-Ordner kommt aus dem Patch (gleiche Mod-ID)
                    var dll = Path.Combine(mods, "ZombieBuddy", "libs", "zbNative.dll");
                    Directory.CreateDirectory(launcherDir);
                    if (File.Exists(dll)) File.Copy(dll, Path.Combine(launcherDir, "zbNative.dll"), true);
                    continue;
                }
                foreach (var md in Directory.GetDirectories(mods))
                {
                    var name = Path.GetFileName(md);
                    if (owner.ContainsKey(name)) { conflicts.Add(name + " (" + owner[name] + " und " + w + ")"); continue; }
                    owner[name] = w;
                    Util.CopyDir(md, Path.Combine(outDir, name));
                }
            }
            if (conflicts.Count > 0) Log("WARNUNG - gleiche Ordnernamen, erstes Item gewinnt: " + string.Join("; ", conflicts));

            // Eigene Mods (z.B. VFA_Fixes, VFA_Deutsch) 1:1 ins Paket
            var own = OwnModFolders(eigeneModsDir);
            if (own.Count == 0) Log("Eigene Mods: keine gefunden in '" + eigeneModsDir + "'.");
            foreach (var md in own)
            {
                Cancel.ThrowIfCancellationRequested();
                var name = Path.GetFileName(md);
                if (owner.ContainsKey(name))
                {
                    Log("WARNUNG - Eigener Mod '" + name + "' ersetzt den gleichnamigen Ordner aus Workshop-Item " + owner[name] + ".");
                    Util.DeleteDir(Path.Combine(outDir, name));
                }
                owner[name] = "eigen";
                Util.CopyDir(md, Path.Combine(outDir, name));
                Log("Eigener Mod kopiert: " + name);
            }

            var devDll = Path.Combine(Util.ExeDir, "zombiebuddy", "zbNative.dll");
            if (File.Exists(devDll)) { Directory.CreateDirectory(launcherDir); File.Copy(devDll, Path.Combine(launcherDir, "zbNative.dll"), true); Log("zbNative.dll aus Launcher\\zombiebuddy (DEV-Vorgabe) uebernommen."); }
            if (!File.Exists(Path.Combine(launcherDir, "zbNative.dll")))
            {   // DLL kommt aus dem Original-ZombieBuddy, auch wenn das nicht in der Packliste steht
                var dll = Path.Combine(workshopDir, ZB_ORIGINAL, "mods", "ZombieBuddy", "libs", "zbNative.dll");
                Directory.CreateDirectory(launcherDir);
                if (File.Exists(dll)) { File.Copy(dll, Path.Combine(launcherDir, "zbNative.dll"), true); Log("zbNative.dll aus ZombieBuddy (" + ZB_ORIGINAL + ") uebernommen."); }
                else Log("WARNUNG - zbNative.dll nicht gefunden (ZombieBuddy-Original " + ZB_ORIGINAL + " abonnieren).");
            }

            // Kurator-Konfiguration (Very Far Away - Zomboid Folder\Zomboid)
            if (!string.IsNullOrEmpty(zomboidCfgDir) && Directory.Exists(zomboidCfgDir))
            {
                Util.CopyDir(zomboidCfgDir, Path.Combine(launcherDir, "zomboid"));
                Log("Kurator-Konfiguration uebernommen aus " + zomboidCfgDir);
            }

            var pack = new Dictionary<string, object> {
                { "created", DateTime.Now.ToString("yyyy-MM-dd HH:mm") },
                { "folders", owner.Keys.OrderBy(x => x).ToList() },
                { "workshopIds", wids },
                { "mods", MergeModIds(EffMods(workshopDir, packWidsOrig, File.Exists(modIdFile) ? Util.ReadList(modIdFile) : new List<string>(), IncludeExtra), OwnModIds(eigeneModsDir)) },
                { "maps", File.Exists(mapFile) ? Util.ReadList(mapFile) : new List<string>() } };
            Util.WriteJson(Path.Combine(launcherDir, "pack.json"), pack);
            Log("MODS-Ordner fertig: " + owner.Count + " Mod-Ordner in " + outDir);
            return missing;
        }

        // Eigene Mod-IDs ans Ende anhaengen (ohne Duplikate; steht eine ID schon weiter vorne, wandert sie ans Ende)
        public static List<string> MergeModIds(List<string> mods, List<string> own)
        {
            Func<string, string> norm = m => m.TrimStart('\\').Trim();
            var ownSet = new HashSet<string>(own.Select(norm), StringComparer.OrdinalIgnoreCase);
            var res = mods.Where(m => !ownSet.Contains(norm(m))).ToList();
            res.AddRange(own);
            return res;
        }

        public List<string> CheckWorkshop(string workshopDir, List<string> wids, List<string> enabled, List<string> maps, string eigeneModsDir = null)
        {
            Log("Workshop-Ordner: " + workshopDir);
            Log("Packliste: " + wids.Count + " Workshop-IDs, modid.txt: " + enabled.Count + " Mod-IDs");
            if (string.IsNullOrEmpty(workshopDir) || !Directory.Exists(workshopDir))
                throw new Exception("Workshop-Ordner nicht gefunden: '" + workshopDir + "' - bitte im DEV-Tab den richtigen Ordner (...\\steamapps\\workshop\\content\\108600) waehlen.");
            var dirs = new List<KeyValuePair<string, string>>();
            var missing = new List<string>();
            foreach (var w in wids)
            {
                var mods = Path.Combine(workshopDir, w, "mods");
                if (!Directory.Exists(mods)) { missing.Add(w); continue; }
                foreach (var md in Directory.GetDirectories(mods)) dirs.Add(new KeyValuePair<string, string>(md, w));
            }
            var ownDirs = OwnModFolders(eigeneModsDir);
            foreach (var od in ownDirs) dirs.Add(new KeyValuePair<string, string>(od, "eigen"));
            Log("Pruefe " + dirs.Count + " Mod-Ordner ..." + (missing.Count > 0 ? " (" + missing.Count + " Workshop-IDs nicht im Ordner)" : "") + (ownDirs.Count > 0 ? " (davon " + ownDirs.Count + " eigene Mods, kein Fehler)" : ""));
            var all = Checker.Scan(dirs);
            Log(all.Count + " mod.info-Dateien gelesen.");
            var rep = Checker.Report(all, enabled, maps, workshopDir);
            rep.Insert(3, "Workshop-Ordner: " + workshopDir);
            if (ownDirs.Count > 0) rep.Insert(4, "Eigene Mods (werden nicht als Fehler gewertet, kommen ans Ende von Mods=): " + string.Join(", ", OwnModIds(eigeneModsDir)));
            rep.Insert(4, "Workshop-IDs in Packliste: " + wids.Count + ", davon NICHT heruntergeladen: " + missing.Count + (missing.Count > 0 ? " -> " + string.Join(", ", missing) : ""));
            return rep;
        }

        void RunProcess(string exe, string args, string dir)
        {
            var psi = new ProcessStartInfo(exe, args) { WorkingDirectory = dir, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            using (var p = Process.Start(psi))
            {
                p.OutputDataReceived += (s, e) => { if (e.Data != null && e.Data.Contains("Success")) Log("  " + e.Data.Trim()); };
                p.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Log("  " + e.Data.Trim()); };
                p.BeginOutputReadLine(); p.BeginErrorReadLine();
                p.WaitForExit();
            }
        }

        public void Zip(string modsDir, string zipPath)
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            var files = Directory.GetFiles(modsDir, "*", SearchOption.AllDirectories);
            long total = files.Sum(f => new FileInfo(f).Length), done = 0;
            using (var fs = new FileStream(zipPath, FileMode.Create))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (var f in files)
                {
                    Cancel.ThrowIfCancellationRequested();
                    var rel = f.Substring(modsDir.TrimEnd(Sep).Length + 1).Replace(Sep, '/');
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    var level = (ext == ".png" || ext == ".ogg" || ext == ".jar" || ext == ".pack" || ext == ".bank" || ext == ".zip") ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
                    zip.CreateEntryFromFile(f, rel, level);
                    done += new FileInfo(f).Length;
                    Progress("ZIP packen", done, total);
                }
            }
            Log("ZIP erstellt: " + zipPath + " (" + Util.Size(new FileInfo(zipPath).Length) + ")");
        }

        public static void UpdateServerIni(string iniPath, List<string> mods, List<string> maps, string workshopItems, List<string> ownMods = null)
        {
            if (ownMods != null) mods = MergeModIds(mods, ownMods);
            var lines = File.Exists(iniPath) ? File.ReadAllLines(iniPath).ToList() : new List<string>();
            File.Copy(iniPath, iniPath + "." + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bak", true);
            Action<string, string> set = (k, v) =>
            {
                int idx = lines.FindIndex(l => l.StartsWith(k + "="));
                if (idx >= 0) lines[idx] = k + "=" + v; else lines.Add(k + "=" + v);
            };
            set("Mods", string.Join(";", mods.Select(m => m.StartsWith("\\") ? m : "\\" + m)));
            set("Map", string.Join(";", maps));
            set("WorkshopItems", workshopItems ?? "");
            File.WriteAllLines(iniPath, lines);
        }
    }

    // ------------------------------------------------------------------ Oberflaeche
    class MainForm : Form
    {
        readonly Dictionary<string, object> cfg;
        Dictionary<string, object> manifest;
        readonly bool devMode;

        TextBox log; ProgressBar bar; Label status, lblVersion, lblServer; Button btnInstall, btnPlay, btnCancel, btnManual, btnDelSrv; PictureBox headerPic; Image defaultLogo; LinkLabel lblUpdate, lblCredit; TextBox tLauncherWs; ComboBox cbServer; List<ServerEntry> servers; ServerEntry curServer; bool switching; static readonly ServerEntry NoServer = new ServerEntry { Name = "", Url = "", Slug = "_leer" };
        CheckBox chkConfig, chkKeepZip; NumericUpDown numRam;
        CancellationTokenSource cts;
        Color accent = Color.FromArgb(178, 34, 34);
        static readonly Color BgDark = Color.FromArgb(22, 22, 26), BgPanel = Color.FromArgb(32, 32, 38), BgInput = Color.FromArgb(14, 14, 17),
                              FgText = Color.FromArgb(230, 230, 230), FgDim = Color.FromArgb(160, 160, 168), BtnDark = Color.FromArgb(48, 48, 56), Lime = Color.FromArgb(150, 200, 40);
        readonly List<Panel> pages = new List<Panel>(); readonly List<Button> navButtons = new List<Button>();

        public MainForm(bool dev)
        {
            devMode = dev;
            cfg = Util.ReadJson(Path.Combine(Util.ExeDir, "launcher.cfg"));
            servers = ServerStore.Load(cfg);
            curServer = servers.FirstOrDefault(x => x.Slug == ServerStore.Selected()) ?? (servers.Count > 0 ? servers[0] : NoServer);
            Util.Profile = curServer.Slug;
            Text = "majestiK Launcher" + (dev ? "  [DEV]" : "");
            Width = 960; Height = 1040; MinimumSize = new Size(900, 760); StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgDark; ForeColor = FgText;
            Font = new Font("Segoe UI", 9.5f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            // Logo oben
            var header = new PictureBox { Dock = DockStyle.Top, Height = 250, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
            headerPic = header;
            try
            {
                var custom = Path.Combine(Util.ExeDir, "logo.png");
                if (File.Exists(custom)) defaultLogo = LoadImageNoLock(custom);
                else using (var st = typeof(MainForm).Assembly.GetManifestResourceStream("logo.jpg")) if (st != null) defaultLogo = new Bitmap(st);
            }
            catch { }
            Resize += (s, e) => FitLogo();
            // eigene, dunkle Tab-Leiste
            var nav = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, BackColor = BgPanel, Padding = new Padding(8, 6, 8, 0) };
            var content = new Panel { Dock = DockStyle.Fill, BackColor = BgDark };
            AddPage(nav, content, "Spielen", BuildPlayTab());
            AddPage(nav, content, "Info / Anleitung", BuildInfoTab());
            if (dev) AddPage(nav, content, "DEV", BuildDevTab());
            var cbLang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Margin = new Padding(40, 2, 0, 0) };
            cbLang.Items.AddRange(new object[] { "Deutsch", "English" });
            cbLang.SelectedIndex = Loc.En ? 1 : 0;
            cbLang.SelectedIndexChanged += (s, e) => ChangeLang(cbLang.SelectedIndex == 1);
            nav.Controls.Add(cbLang);
            ShowPage(0);
            // gemeinsamer Log-Bereich unten (fuer Spielen- UND DEV-Tab)
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 240, Padding = new Padding(8), BackColor = BgPanel };
            log = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, Dock = DockStyle.Fill, Font = new Font("Consolas", 9f), BackColor = BgInput, ForeColor = FgText, BorderStyle = BorderStyle.FixedSingle };
            status = new Label { Dock = DockStyle.Top, Height = 22, Text = "Bereit.", ForeColor = FgDim };
            bar = new ProgressBar { Dock = DockStyle.Top, Height = 18 };
            bottom.Controls.Add(log); bottom.Controls.Add(status); bottom.Controls.Add(bar);
            Controls.Add(content);
            Controls.Add(bottom);
            Controls.Add(nav);
            Controls.Add(header);
            var credit = new Panel { Dock = DockStyle.Bottom, Height = 22, BackColor = BgDark };
            lblCredit = new LinkLabel
            {
                Text = "", AutoSize = false, Dock = DockStyle.Right, Width = 380,
                TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI", 8f), ForeColor = FgDim, LinkColor = FgDim, ActiveLinkColor = Lime, VisitedLinkColor = FgDim, BackColor = BgDark
            };
            lblCredit.LinkClicked += (s, e) => { try { Process.Start(Convert.ToString(e.Link.LinkData)); } catch { } };
            UpdateCredit();
            lblUpdate = new LinkLabel
            {
                Text = "", AutoSize = false, Dock = DockStyle.Fill, Visible = false, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                LinkColor = Lime, ActiveLinkColor = Color.White, VisitedLinkColor = Lime, BackColor = BgDark
            };
            lblUpdate.LinkClicked += (s, e) => InstallLauncherUpdate();
            credit.Controls.Add(lblUpdate); credit.Controls.Add(lblCredit);
            Controls.Add(credit);
            try { var old = Path.Combine(Util.ExeDir, "majestiKLauncher.exe.old"); if (File.Exists(old)) File.Delete(old); } catch { }
            ApplyTheme(this);
            Loc.Apply(this);
            ApplyLogo();
            Shown += (s, e) =>
            {
                ApplyDevFields(); RefreshManifest(); CheckLauncherUpdate(); if (!devMode) SyncServerList();
                ThreadPool.QueueUserWorkItem(_ => { try { if (!ZbInstalled() && ZbDetected()) BeginInvoke((Action)(() => OfferZombieBuddy())); } catch { } });
                updTimer = new System.Windows.Forms.Timer { Interval = 30 * 60 * 1000 };   // Update-Pruefung auch bei laenger offenem Launcher
                updTimer.Tick += (s2, e2) => { if (!lblUpdate.Visible) CheckLauncherUpdate(); };
                updTimer.Start();
            };
        }

        void ChangeLang(bool en)
        {
            if (en == Loc.En) return;
            Loc.Save(en);
            if (MessageBox.Show(this, "Launcher neu starten, damit die Sprache wechselt?\r\nRestart the launcher to change the language?", "Sprache / Language", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            try { Process.Start(Application.ExecutablePath, devMode ? "--dev" : ""); Application.Exit(); } catch { }
        }

        void AddPage(FlowLayoutPanel nav, Panel content, string title, Panel page)
        {
            page.Dock = DockStyle.Fill; page.BackColor = BgDark; page.Visible = false;
            content.Controls.Add(page); pages.Add(page);
            int idx = pages.Count - 1;
            var b = new Button { Text = title, AutoSize = true, Height = 34, Padding = new Padding(10, 0, 10, 0), FlatStyle = FlatStyle.Flat, Tag = "nav", Margin = new Padding(0, 0, 6, 0), Cursor = Cursors.Hand };
            b.FlatAppearance.BorderSize = 0;
            b.Click += (s, e) => ShowPage(idx);
            nav.Controls.Add(b); navButtons.Add(b);
        }

        void ShowPage(int idx)
        {
            for (int i = 0; i < pages.Count; i++)
            {
                pages[i].Visible = i == idx;
                navButtons[i].BackColor = i == idx ? accent : BtnDark;
                navButtons[i].ForeColor = Color.White;
            }
        }

        public void ApplyTheme(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is Button)
                {
                    var b = (Button)c;
                    if (Equals(b.Tag, "nav")) { }
                    else if (b.BackColor == accent) { b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = Color.FromArgb(120, 20, 20); b.ForeColor = Color.White; }
                    else { b.FlatStyle = FlatStyle.Flat; b.BackColor = BtnDark; b.ForeColor = FgText; b.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 80); }
                    b.Cursor = Cursors.Hand;
                }
                else if (c is TextBox) { c.BackColor = BgInput; c.ForeColor = FgText; ((TextBox)c).BorderStyle = BorderStyle.FixedSingle; }
                else if (c is NumericUpDown) { c.BackColor = BgInput; c.ForeColor = FgText; }
                else if (c is CheckBox) { c.ForeColor = FgText; c.BackColor = Color.Transparent; }
                else if (c is Label) { if (c.ForeColor != accent && c.ForeColor != Color.DarkGreen && c.ForeColor != Lime) c.ForeColor = FgText; }
                if (c.HasChildren) ApplyTheme(c);
            }
        }

        // ---------- Tab Spielen
        Panel BuildPlayTab()
        {
            var p = new Panel();
            lblVersion = new Label { AutoSize = true, Location = new Point(20, 16), Text = "Lade Manifest ...", Font = new Font("Segoe UI", 10.5f, FontStyle.Bold) };
            lblServer = new Label { AutoSize = true, Location = new Point(20, 42), Text = "" };
            var btnCopy = new Button { Text = "Server-Adresse kopieren", Location = new Point(560, 36), Width = 190, Height = 30 };
            btnCopy.Click += (s, e) => { var a = ServerAddress(); if (a != "") { Clipboard.SetText(a); SetStatus("Kopiert: " + a); } };

            btnInstall = new Button { Text = "Installieren / Aktualisieren", Location = new Point(20, 80), Width = 240, Height = 44, BackColor = accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnPlay = new Button { Text = "Spielen", Location = new Point(270, 80), Width = 160, Height = 44 };
            btnCancel = new Button { Text = "Abbrechen", Location = new Point(440, 80), Width = 110, Height = 44, Enabled = false };
            btnManual = new Button { Text = "ZIP manuell waehlen ...", Location = new Point(560, 80), Width = 190, Height = 44 };
            btnInstall.Click += (s, e) => RunInstall(null);
            btnManual.Click += (s, e) =>
            {
                using (var d = new OpenFileDialog { Filter = "Mod-Paket (*.zip)|*.zip" })
                    if (d.ShowDialog(this) == DialogResult.OK) RunInstall(d.FileName);
            };
            btnPlay.Click += (s, e) => Play();
            btnCancel.Click += (s, e) => { if (cts != null) cts.Cancel(); };

            chkConfig = new CheckBox { Text = "Mod-Einstellungen des Kurators uebernehmen (Backup wird angelegt)", Checked = true, AutoSize = true, Location = new Point(20, 140) };
            chkKeepZip = new CheckBox { Text = "Download-ZIP nach der Installation behalten", Checked = false, AutoSize = true, Location = new Point(20, 166) };
            var lblRam = new Label { Text = "RAM fuer das Spiel (GB, 0 = nicht aendern):", AutoSize = true, Location = new Point(480, 142) };
            numRam = new NumericUpDown { Minimum = 0, Maximum = 64, Value = Math.Max(0, Math.Min(64, int.Parse(Util.S(LoadPrefs(), "ramGb", "12")))), Location = new Point(760, 138), Width = 60 };


            var btnZb = new Button { Text = "Nur ZombieBuddy installieren ...", Location = new Point(20, 200), Width = 240, Height = 34 };
            btnZb.Click += (s, e) => { using (var f = new ZbForm((int)numRam.Value)) f.ShowDialog(this); };
            p.Controls.AddRange(new Control[] { lblVersion, lblServer, btnCopy, btnInstall, btnPlay, btnCancel, btnManual, chkConfig, chkKeepZip, lblRam, numRam, btnZb });
            foreach (Control c in p.Controls) c.Top += 46;   // Platz fuer die Server-Leiste oben

            var lblSrv = new Label { Text = "Server:", AutoSize = true, Location = new Point(20, 16), Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
            cbServer = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(86, 12), Width = 380 };
            foreach (var sv in servers) cbServer.Items.Add(sv);
            if (cbServer.Items.Count > 0) cbServer.SelectedItem = curServer;
            var btnAddSrv = new Button { Text = "+ Server hinzufuegen", Location = new Point(480, 10), Width = 170, Height = 30 };
            btnDelSrv = new Button { Text = "Entfernen", Location = new Point(660, 10), Width = 100, Height = 30, Enabled = curServer.Slug != "" };
            cbServer.SelectedIndexChanged += (s, e) => { if (!switching && cbServer.SelectedItem is ServerEntry) SwitchServer((ServerEntry)cbServer.SelectedItem); };
            btnAddSrv.Click += (s, e) => AddServer();
            btnDelSrv.Click += (s, e) => RemoveServer();
            var btnLogo = new Button { Text = "Logo ...", Location = new Point(770, 10), Width = 80, Height = 30 };
            var btnLogoX = new Button { Text = "X", Location = new Point(856, 10), Width = 30, Height = 30 };
            new ToolTip().SetToolTip(btnLogoX, "Eigenes Logo entfernen");
            btnLogo.Click += (s, e) => ChooseLogo();
            btnLogoX.Click += (s, e) => ClearLogo();
            btnAddSrv.Visible = btnDelSrv.Visible = btnLogo.Visible = btnLogoX.Visible = devMode;   // Server verwalten + Logo aendern nur im DEV-Modus
            if (!devMode) cbServer.Width = 600;
            p.Controls.AddRange(new Control[] { lblSrv, cbServer, btnAddSrv, btnDelSrv, btnLogo, btnLogoX });
            return p;
        }

        Panel BuildInfoTab()
        {
            var p = new Panel { Padding = new Padding(10) };
            var tb = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5f), BackColor = Color.White };
            var path = Path.Combine(Util.ExeDir, "launcher_info.txt");
            if (Loc.En && File.Exists(Path.Combine(Util.ExeDir, "launcher_info_en.txt"))) path = Path.Combine(Util.ExeDir, "launcher_info_en.txt");
            tb.Text = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n").Replace("\n", "\r\n") : "launcher_info.txt fehlt neben der EXE.";
            p.Controls.Add(tb);
            return p;
        }

        // ---------- Tab DEV
        CheckBox chkExtra; TextBox tLogoUrl, tWs, tList, tOut, tCfg, tModIds, tMaps, tSteamCmd, tZip, tVersion, tUrl, tSha, tIp, tPort, tPw, tNews, tSalt, tManifestUrl, tZbRepo, tZbTag, tZbVer, tZbUrl, tZbSha;
        Panel BuildDevTab()
        {
            var p = new Panel { AutoScroll = true };
            int y = 10;
            Func<string, string, bool, TextBox> row = (label, val, browse) =>
            {
                p.Controls.Add(new Label { Text = label, Location = new Point(10, y + 4), Width = 190 });
                var t = new TextBox { Text = val, Location = new Point(200, y), Width = browse ? 560 : 640 };
                p.Controls.Add(t);
                if (browse)
                {
                    var b = new Button { Text = "...", Location = new Point(765, y - 1), Width = 75 };
                    b.Click += (s, e) =>
                    {
                        if (label.Contains("Ordner"))
                        {
                            using (var d = new FolderBrowserDialog { SelectedPath = t.Text }) if (d.ShowDialog(this) == DialogResult.OK) t.Text = d.SelectedPath;
                        }
                        else using (var d = new OpenFileDialog { FileName = t.Text }) if (d.ShowDialog(this) == DialogResult.OK) t.Text = d.FileName;
                    };
                    p.Controls.Add(b);
                }
                y += 30;
                return t;
            };
            p.Controls.Add(new Label { Text = "Server (oben im Tab 'Spielen' waehlen): alle Einstellungen hier gelten fuer den gewaehlten Server.", ForeColor = Lime, Location = new Point(10, y), AutoSize = true }); y += 26;
            p.Controls.Add(new Label { Text = "1) MODS-Ordner bauen", Font = new Font(Font, FontStyle.Bold), Location = new Point(10, y), AutoSize = true }); y += 26;
            tWs = row("Workshop-Ordner (108600)", DevVal("workshopDir"), true);
            tList = row("Packliste (Workshop-IDs)", DevVal("packList"), true);
            tModIds = row("modid.txt", DevVal("modIds"), true);
            tMaps = row("mapid.txt", DevVal("maps"), true);
            tCfg = row("Kurator-Zomboid-Ordner", DevVal("zomboidCfg"), true);
            tOut = row("Ziel MODS-Ordner", DevVal("modsOut"), true);
            tSteamCmd = row("steamcmd.exe (optional)", DevVal("steamcmd"), true);
            chkExtra = new CheckBox { Text = "Weitere Workshop-Items (nicht in der Packliste) mitpacken + aktivieren (hinter modid.txt, vor eigenen Mods)", Checked = DevVal("extraWs") == "1", AutoSize = true, Location = new Point(200, y) };
            p.Controls.Add(chkExtra); y += 28;
            var b1 = new Button { Text = "MODS-Ordner bauen", Location = new Point(200, y), Width = 200 };
            b1.Click += (s, e) => DevBuild(); p.Controls.Add(b1);
            var b0 = new Button { Text = "Nur pruefen (Abhaengigkeiten)", Location = new Point(410, y), Width = 240 };
            b0.Click += (s, e) => DevCheck(); p.Controls.Add(b0); y += 40;

            p.Controls.Add(new Label { Text = "2) ZIP packen und hochladen", Font = new Font(Font, FontStyle.Bold), Location = new Point(10, y), AutoSize = true }); y += 26;
            tZip = row("ZIP-Datei", DevVal("zipOut"), true);
            var b2 = new Button { Text = "ZIP packen + SHA256", Location = new Point(200, y), Width = 200 };
            b2.Click += (s, e) => DevZip(); p.Controls.Add(b2); y += 40;

            p.Controls.Add(new Label { Text = "3) Manifest (das ist die Datei hinter manifestUrl - hier traegst du den neuen Link ein)", Font = new Font(Font, FontStyle.Bold), Location = new Point(10, y), AutoSize = true }); y += 26;
            tManifestUrl = row("Manifest-Link (dieser Server)", curServer.Url, false);
            tLauncherWs = row("Launcher-Workshop-Link (alle Server)", LauncherWsUrl, false);
            var m = manifest ?? new Dictionary<string, object>();
            tVersion = row("Version", DateTime.Now.ToString("yyyy.MM.dd-HHmm"), false);
            tUrl = row("Download-Link (gofile/direkt)", "", false);
            tSha = row("SHA256 (optional)", "", false);
            tIp = row("Server-IP / Domain", "", false);
            tPort = row("Server-Port", "16261", false);
            tPw = row("Server-Passwort (optional)", "", false);
            tNews = row("Nachricht an Spieler", "", false);
            tLogoUrl = row("Logo-Link (Bild, fuer alle Spieler)", "", false);
            tSalt = row("gofile-Salt (nur bei Problemen)", "12af056dacea0b", false);
            var b3 = new Button { Text = "manifest.json speichern", Location = new Point(200, y), Width = 200 };
            var b4 = new Button { Text = "In Zwischenablage", Location = new Point(410, y), Width = 160 };
            var b5 = new Button { Text = "DEV-Einstellungen speichern", Location = new Point(580, y), Width = 180 };
            b3.Click += (s, e) => { var path = ManifestSavePath(); Util.WriteJson(path, DevManifest()); SetStatus("Gespeichert: " + path); };
            b4.Click += (s, e) => { Clipboard.SetText(Util.Pretty(Util.Json.Serialize(DevManifest()))); SetStatus("Manifest in der Zwischenablage."); };
            b5.Click += (s, e) => DevSaveCfg();
            p.Controls.AddRange(new Control[] { b3, b4, b5 }); y += 40;

            p.Controls.Add(new Label { Text = "4) Server", Font = new Font(Font, FontStyle.Bold), Location = new Point(10, y), AutoSize = true }); y += 26;
            var b6 = new Button { Text = "Server-INI aktualisieren (Mods=, Map=) ...", Location = new Point(200, y), Width = 320 };
            b6.Click += (s, e) => DevServerIni(); p.Controls.Add(b6);
            var b9 = new Button { Text = "Mod-Liste / Workshop-Auswahl ...", Location = new Point(530, y), Width = 260 };
            b9.Click += (s, e) => DevModList(); p.Controls.Add(b9); y += 34;
            var b10 = new Button { Text = "Serverliste fuer Spieler speichern (servers.json) ...", Location = new Point(200, y), Width = 320 };
            b10.Click += (s, e) => DevExportServers(); p.Controls.Add(b10);
            var b11 = new Button { Text = "Spieler-Paket erstellen (ZIP) ...", Location = new Point(530, y), Width = 260 };
            b11.Click += (s, e) => DevBuildPlayerPack(); p.Controls.Add(b11); y += 34;
            lblWs = new Label { Location = new Point(200, y), AutoSize = true }; p.Controls.Add(lblWs); y += 30;

            p.Controls.Add(new Label { Text = "5) ZombieBuddy - zbNative.dll (wird ueber das Manifest an alle verteilt)", Font = new Font(Font, FontStyle.Bold), Location = new Point(10, y), AutoSize = true }); y += 26;
            tZbRepo = row("GitHub-Repo", DevVal("zbRepo"), false);
            tZbTag = row("Release-Tag (leer = neuestes)", DevVal("zbTag"), false);
            tZbVer = row("Vorgabe: Version", "", false);
            tZbUrl = row("Vorgabe: DLL-Link", "", false);
            tZbSha = row("Vorgabe: SHA256", "", false);
            var b7 = new Button { Text = "zbNative.dll von GitHub holen", Location = new Point(200, y), Width = 240 };
            var b8 = new Button { Text = "Vorgabe entfernen", Location = new Point(450, y), Width = 160 };
            b7.Click += (s, e) => DevZbUpdate();
            b8.Click += (s, e) => { tZbVer.Text = tZbUrl.Text = tZbSha.Text = ""; SetStatus("ZombieBuddy-Vorgabe geleert - manifest.json speichern und hochladen."); };
            p.Controls.AddRange(new Control[] { b7, b8 }); y += 40;

            var help = new Label
            {
                Location = new Point(10, y), Width = 840, Height = 120,
                Text = "Ablauf fuer ein Update: 1) MODS-Ordner bauen  2) ZIP packen  3) ZIP hochladen (gofile, eigener Server, ...)\r\n" +
                       "4) Link + neue Version oben eintragen -> manifest.json speichern -> Datei an die Adresse aus manifestUrl hochladen\r\n" +
                       "   (z.B. GitHub Gist 'raw'-Link ohne Commit-Hash, oder eigener Webserver). Alle Launcher sehen die neue Version beim naechsten Start.\r\n" +
                       "launcher.cfg (mit manifestUrl) gehoert neben die Launcher.exe, die du an die Spieler gibst. Den Ordner 'dev' brauchen Spieler nicht."
            };
            p.Controls.Add(help);
            if (manifest != null) FillDevFromManifest();
            LoadWsSelection();
            return p;
        }

        void FillDevFromManifest()
        {
            if (!devMode || manifest == null || tUrl == null) return;
            tUrl.Text = Util.S(manifest, "packUrl", tUrl.Text);
            tSha.Text = Util.S(manifest, "packSha256", tSha.Text);
            var sv = Util.D(manifest, "server");
            if (sv != null) { tIp.Text = Util.S(sv, "ip"); tPort.Text = Util.S(sv, "port", "16261"); tPw.Text = Util.S(sv, "password"); }
            tNews.Text = Util.S(manifest, "news", tNews.Text);
            tLogoUrl.Text = Util.S(manifest, "logoUrl", tLogoUrl.Text);
            tSalt.Text = Util.S(manifest, "gofileSalt", tSalt.Text);
            if (!File.Exists(WsFile)) LoadWsSelection();
            var zb = Util.D(manifest, "zombieBuddy");
            if (zb != null) { tZbVer.Text = Util.S(zb, "version"); tZbUrl.Text = Util.S(zb, "dllUrl"); tZbSha.Text = Util.S(zb, "dllSha256"); }
        }

        void DevZbUpdate()
        {
            var repo = tZbRepo.Text.Trim(); var tag = tZbTag.Text.Trim(); var modsOut = tOut.Text.Trim();
            RunTask("ZombieBuddy", ct =>
            {
                AppendLog("GitHub: suche Releases von " + repo + " ...");
                var json = Downloader.GetString("https://api.github.com/repos/" + repo + "/releases?per_page=30",
                    new Dictionary<string, string> { { "Accept", "application/vnd.github+json" } });
                var rels = Util.Json.Deserialize<List<object>>(json).Cast<Dictionary<string, object>>();
                // "Latest" ist bei ZombieBuddy oft der Windows-Installer -> neuestes Release nehmen, das eine zbNative.dll hat
                string ver = null, url = null;
                foreach (var r in rels)
                {
                    if (r.ContainsKey("draft") && Equals(r["draft"], true)) continue;
                    if (tag != "" && Util.S(r, "tag_name") != tag) continue;
                    if (tag == "" && Equals(r.ContainsKey("prerelease") ? r["prerelease"] : false, true)) continue;
                    var a = Util.L2(r, "assets").FirstOrDefault(x => Util.S(x, "name").Equals("zbNative.dll", StringComparison.OrdinalIgnoreCase));
                    if (a == null) continue;
                    ver = Util.S(r, "tag_name"); url = Util.S(a, "browser_download_url"); break;
                }
                if (url == null) throw new Exception(tag != "" ? "Release '" + tag + "' mit zbNative.dll nicht gefunden." : "Kein Release mit zbNative.dll gefunden.");
                var dir = Path.Combine(Util.ExeDir, "zombiebuddy");
                Directory.CreateDirectory(dir);
                var dst = Path.Combine(dir, "zbNative.dll");
                Downloader.GetFile(url, dst + ".tmp");
                var bytes = new FileInfo(dst + ".tmp").Length;
                if (bytes < 1024 || bytes > 5 * 1024 * 1024) { File.Delete(dst + ".tmp"); throw new Exception("Unerwartete Dateigroesse: " + bytes + " Bytes"); }
                if (File.Exists(dst)) File.Delete(dst);
                File.Move(dst + ".tmp", dst);
                File.WriteAllText(Path.Combine(dir, "version.txt"), ver + "\r\n" + url + "\r\n");
                var sha = Util.Sha256File(dst);
                AppendLog("zbNative.dll " + ver + " -> " + dst + "  (SHA256 " + sha + ")");
                if (modsOut != "" && Directory.Exists(modsOut))
                {
                    var ld = Path.Combine(modsOut, "_launcher"); Directory.CreateDirectory(ld);
                    File.Copy(dst, Path.Combine(ld, "zbNative.dll"), true);
                    AppendLog("auch nach " + ld + " kopiert (kommt beim naechsten ZIP mit).");
                }
                BeginInvoke((Action)(() => { tZbVer.Text = ver; tZbUrl.Text = url; tZbSha.Text = sha; }));
                AppendLog("Jetzt unter 3) 'manifest.json speichern' und das Manifest hochladen - dann nutzen alle Spieler diese DLL.");
            });
        }

        Dictionary<string, object> DevManifest()
        {
            return new Dictionary<string, object> {
                { "version", tVersion.Text.Trim() },
                { "packUrl", tUrl.Text.Trim() },
                { "packSha256", tSha.Text.Trim() },
                { "news", tNews.Text.Trim() },
                { "logoUrl", tLogoUrl.Text.Trim() },
                { "gofileSalt", tSalt.Text.Trim() },
                { "workshopItems", new Dictionary<string, object> { { "wids", wsWids.ToArray() }, { "folders", WsFolders().ToArray() } } },
                { "zombieBuddy", new Dictionary<string, object> { { "version", tZbVer.Text.Trim() }, { "dllUrl", tZbUrl.Text.Trim() }, { "dllSha256", tZbSha.Text.Trim().ToLowerInvariant() } } },
                { "server", new Dictionary<string, object> { { "ip", tIp.Text.Trim() }, { "port", tPort.Text.Trim() }, { "password", tPw.Text.Trim() } } } };
        }

        void DevSaveCfg()
        {
            var d = new Dictionary<string, object> {
                { "workshopDir", tWs.Text }, { "packList", tList.Text }, { "modIds", tModIds.Text }, { "maps", tMaps.Text },
                { "extraWs", chkExtra.Checked ? "1" : "0" }, { "zomboidCfg", tCfg.Text }, { "modsOut", tOut.Text }, { "eigeneMods", EigeneModsDir() }, { "steamcmd", tSteamCmd.Text }, { "zipOut", tZip.Text },
                { "zbRepo", tZbRepo.Text.Trim() }, { "zbTag", tZbTag.Text.Trim() } };
            Util.WriteJson(DevFile(curServer.Slug), d);
            // Manifest-Link des Servers aktualisieren (Dropdown-Eintrag)
            var nu = tManifestUrl.Text.Trim();
            if (curServer != NoServer && nu != "" && nu != curServer.Url)
            {
                curServer.Url = nu; ServerStore.Save(servers, curServer.Slug); RefreshManifest();
            }
            // Launcher-Workshop-Link kommt in die launcher.cfg (wird mit dem Launcher im Workshop-Paket ausgeliefert)
            var c = Util.ReadJson(Path.Combine(Util.ExeDir, "launcher.cfg"));
            c["launcherWorkshopUrl"] = tLauncherWs.Text.Trim();
            Util.WriteJson(Path.Combine(Util.ExeDir, "launcher.cfg"), c);
            UpdateCredit();
            SetStatus("DEV-Einstellungen fuer '" + curServer.Name + "' gespeichert: " + DevFile(curServer.Slug) + " (Launcher-Link in launcher.cfg)");
        }

        // ---------- DEV-Einstellungen pro Server
        string DevSlug { get { return curServer != null ? curServer.Slug : ""; } }
        string DevFile(string slug) { return Path.Combine(Util.ExeDir, "dev", "srv_" + (slug == "" ? "default" : slug) + ".json"); }
        string DevDirPath() { return DevSlug == "" ? Path.Combine(Util.ExeDir, "dev") : Path.Combine(Util.ExeDir, "dev", DevSlug); }
        string ManifestSavePath() { return DevSlug == "" ? Path.Combine(Util.ExeDir, "manifest.json") : Path.Combine(DevDirPath(), "manifest.json"); }
        Dictionary<string, object> DevDict()
        {
            var f = DevFile(DevSlug);
            if (File.Exists(f)) return Util.ReadJson(f);
            if (DevSlug == "") return Util.D(cfg, "dev") ?? new Dictionary<string, object>();   // alte Einstellungen aus launcher.cfg
            return new Dictionary<string, object>();
        }
        string DevVal(string key) { return Util.S(DevDict(), key, DevDef(key)); }
        string DevDef(string key)
        {
            var root = Path.GetFullPath(Path.Combine(Util.ExeDir, ".."));
            bool def = DevSlug == "";
            var dd = DevDirPath();
            switch (key)
            {
                case "workshopDir": return SteamInfo.WorkshopDir() ?? "";
                case "packList": return Path.Combine(dd, "packliste_workshopids.txt");
                case "modIds": return Path.Combine(dd, "modid.txt");
                case "maps": return Path.Combine(dd, "mapid.txt");
                case "zomboidCfg": return def ? Path.Combine(root, "Very Far Away - Zomboid Folder", "Zomboid") : "";
                case "modsOut": return Path.Combine(root, def ? "MODS" : "MODS_" + DevSlug);
                case "steamcmd": return File.Exists(@"G:\steamcmd\steamcmd.exe") ? @"G:\steamcmd\steamcmd.exe" : "";
                case "zipOut": return Path.Combine(root, def ? "VeryFarAway_Pack.zip" : DevSlug + "_Pack.zip");
                case "zbRepo": return "zed-0xff/ZombieBuddy";
                case "eigeneMods": return Path.Combine(root, def ? "Eigene Mods" : "Eigene Mods - " + DevSlug);
                case "extraWs": return "0";
            }
            return "";
        }

        // Felder des DEV-Tabs auf den gewaehlten Server umstellen
        void ApplyDevFields()
        {
            if (!devMode || tWs == null) return;
            try
            {
                tWs.Text = DevVal("workshopDir"); tList.Text = DevVal("packList"); tModIds.Text = DevVal("modIds"); tMaps.Text = DevVal("maps");
                tCfg.Text = DevVal("zomboidCfg"); tOut.Text = DevVal("modsOut"); tSteamCmd.Text = DevVal("steamcmd"); tZip.Text = DevVal("zipOut");
                tZbRepo.Text = DevVal("zbRepo"); tZbTag.Text = DevVal("zbTag"); chkExtra.Checked = DevVal("extraWs") == "1";
                tManifestUrl.Text = curServer.Url;
                tVersion.Text = DateTime.Now.ToString("yyyy.MM.dd-HHmm"); tUrl.Text = ""; tSha.Text = ""; tIp.Text = ""; tPort.Text = "16261"; tPw.Text = ""; tNews.Text = ""; tLogoUrl.Text = "";
                tZbVer.Text = ""; tZbUrl.Text = ""; tZbSha.Text = "";
                if (DevSlug != "" && DevSlug != "_leer")
                {   // neuer Server: leere Listen anlegen, damit Pruefen/Bauen nicht an fehlenden Dateien scheitert
                    Directory.CreateDirectory(DevDirPath());
                    foreach (var f in new[] { tList.Text, tModIds.Text, tMaps.Text }) if (f != "" && !File.Exists(f)) { Directory.CreateDirectory(Path.GetDirectoryName(f)); File.WriteAllText(f, ""); }
                }
                wsWids = new List<string>(); LoadWsSelection();
            }
            catch (Exception ex) { AppendLog("WARNUNG - DEV-Felder: " + ex.Message); }
        }

        // Ordner mit eigenen Mods: launcher.cfg -> dev -> eigeneMods, sonst <Wurzel>\Eigene Mods
        string EigeneModsDir()
        {
            return DevVal("eigeneMods");
        }

        void DevBuild()
        {
            string ws = tWs.Text, list = tList.Text, outDir = tOut.Text, cfgDir = tCfg.Text, modIds = tModIds.Text, maps = tMaps.Text, sc = tSteamCmd.Text, own = EigeneModsDir();
            var selWs = wsWids.ToList(); bool inc = chkExtra.Checked;
            RunTask("MODS-Ordner bauen", ct =>
            {
                var b = new PackBuilder { Log = AppendLog, Progress = SetProgress, Cancel = ct, WorkshopOnly = new HashSet<string>(selWs), IncludeExtra = inc };
                var wids = Util.ReadList(list); var wids0 = wids;
                var missing = b.BuildModsFolder(ws, wids, outDir, cfgDir, modIds, maps, sc, own);
                AppendLog(missing.Count == 0 ? "Alle " + wids.Count + " Workshop-Items vorhanden." : missing.Count + " Items fehlen (siehe oben).");
                var rep = b.CheckWorkshop(ws, PackBuilder.EffWids(ws, wids0, inc), PackBuilder.EffMods(ws, wids0, Util.ReadList(modIds), inc), File.Exists(maps) ? Util.ReadList(maps) : null, own);
                var path = Path.Combine(Path.GetDirectoryName(outDir.TrimEnd('\\')), "pruefbericht.txt");
                File.WriteAllLines(path, rep, Encoding.UTF8);
                AppendLog(rep[2]);
                AppendLog("Pruefbericht: " + path);
            });
        }

        void DevCheck()
        {
            string ws = tWs.Text, list = tList.Text, outDir = tOut.Text, modIds = tModIds.Text, maps = tMaps.Text, own = EigeneModsDir(); bool inc = chkExtra.Checked;
            RunTask("Pruefen", ct =>
            {
                var b = new PackBuilder { Log = AppendLog, Progress = SetProgress, Cancel = ct };
                var w0 = Util.ReadList(list);
                var rep = b.CheckWorkshop(ws, PackBuilder.EffWids(ws, w0, inc), PackBuilder.EffMods(ws, w0, Util.ReadList(modIds), inc), File.Exists(maps) ? Util.ReadList(maps) : null, own);
                var path = Path.Combine(Path.GetDirectoryName(outDir.TrimEnd('\\')), "pruefbericht.txt");
                File.WriteAllLines(path, rep, Encoding.UTF8);
                foreach (var l in rep.Take(60)) AppendLog(l);
                AppendLog("Kompletter Bericht: " + path);
                BeginInvoke((Action)(() => Loc.Show(this, rep[2] + "\r\n\r\nBericht: " + path, "Pruefung fertig")));
            });
        }

        void DevZip()
        {
            string outDir = tOut.Text, zipPath = tZip.Text;
            RunTask("ZIP packen", ct =>
            {
                var b = new PackBuilder { Log = AppendLog, Progress = SetProgress, Cancel = ct };
                b.Zip(outDir, zipPath);
                AppendLog("Berechne SHA256 ...");
                var len = new FileInfo(zipPath).Length;
                var sha = Util.Sha256File(zipPath, d => SetProgress("SHA256", d, len));
                Invoke((Action)(() => { tSha.Text = sha; tVersion.Text = DateTime.Now.ToString("yyyy.MM.dd-HHmm"); }));
                AppendLog("SHA256: " + sha);
                AppendLog("Jetzt die ZIP hochladen, Link unter 'Download-Link' eintragen, manifest.json speichern und hochladen.");
            });
        }

        string WsFile { get { return Path.Combine(DevDirPath(), "workshop_items.txt"); } }
        List<string> wsWids = new List<string>();
        Label lblWs;

        void LoadWsSelection()
        {
            if (File.Exists(WsFile)) wsWids = Util.ReadList(WsFile);
            else if (manifest != null) wsWids = Util.L(Util.D(manifest, "workshopItems"), "wids");
            UpdateWsLabel();
        }

        void UpdateWsLabel()
        {
            if (lblWs != null) lblWs.Text = wsWids.Count == 0 ? "Keine Mods ueber den Workshop (alles im Paket)." : wsWids.Count + " Mods laden ueber den Workshop (WorkshopItems=).";
        }

        // Ordnernamen (unter Zomboid\mods) der Workshop-Mods - fuer das Aufraeumen beim Spieler
        List<string> WsFolders()
        {
            var res = new List<string>();
            var ws = tWs.Text.Trim();
            var old = Util.L(Util.D(manifest, "workshopItems"), "folders");
            foreach (var w in wsWids)
            {
                var md = Path.Combine(ws, w, "mods");
                if (w == PackBuilder.ZB_FIX) continue; // bleibt im Paket
                if (Directory.Exists(md)) res.AddRange(Directory.GetDirectories(md).Select(Path.GetFileName));
            }
            if (res.Count == 0 && wsWids.Count > 0) res.AddRange(old);
            return res.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        void DevModList()
        {
            using (var f = new ModListForm(tWs.Text.Trim(), Util.ReadList(tList.Text), wsWids, this))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                wsWids = f.Selected;
                Directory.CreateDirectory(Path.GetDirectoryName(WsFile));
                File.WriteAllLines(WsFile, wsWids);
                UpdateWsLabel();
                AppendLog("Workshop-Auswahl gespeichert (" + wsWids.Count + "): " + WsFile);
                AppendLog("Naechste Schritte: Server-INI aktualisieren, manifest.json speichern + hochladen. Haken ENTFERNT? Dann MODS-Ordner neu bauen + ZIP neu hochladen.");
            }
        }

        void DevServerIni()
        {
            using (var d = new OpenFileDialog { Filter = "Server-INI (*.ini)|*.ini", InitialDirectory = Path.Combine(Util.ZomboidDir, "Server") })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var mods = PackBuilder.EffMods(tWs.Text, Util.ReadList(tList.Text), Util.ReadList(tModIds.Text), chkExtra.Checked);
                var maps = Util.ReadList(tMaps.Text);
                var own = PackBuilder.OwnModIds(EigeneModsDir());
                PackBuilder.UpdateServerIni(d.FileName, mods, maps, string.Join(";", wsWids), own);
                if (own.Count > 0) AppendLog("Eigene Mods ans Ende von Mods= gesetzt: " + string.Join(", ", own));
                AppendLog("Server-INI aktualisiert: " + d.FileName + " (" + PackBuilder.MergeModIds(mods, own).Count + " Mods, " + maps.Count + " Maps, WorkshopItems: " + wsWids.Count + ", Backup angelegt).");
                if (wsWids.Count > 0)
                {
                    AppendLog("Server: diese Ordner aus <Server>\\Zomboid\\mods loeschen, sonst laedt der Server die alte Kopie statt der Workshop-Version:");
                    AppendLog("   " + string.Join(", ", WsFolders()));
                }
            }
        }

        const string CREDIT_URL = "https://steamcommunity.com/id/majestiKTox/myworkshopfiles/?appid=108600";
        string LauncherWsUrl { get { return Util.S(Util.ReadJson(Path.Combine(Util.ExeDir, "launcher.cfg")), "launcherWorkshopUrl"); } }

        static string WsIdFromUrl(string url)
        {
            var m = Regex.Match(url ?? "", @"[?&]id=(\d+)");
            return m.Success ? m.Groups[1].Value : "";
        }

        // Fester Credit ganz unten: "majestiK Launcher vX | Workshop | by T0X.IQ" (Workshop-Link nur, wenn in launcher.cfg eingetragen)
        void UpdateCredit()
        {
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            var txt = "majestiK Launcher v" + ver.Major + "." + ver.Minor + "." + ver.Build + "  |  ";
            var wsUrl = LauncherWsUrl;
            lblCredit.Links.Clear();
            int wsAt = -1;
            if (wsUrl != "") { wsAt = txt.Length; txt += "Workshop"; txt += "  |  "; }
            int by = txt.Length + 3;
            txt += "by T0X.IQ";
            lblCredit.Text = txt;
            if (wsAt >= 0) lblCredit.Links.Add(wsAt, "Workshop".Length, wsUrl);
            lblCredit.Links.Add(by + 0, 6, CREDIT_URL);
        }

        // ---------- Launcher-Update ueber den Steam-Workshop erkennen
        // Sucht in allen Steam-Bibliotheken: workshop\content\108600\<ID>\mods\majestiKLauncher\majestiKLauncher.exe
        // Der Steam-Workshop erlaubt keine exe/dll/zip. Deshalb steht die Update-Info als Textdatei im Workshop-Eintrag
        // (latest_version.txt: Zeile 1 = Version, z.B. 1.2.0 ; Zeile 2 = Download-Link, direkte .exe oder Webseite)
        // und optional online (launcher.cfg -> launcherUpdateUrl, gleiches Format).
        static bool ParseUpdateInfo(string text, out Version v, out string url)
        {
            v = null; url = "";
            var lines = (text ?? "").Replace("\r", "").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith("#")).ToList();
            if (lines.Count == 0) return false;
            try { v = new Version(lines[0].TrimStart('v', 'V')); } catch { return false; }
            if (v.Build < 0) v = new Version(v.Major, v.Minor, 0, 0);
            if (lines.Count > 1) url = lines[1];
            return true;
        }

        static bool FindWorkshopUpdateInfo(string preferId, out Version found, out string foundUrl)
        {
            found = null; foundUrl = "";
            foreach (var lib in SteamInfo.Libraries())
            {
                var root = Path.Combine(lib, "steamapps", "workshop", "content", "108600");
                if (!Directory.Exists(root)) continue;
                foreach (var item in Directory.GetDirectories(root))
                {
                    if (preferId != "" && !Path.GetFileName(item).Equals(preferId)) continue;
                    var dir = Path.Combine(item, "mods", "majestiKLauncher");
                    if (!Directory.Exists(dir)) continue;
                    string f = null;
                    try { f = Directory.GetFiles(dir, "latest_version.txt", SearchOption.AllDirectories).FirstOrDefault(); } catch { }
                    if (f == null) continue;
                    Version v; string u;
                    if (ParseUpdateInfo(File.ReadAllText(f, Encoding.UTF8), out v, out u) && (found == null || v > found)) { found = v; foundUrl = u; }
                }
            }
            return found != null;
        }

        const string DEFAULT_UPDATE_URL = "https://raw.githubusercontent.com/T0XiQ96/majestiKLauncher/main/latest_version.txt";
        string updUrl = ""; Version updVer;
        System.Windows.Forms.Timer updTimer;

        void CheckLauncherUpdate()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    Version best = null; string bestUrl = "";
                    Version v; string u;
                    var id = WsIdFromUrl(LauncherWsUrl);
                    if (FindWorkshopUpdateInfo(id, out v, out u) || (id != "" && FindWorkshopUpdateInfo("", out v, out u))) { best = v; bestUrl = u; }
                    var online = Util.S(Util.ReadJson(Path.Combine(Util.ExeDir, "launcher.cfg")), "launcherUpdateUrl");
                    if (online == "") online = DEFAULT_UPDATE_URL;
                    if (online != "")
                    {
                        try
                        {
                            var txt = Downloader.GetString(online + (online.Contains("?") ? "&" : "?") + "nocache=" + DateTime.UtcNow.Ticks);
                            if (ParseUpdateInfo(txt, out v, out u) && (best == null || v > best)) { best = v; bestUrl = u; }
                        }
                        catch { }
                    }
                    if (best == null) return;
                    var mine = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                    if (best <= mine) return;
                    if (bestUrl == "") bestUrl = LauncherWsUrl;
                    updUrl = bestUrl; updVer = best;
                    BeginInvoke((Action)(() =>
                    {
                        lblUpdate.Text = "Launcher-Update verfuegbar: v" + best.Major + "." + best.Minor + "." + best.Build + " (du hast v" + mine.Major + "." + mine.Minor + "." + mine.Build + ") - hier klicken";
                        lblUpdate.LinkArea = new LinkArea(0, lblUpdate.Text.Length);
                        lblUpdate.Visible = true;
                        AppendLog("Launcher-Update gefunden: v" + best + (bestUrl != "" ? "  -> " + bestUrl : ""));
                    }));
                }
                catch { }
            });
        }

        void InstallLauncherUpdate()
        {
            if (updUrl == "") { Loc.Show(this, "Es ist kein Download-Link hinterlegt. Bitte die Workshop-Seite des Launchers oeffnen.", "Launcher-Update"); return; }
            try
            {
                var path = updUrl.Split('?')[0];
                if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { Process.Start(updUrl); return; }   // Webseite -> im Browser oeffnen
                if (Loc.Show(this, "Die neue Launcher-Version herunterladen, installieren und neu starten?\r\n(Server-Liste, Logos und launcher.cfg bleiben erhalten.)", "Launcher-Update", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                var dstDir = Util.ExeDir.TrimEnd('\\');
                var tmp = Path.Combine(Util.DataDir, "majestiKLauncher_new.exe");
                Downloader.GetFile(updUrl, tmp);
                var hdr = new byte[2]; using (var fs = File.OpenRead(tmp)) fs.Read(hdr, 0, 2);
                var len = new FileInfo(tmp).Length;
                if (hdr[0] != (byte)'M' || hdr[1] != (byte)'Z' || len < 100 * 1024) { File.Delete(tmp); throw new Exception("Die heruntergeladene Datei ist keine gueltige Launcher-exe."); }
                var dst = Path.Combine(dstDir, "majestiKLauncher.exe");
                var cur = Application.ExecutablePath;
                if (File.Exists(dst) && string.Equals(Path.GetFullPath(dst), Path.GetFullPath(cur), StringComparison.OrdinalIgnoreCase))
                {
                    var old = dst + ".old"; if (File.Exists(old)) File.Delete(old);
                    File.Move(dst, old);   // laufende exe umbenennen geht unter Windows
                }
                File.Copy(tmp, dst, true); File.Delete(tmp);
                Process.Start(dst, devMode ? "--dev" : "");
                Application.Exit();
            }
            catch (Exception ex) { Loc.Show(this, "Update fehlgeschlagen: " + ex.Message, "Launcher-Update"); }
        }

        // ---------- Mehrere Server
        void SwitchServer(ServerEntry e)
        {
            curServer = e; Util.Profile = e.Slug;
            ServerStore.Save(servers, e.Slug);
            btnDelSrv.Enabled = servers.Contains(e);
            manifest = null;
            ApplyLogo();
            ApplyDevFields();
            AppendLog("Server gewaehlt: " + e.Name + (e.IsCollection ? " (Steam-Kollektion)" : ""));
            RefreshManifest();
        }

        void AddServer()
        {
            using (var f = new AddServerForm())
            {
                while (f.ShowDialog(this) == DialogResult.OK)
                {
                    var name = f.tName.Text.Trim(); var url = f.tUrl.Text.Trim();
                    if (name == "" || url == "") { Loc.Show(this, "Bitte Name und Link eingeben.", "Server hinzufuegen"); continue; }
                    if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("steam://", StringComparison.OrdinalIgnoreCase))
                    { Loc.Show(this, "Der Link muss mit https:// beginnen.", "Server hinzufuegen"); continue; }
                    if (servers.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { Loc.Show(this, "Einen Server mit diesem Namen gibt es schon.", "Server hinzufuegen"); continue; }
                    var kind = ServerEntry.LooksLikeSteam(url) ? "collection" : "manifest";
                    if (kind == "manifest")
                    {
                        try
                        {
                            var txt = Downloader.GetString(url + (url.Contains("?") ? "&" : "?") + "nocache=" + DateTime.UtcNow.Ticks);
                            var m = Util.Json.Deserialize<Dictionary<string, object>>(txt);
                            if (m == null || (Util.S(m, "packUrl") == "" && Util.S(m, "version") == "")) throw new Exception("Das ist kein Mod-Paket-Manifest (kein 'packUrl'/'version').");
                        }
                        catch (Exception ex)
                        {
                            if (Loc.Show(this, "Der Link ist als Manifest nicht lesbar:\r\n" + ex.Message + "\r\n\r\nTrotzdem speichern?", "Server hinzufuegen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) continue;
                        }
                    }
                    var e2 = new ServerEntry { Name = name, Url = url, Kind = kind, Slug = ServerEntry.MakeSlug(name, servers.Select(x => x.Slug)), LogoUrl = f.tLogo.Text.Trim() };
                    if (e2.LogoUrl != "") e2.Logo = DownloadListLogo(e2.LogoUrl, url);
                    servers.Add(e2); cbServer.Items.Add(e2);
                    ServerStore.Save(servers, e2.Slug);
                    cbServer.SelectedItem = e2;   // loest SwitchServer aus
                    AppendLog("Server hinzugefuegt: " + name + (kind == "collection" ? " (Steam-Kollektion - Spieler werden dorthin geleitet)" : " (Download-Link)"));
                    return;
                }
            }
        }

        // ---------- Zentrale Serverliste: Spieler koennen keine Server hinzufuegen/entfernen, der Betreiber pflegt servers.json online
        const string DEFAULT_SERVERLIST_URL = "https://raw.githubusercontent.com/T0XiQ96/majestiKLauncher/main/servers.json";

        static string DownloadListLogo(string logoUrl, string key)
        {
            try
            {
                var dir = Path.Combine(Util.BaseDataDir, "logos"); Directory.CreateDirectory(dir);
                var f = Path.Combine(dir, "list_" + Util.Sha256String(key).Substring(0, 12) + ".img");
                var tmp = f + ".tmp"; if (File.Exists(tmp)) File.Delete(tmp);
                if (logoUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) Downloader.GetFile(logoUrl, tmp);
                else File.Copy(Path.IsPathRooted(logoUrl) ? logoUrl : Path.Combine(Util.ExeDir, logoUrl), tmp, true);   // mitgelieferte Datei neben der exe
                var len = new FileInfo(tmp).Length;
                if (len < 100 || len > 8 * 1024 * 1024) { File.Delete(tmp); return ""; }
                using (LoadImageNoLock(tmp)) { }
                if (File.Exists(f)) File.Delete(f);
                File.Move(tmp, f);
                return f;
            }
            catch { return ""; }
        }

        void SyncServerList()
        {
            var src = Util.S(cfg, "serverListUrl");
            var localList = Path.Combine(Util.ExeDir, "servers.json");
            bool useLocal = src == "" && File.Exists(localList);   // servers.json liegt neben der exe -> diese Liste gilt (ohne Internet)
            if (src == "") src = DEFAULT_SERVERLIST_URL;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var txt = useLocal ? File.ReadAllText(localList, Encoding.UTF8) : Downloader.GetString(src + (src.Contains("?") ? "&" : "?") + "nocache=" + DateTime.UtcNow.Ticks);
                    var items = new List<ServerEntry>(); var logos = new Dictionary<string, string>();
                    foreach (var d in Util.L2(Util.Json.Deserialize<Dictionary<string, object>>(txt), "servers"))
                    {
                        var n = Util.S(d, "name"); var u = Util.S(d, "url");
                        if (n == "" || u == "") continue;
                        var it = new ServerEntry { Name = n, Url = u, LogoUrl = Util.S(d, "logoUrl"), Kind = ServerEntry.LooksLikeSteam(u) ? "collection" : "manifest" };
                        items.Add(it);
                        if (it.LogoUrl != "") { var lp = DownloadListLogo(it.LogoUrl, u); if (lp != "") logos[u] = lp; }
                    }
                    if (items.Count == 0) return;
                    BeginInvoke((Action)(() => ApplyServerList(items, logos)));
                }
                catch { }
            });
        }

        void ApplyServerList(List<ServerEntry> items, Dictionary<string, string> logos)
        {
            bool changed = false, logoChanged = false;
            var keep = new HashSet<string>(items.Select(x => x.Url), StringComparer.OrdinalIgnoreCase);
            foreach (var it in items)
            {
                var ex = servers.FirstOrDefault(x => x.Url.Equals(it.Url, StringComparison.OrdinalIgnoreCase));
                string lg; logos.TryGetValue(it.Url, out lg);
                if (ex == null)
                {
                    it.Slug = ServerEntry.MakeSlug(it.Name, servers.Select(x => x.Slug)); it.Remote = true; it.Logo = lg ?? "";
                    servers.Add(it); changed = true;
                    continue;
                }
                if (!ex.Remote || ex.Name != it.Name || ex.Kind != it.Kind || ex.LogoUrl != it.LogoUrl) changed = true;
                ex.Remote = true; ex.Name = it.Name; ex.Kind = it.Kind; ex.LogoUrl = it.LogoUrl;
                if (lg != null && ex.Logo != lg) { ex.Logo = lg; changed = true; if (ex == curServer) logoChanged = true; }
                else if (lg == null && it.LogoUrl == "" && ex.Logo.IndexOf("list_", StringComparison.Ordinal) >= 0) { ex.Logo = ""; changed = true; if (ex == curServer) logoChanged = true; }
            }
            foreach (var gone in servers.Where(x => x.Remote && !keep.Contains(x.Url) && x != curServer).ToList()) { servers.Remove(gone); changed = true; }
            if (!changed) return;
            switching = true;
            cbServer.Items.Clear(); foreach (var sv in servers) cbServer.Items.Add(sv);
            if (servers.Contains(curServer)) cbServer.SelectedItem = curServer;
            switching = false;
            ServerStore.Save(servers, curServer.Slug);
            AppendLog("Serverliste vom Betreiber aktualisiert (" + servers.Count + " Server).");
            if (curServer == NoServer && servers.Count > 0) cbServer.SelectedItem = servers[0];   // loest SwitchServer aus
            else if (logoChanged) ApplyLogo();
        }

        void DevExportServers()
        {
            ExportServersTo(Util.ExeDir);
            SetStatus("Gespeichert: " + Path.Combine(Util.ExeDir, "servers.json"));
            AppendLog("servers.json gespeichert (Logos: Ordner 'logos' daneben). Fuer die Spieler am besten: 'Spieler-Paket erstellen (ZIP)'.");
        }

        // servers.json (+ Ordner logos) in 'dir' schreiben
        void ExportServersTo(string dir)
        {
            var list = new List<object>();
            foreach (var e in servers)
            {
                var d = new Dictionary<string, object> { { "name", e.Name }, { "url", e.Url } };
                if (e.LogoUrl != "") d["logoUrl"] = e.LogoUrl;
                else if (e.Logo != "" && File.Exists(e.Logo) && e.Logo.IndexOf("list_", StringComparison.Ordinal) < 0)
                {   // selbst gesetztes Logo: nach logos\ kopieren, damit es mit dem Paket an die Spieler geht
                    var ld = Path.Combine(dir, "logos"); Directory.CreateDirectory(ld);
                    var fn = e.Slug + Path.GetExtension(e.Logo).ToLowerInvariant(); if (fn.StartsWith(".")) fn = "server" + fn;
                    File.Copy(e.Logo, Path.Combine(ld, fn), true);
                    d["logoUrl"] = "logos\\" + fn;
                }
                list.Add(d);
            }
            Util.WriteJson(Path.Combine(dir, "servers.json"), new Dictionary<string, object> { { "servers", list } });
        }

        // Fertiges Spieler-Paket: exe + launcher.cfg + servers.json + logos + Manifeste + Info-Texte -> ZIP
        void DevBuildPlayerPack()
        {
            using (var sf = new SaveFileDialog { Filter = "ZIP (*.zip)|*.zip", FileName = "majestiK_Spielerpaket.zip", Title = "Spieler-Paket speichern" })
            {
                if (sf.ShowDialog(this) != DialogResult.OK) return;
                string stage = null;
                try
                {
                    stage = Path.Combine(Path.GetTempPath(), "majestiK_pack_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                    var root = Path.Combine(stage, "majestiKLauncher"); Directory.CreateDirectory(root);
                    File.Copy(Application.ExecutablePath, Path.Combine(root, "majestiKLauncher.exe"), true);
                    if (File.Exists(Application.ExecutablePath + ".config")) File.Copy(Application.ExecutablePath + ".config", Path.Combine(root, "majestiKLauncher.exe.config"), true);
                    foreach (var f in new[] { "launcher_info.txt", "launcher_info_en.txt", "logo.png" })
                    { var s = Path.Combine(Util.ExeDir, f); if (File.Exists(s)) File.Copy(s, Path.Combine(root, f), true); }
                    ExportServersTo(root);
                    var first = servers.FirstOrDefault(x => !x.IsCollection) ?? servers.FirstOrDefault();
                    var c = new Dictionary<string, object> { { "title", first != null ? first.Name : "" }, { "manifestUrl", first != null ? first.Url : "" } };
                    var ws = LauncherWsUrl; if (ws != "") c["launcherWorkshopUrl"] = ws;
                    foreach (var k in new[] { "serverListUrl", "launcherUpdateUrl" }) { var v = Util.S(cfg, k); if (v != "") c[k] = v; }
                    Util.WriteJson(Path.Combine(root, "launcher.cfg"), c);
                    int mc = 0;
                    foreach (var sv in servers)
                    {
                        if (sv.IsCollection || !sv.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                        string txt = null;
                        if (sv == curServer) txt = Util.Pretty(Util.Json.Serialize(DevManifest()));
                        else
                        {
                            var mp = sv.Slug == "" ? Path.Combine(Util.ExeDir, "manifest.json") : Path.Combine(Util.ExeDir, "dev", sv.Slug, "manifest.json");
                            if (File.Exists(mp)) txt = File.ReadAllText(mp, Encoding.UTF8);
                        }
                        if (txt == null) { AppendLog("Hinweis: kein gespeichertes Manifest fuer '" + sv.Name + "' (Server auswaehlen, 'manifest.json speichern') - Spieler brauchen dann den Online-Link."); continue; }
                        Directory.CreateDirectory(Path.Combine(root, "manifests"));
                        File.WriteAllText(BundledManifestPath(sv.Url).Replace(Util.ExeDir, root + Path.DirectorySeparatorChar), txt, new UTF8Encoding(false)); mc++;
                    }
                    if (File.Exists(sf.FileName)) File.Delete(sf.FileName);
                    System.IO.Compression.ZipFile.CreateFromDirectory(root, sf.FileName, System.IO.Compression.CompressionLevel.Optimal, true);
                    AppendLog("Spieler-Paket erstellt: " + sf.FileName + "  (" + servers.Count + " Server, " + mc + " Manifest(e) enthalten)");
                    AppendLog("Achtung: Manifeste enthalten ggf. das Server-Passwort. Dieses ZIP an deine Spieler geben - Entpacken, majestiKLauncher.exe starten, Installieren.");
                    SetStatus("Spieler-Paket erstellt: " + sf.FileName);
                }
                catch (Exception ex) { AppendLog("FEHLER: " + ex.Message); }
                finally { try { if (stage != null) Directory.Delete(stage, true); } catch { } }
            }
        }

        void RemoveServer()
        {
            if (curServer == null || !servers.Contains(curServer)) return;
            if (Loc.Show(this, "'" + curServer.Name + "' aus der Liste entfernen?\r\n(Bereits installierte Mods bleiben im Spielordner.)", "Server entfernen", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            var gone = curServer; servers.Remove(gone);
            switching = true; cbServer.Items.Remove(gone); switching = false;
            var next = servers.FirstOrDefault() ?? NoServer;
            ServerStore.Save(servers, next.Slug);
            if (next != NoServer) { cbServer.SelectedItem = next; }
            else { SwitchServer(NoServer); }
        }

        // ---------- Logo pro Server: 1) vom Nutzer gewaehlt  2) logoUrl aus dem Manifest  3) Standard-Logo
        static Image LoadImageNoLock(string path)
        {
            using (var ms = new MemoryStream(File.ReadAllBytes(path))) return new Bitmap(ms);
        }

        string RemoteLogoPath { get { return Path.Combine(Util.DataDir, "logo_remote.img"); } }

        void ApplyLogo()
        {
            Image img = null;
            try
            {
                if (curServer != null && curServer.Logo != "" && File.Exists(curServer.Logo)) img = LoadImageNoLock(curServer.Logo);
                else if (File.Exists(RemoteLogoPath) && manifest != null && Util.S(manifest, "logoUrl") != "") img = LoadImageNoLock(RemoteLogoPath);
            }
            catch { img = null; }
            headerPic.Image = img ?? defaultLogo;
            FitLogo();
        }

        void FitLogo()
        {
            var im = headerPic.Image;
            if (im == null) return;
            var ratio = (double)im.Height / im.Width;
            headerPic.Height = Math.Max(150, Math.Min(340, (int)(ClientSize.Width * ratio)));
        }

        void ChooseLogo()
        {
            if (curServer == null || !servers.Contains(curServer)) { Loc.Show(this, "Zuerst einen Server auswaehlen oder hinzufuegen.", "Logo"); return; }
            using (var d = new OpenFileDialog { Filter = "Bilder (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif", Title = "Logo fuer '" + curServer.Name + "' waehlen" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    LoadImageNoLock(d.FileName).Dispose();   // pruefen, ob es ein Bild ist
                    var dir = Path.Combine(Util.BaseDataDir, "logos"); Directory.CreateDirectory(dir);
                    var dst = Path.Combine(dir, (curServer.Slug == "" ? "default" : curServer.Slug) + Path.GetExtension(d.FileName).ToLowerInvariant());
                    File.Copy(d.FileName, dst, true);
                    curServer.Logo = dst; ServerStore.Save(servers, curServer.Slug);
                    ApplyLogo(); AppendLog("Logo fuer '" + curServer.Name + "' gesetzt.");
                }
                catch (Exception ex) { Loc.Show(this, "Das Bild konnte nicht geladen werden: " + ex.Message, "Logo"); }
            }
        }

        void ClearLogo()
        {
            if (curServer == null || !servers.Contains(curServer)) return;
            curServer.Logo = ""; ServerStore.Save(servers, curServer.Slug); ApplyLogo();
            AppendLog("Eigenes Logo entfernt (es gilt wieder das Logo aus dem Manifest bzw. das Standard-Logo).");
        }

        void FetchRemoteLogo()
        {
            var lu = manifest != null ? Util.S(manifest, "logoUrl") : "";
            if (lu == "") return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var tmp = RemoteLogoPath + ".tmp";
                    Downloader.GetFile(lu, tmp);
                    var len = new FileInfo(tmp).Length;
                    if (len < 100 || len > 8 * 1024 * 1024) { File.Delete(tmp); return; }
                    using (LoadImageNoLock(tmp)) { }
                    if (File.Exists(RemoteLogoPath)) File.Delete(RemoteLogoPath);
                    File.Move(tmp, RemoteLogoPath);
                    BeginInvoke((Action)ApplyLogo);
                }
                catch { }
            });
        }

        void ApplyServerKind()
        {
            bool col = curServer != null && curServer.IsCollection;
            btnManual.Enabled = !col;
            if (col)
            {
                btnInstall.Text = "Kollektion in Steam oeffnen";
                lblVersion.Text = "Steam-Workshop-Kollektion: dort alles abonnieren, Steam laedt die Mods herunter.";
                lblVersion.ForeColor = Lime;
                lblServer.Text = curServer.Url;
            }
        }

        void OpenCollection()
        {
            var url = curServer.Url;
            try { Process.Start(url.StartsWith("steam://", StringComparison.OrdinalIgnoreCase) ? url : "steam://openurl/" + url); }
            catch { try { Process.Start(url); } catch (Exception ex) { AppendLog("FEHLER: " + ex.Message); return; } }
            AppendLog("Kollektion geoeffnet: " + url);
            AppendLog("In der Kollektion unten/oben auf 'Alle abonnieren' klicken, Steam laedt dann alle Mods herunter (Steam > Downloads).");
            AppendLog("Danach Mods im Spiel (Mods-Menue) bzw. ueber die Server-Mod-Liste aktivieren, ZombieBuddy bei Bedarf ueber 'Nur ZombieBuddy installieren ...'.");
        }

        // ---------- Manifest
        static string BundledManifestPath(string url) { return Path.Combine(Util.ExeDir, "manifests", Util.Sha256String(url).Substring(0, 12) + ".json"); }

        // Online-Manifest; ist es nicht erreichbar, gilt das mitgelieferte (manifests\<hash>.json). Kein http-Link = Datei (relativ zur exe).
        static string LoadManifestText(string url)
        {
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return File.ReadAllText(Path.IsPathRooted(url) ? url : Path.Combine(Util.ExeDir, url), Encoding.UTF8);
            try { return Downloader.GetString(url + (url.Contains("?") ? "&" : "?") + "nocache=" + DateTime.UtcNow.Ticks); }
            catch { var b = BundledManifestPath(url); if (File.Exists(b)) return File.ReadAllText(b, Encoding.UTF8); throw; }
        }

        void RefreshManifest()
        {
            if (curServer != null && curServer.IsCollection) { ApplyServerKind(); return; }
            btnManual.Enabled = true;
            var url = curServer != null ? curServer.Url : Util.S(cfg, "manifestUrl");
            if (url == "")
            {
                lblVersion.Text = "Kein Server vorhanden - oben auf '+ Server hinzufuegen' klicken und Name + Link eintragen.";
                lblVersion.ForeColor = FgText;
                return;
            }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var txt = LoadManifestText(url);
                    manifest = Util.Json.Deserialize<Dictionary<string, object>>(txt);
                    BeginInvoke((Action)(() =>
                    {
                        UpdateStatusLabel();
                        var a = ServerAddress();
                        lblServer.Text = (a != "" ? "Server: " + a : "") + (Util.S(manifest, "news") != "" ? "     " + Util.S(manifest, "news") : "");
                        FillDevFromManifest();
                        ApplyLogo(); FetchRemoteLogo();
                    }));
                }
                catch (Exception e)
                {
                    BeginInvoke((Action)(() => { lblVersion.Text = "Manifest nicht erreichbar: " + e.Message; }));
                }
            });
        }

        // Muss installiert/aktualisiert werden? (Paketversion oder Workshop-Auswahl geaendert)
        string UpdateReason()
        {
            if (manifest == null) return null;
            var st = new Installer().LoadState();
            var inst = Util.S(st, "version", "");
            if (inst == "") return "noch nicht installiert";
            if (inst != ManifestVersion() && !inst.StartsWith("manuell")) return "neues Paket";
            List<string> rem, res;
            new Installer().WorkshopDiff(Util.D(manifest, "workshopItems"), out rem, out res);
            if (res.Count > 0) return res.Count + " Mods kommen wieder aus dem Paket";
            if (rem.Count > 0) return rem.Count + " Mods wechseln auf den Steam-Workshop";
            return null;
        }

        void UpdateStatusLabel()
        {
            var st = new Installer().LoadState();
            var inst = Util.S(st, "version", "-");
            var why = UpdateReason();
            lblVersion.Text = "Aktuelles Paket: " + ManifestVersion() + "     Installiert: " + inst + (why == null ? "   (aktuell)" : "   -> Aktualisieren noetig: " + why);
            lblVersion.ForeColor = why == null ? Lime : Color.FromArgb(235, 80, 80);
            btnInstall.Text = why == null ? "Neu installieren" : "Installieren / Aktualisieren";
        }

        string ManifestVersion()
        {
            if (manifest == null) return "";
            var v = Util.S(manifest, "version");
            return v != "" ? v : Util.S(manifest, "packUrl");
        }
        string ServerAddress()
        {
            var sv = Util.D(manifest, "server");
            if (sv == null || Util.S(sv, "ip") == "") return "";
            return Util.S(sv, "ip") + ":" + Util.S(sv, "port", "16261");
        }

        // ---------- Installieren
        void RunInstall(string localZip)
        {
            if (curServer != null && curServer.IsCollection) { OpenCollection(); return; }
            if (localZip == null && (manifest == null || Util.S(manifest, "packUrl") == ""))
            {
                Loc.Show(this, "Kein Download-Link im Manifest. Du kannst die ZIP auch manuell waehlen.", "Hinweis");
                return;
            }
            SavePrefs();
            var ram = (int)numRam.Value; bool applyCfg = chkConfig.Checked, keep = chkKeepZip.Checked;
            RunTask("Installation", ct =>
            {
                var pz = SteamInfo.PZInstallDir();
                if (pz == null) throw new Exception("Project Zomboid wurde nicht gefunden. Ist es ueber Steam installiert?");
                AppendLog("Project Zomboid: " + pz);
                AppendLog("Zomboid-Ordner: " + Util.ZomboidDir);
                if (Process.GetProcessesByName("ProjectZomboid64").Length > 0) throw new Exception("Project Zomboid laeuft noch - bitte zuerst beenden.");
                var wsBlock = Util.D(manifest, "workshopItems");
                var wsWidsM = Util.L(wsBlock, "wids");
                List<string> wsRem, wsRes;
                new Installer().WorkshopDiff(wsBlock, out wsRem, out wsRes);
                var stQ = new Installer().LoadState();
                if (localZip == null && Util.S(stQ, "version") == ManifestVersion() && wsRes.Count == 0)
                {   // Paket ist aktuell - nur Workshop-Abgleich + ZombieBuddy, kein Download
                    AppendLog("Paket ist aktuell - gleiche nur die Workshop-Mods ab (kein Download noetig).");
                    var q = new Installer { Log = AppendLog, Progress = SetProgress, Cancel = ct, ZbPin = Util.D(manifest, "zombieBuddy") };
                    q.RemoveWorkshopFolders(wsBlock);
                    AppendLog("ZombieBuddy pruefen ...");
                    q.SetupZombieBuddy(pz, ram);
                    q.ApproveWorkshopJavaMods(wsWidsM);
                    try { q.EnsureMenuMods(); } catch (Exception ex) { AppendLog("WARNUNG - default.txt (Horse): " + ex.Message); }
                    AppendLog("FERTIG.");
                    BeginInvoke((Action)(() => { UpdateStatusLabel(); WorkshopInfo(wsWidsM.Count); }));
                    return;
                }
                if (wsRes.Count > 0) AppendLog(wsRes.Count + " Mods kommen wieder aus dem Paket - Paket wird neu installiert.");

                string zip = localZip;
                if (zip == null)
                {
                    zip = Path.Combine(Util.DataDir, "pack.zip");
                    var drive = new DriveInfo(Path.GetPathRoot(Util.DataDir));
                    AppendLog("Freier Speicher auf " + drive.Name + ": " + Util.Size(drive.AvailableFreeSpace));
                    var dl = new Downloader { Log = AppendLog, Progress = (a, b) => SetProgress("Download", a, b), Cancel = ct, GofileSalt = Util.S(manifest, "gofileSalt", "12af056dacea0b") };
                    var st0 = new Installer().LoadState();
                    if (File.Exists(zip) && Util.S(st0, "cachedVersion") == ManifestVersion())
                        AppendLog("Paket bereits heruntergeladen, verwende Cache.");
                    else
                    {
                        if (File.Exists(zip)) File.Delete(zip);
                        AppendLog("Lade Paket " + ManifestVersion() + " ...");
                        dl.Download(Util.S(manifest, "packUrl"), zip);
                        st0["cachedVersion"] = ManifestVersion(); Util.WriteJson(new Installer().StatePath, st0);
                    }
                    var sha = Util.S(manifest, "packSha256");
                    if (sha != "")
                    {
                        AppendLog("Pruefe SHA256 ...");
                        var len = new FileInfo(zip).Length;
                        var got = Util.Sha256File(zip, d => SetProgress("Pruefen", d, len));
                        if (!string.Equals(got, sha, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Delete(zip);
                            throw new Exception("Pruefsumme stimmt nicht - Download beschaedigt oder Link veraltet. Bitte erneut versuchen.");
                        }
                    }
                }

                var inst = new Installer { Log = AppendLog, Progress = SetProgress, Cancel = ct, ZbPin = Util.D(manifest, "zombieBuddy") };
                var folders = inst.InstallPack(zip, applyCfg);
                AppendLog("ZombieBuddy einrichten ...");
                inst.SetupZombieBuddy(pz, ram);
                inst.RemoveWorkshopFolders(wsBlock);
                inst.ApproveJavaMods(folders);
                inst.ApproveWorkshopJavaMods(wsWidsM);
                try { inst.EnsureMenuMods(); } catch (Exception ex) { AppendLog("WARNUNG - default.txt (Horse): " + ex.Message); }
                var missingRes = wsRes.Where(f => !Directory.Exists(Path.Combine(Util.ZomboidDir, "mods", f)) && !Util.L(wsBlock, "folders").Contains(f, StringComparer.OrdinalIgnoreCase)).ToList();
                if (missingRes.Count > 0) AppendLog("WARNUNG - nicht im Paket enthalten (Server-Admin muss das Paket neu bauen): " + string.Join(", ", missingRes));

                var st = inst.LoadState();
                st["version"] = localZip == null ? ManifestVersion() : "manuell " + DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                st["installedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                Util.WriteJson(inst.StatePath, st);
                if (localZip == null && !keep) { try { File.Delete(zip); AppendLog("Download-ZIP geloescht."); } catch { } }

                AppendLog("");
                AppendLog("FERTIG! " + folders.Count + " Mod-Ordner installiert.");
                AppendLog("Wichtig: Falls du die Very-Far-Away-Collection auf Steam abonniert hast, deabonniere sie bitte selbst");
                AppendLog("(vorher gern als Backup in eine eigene Kollektion packen) - sonst kann der Server mit");
                AppendLog("\"File doesn't match the one on the server\" kicken. Details im Tab 'Info / Anleitung'.");
                BeginInvoke((Action)(() => { RefreshManifest(); WorkshopInfo(wsWidsM.Count); }));
            });
        }

        void WorkshopInfo(int n)
        {
            if (n == 0) return;
            Loc.Show(this,
                n + " Mods laedt der Server ueber den Steam-Workshop.\r\n\r\n" +
                "Du musst dafuer NICHTS abonnieren: Beim Beitreten laedt Project Zomboid diese Mods automatisch ueber Steam herunter " +
                "(Steam muss laufen; beim ersten Mal bzw. nach Mod-Updates kann das ein paar Minuten dauern).\r\n\r\n" +
                "Kommt beim Beitreten \"Workshop item version is different\" oder \"File doesn't match\": Spiel komplett beenden, " +
                "Steam die Updates fertig laden lassen und neu verbinden.",
                "Mods ueber den Steam-Workshop", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---------- ZombieBuddy erkannt, aber nicht eingerichtet -> nachfragen (neueste Version + Compatibility-Fix)
        static bool ZbInstalled()
        {
            try
            {
                var pz = SteamInfo.PZInstallDir(); if (pz == null) return true;
                if (!File.Exists(Path.Combine(pz, "ZombieBuddy.jar")) || !File.Exists(Path.Combine(pz, "zbNative.dll"))) return false;
                var j = Path.Combine(pz, "ProjectZomboid64.json");
                return File.Exists(j) && File.ReadAllText(j).Contains("-agentlib:zbNative");
            }
            catch { return true; }
        }

        static bool ZbDetected()
        {
            try
            {
                var dirs = new List<string> { Path.Combine(Util.ZomboidDir, "mods") };
                foreach (var lib in SteamInfo.Libraries())
                {
                    var root = Path.Combine(lib, "steamapps", "workshop", "content", "108600");
                    if (!Directory.Exists(root)) continue;
                    foreach (var item in Directory.GetDirectories(root))
                    {
                        var n = Path.GetFileName(item);
                        if (n == "3619862853" || n == "3809837933") return true;
                        var m = Path.Combine(item, "mods"); if (Directory.Exists(m)) dirs.Add(m);
                    }
                }
                foreach (var d in dirs)
                {
                    if (!Directory.Exists(d)) continue;
                    foreach (var mod in Directory.GetDirectories(d))
                    {
                        if (Path.GetFileName(mod).StartsWith("ZombieBuddy", StringComparison.OrdinalIgnoreCase)) return true;
                        foreach (var mi in new[] { Path.Combine(mod, "mod.info"), Path.Combine(mod, "42", "mod.info") })
                            if (File.Exists(mi) && File.ReadAllText(mi).IndexOf("javaJarFile", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        // true = Installation wurde angestossen (Spielstart dann abbrechen, Nutzer klickt danach erneut)
        bool OfferZombieBuddy()
        {
            if (ZbInstalled() || !ZbDetected()) return false;
            var pr = LoadPrefs();
            if (Util.S(pr, "zbOffer") == "no") return false;
            if (Loc.Show(this, "ZombieBuddy (Java-Mods) wurde erkannt, ist aber nicht eingerichtet.\r\nJetzt installieren? (neueste Version + B42-Fix)", "ZombieBuddy", MessageBoxButtons.YesNo) != DialogResult.Yes)
            { pr["zbOffer"] = "no"; try { Util.WriteJson(Path.Combine(Util.DataDir, "prefs.json"), pr); } catch { } return false; }
            using (var f = new ZbForm((int)numRam.Value) { Auto = true }) f.ShowDialog(this);
            AppendLog("ZombieBuddy-Installation beendet - bitte 'Spielen' erneut klicken.");
            return true;
        }

        void Play()
        {
            try
            {
                if (OfferZombieBuddy()) return;
                if (curServer != null && curServer.IsCollection)
                {
                    Process.Start("steam://rungameid/108600");
                    AppendLog("Spiel wird ueber Steam gestartet (Kollektion: Mods vorher dort abonnieren, im Spiel unter 'Mods' aktivieren) ...");
                    return;
                }
                var why = UpdateReason();
                if (why != null)
                {
                    if (Loc.Show(this, "Deine Installation passt nicht zum Server (" + why + ").\r\nJetzt aktualisieren?", "Aktualisieren noetig",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) RunInstall(null);
                    return;
                }
                var pz = SteamInfo.PZInstallDir();
                if (pz != null)
                {
                    var inst = new Installer { Log = AppendLog, ZbPin = Util.D(manifest, "zombieBuddy") };
                    inst.ApproveWorkshopJavaMods(Util.L(Util.D(manifest, "workshopItems"), "wids"));
                    try { inst.EnsureMenuMods(); } catch (Exception ex) { AppendLog("WARNUNG - default.txt (Horse): " + ex.Message); }
                    if (File.Exists(Path.Combine(Util.ZomboidDir, "mods", "ZombieBuddyFix", "libs", "ZombieBuddy.jar")))
                        inst.SetupZombieBuddy(pz, (int)numRam.Value); // nach Steam-Updates erneut sicherstellen
                }
                SavePrefs();
                var a = ServerAddress();
                if (a != "") { Clipboard.SetText(a); AppendLog("Server-Adresse in der Zwischenablage: " + a); }
                Process.Start("steam://rungameid/108600");
                AppendLog("Spiel wird ueber Steam gestartet ...");
            }
            catch (Exception e) { AppendLog("FEHLER: " + e.Message); }
        }

        // ---------- Prefs / UI-Helfer
        Dictionary<string, object> LoadPrefs() { return Util.ReadJson(Path.Combine(Util.DataDir, "prefs.json")); }
        void SavePrefs()
        {
            var p = LoadPrefs(); p["ramGb"] = (int)numRam.Value;
            Util.WriteJson(Path.Combine(Util.DataDir, "prefs.json"), p);
        }

        void RunTask(string name, Action<CancellationToken> work)
        {
            cts = new CancellationTokenSource();
            var ct = cts.Token;
            SetBusy(true);
            AppendLog("=== " + name + " gestartet " + DateTime.Now.ToString("HH:mm:ss") + " ===");
            var th = new Thread(() =>
            {
                try { work(ct); SetStatus(name + " abgeschlossen."); }
                catch (OperationCanceledException) { AppendLog("Abgebrochen."); SetStatus("Abgebrochen."); }
                catch (Exception e) { AppendLog("FEHLER: " + e.Message); SetStatus("Fehler: " + e.Message); }
                finally { BeginInvoke((Action)(() => SetBusy(false))); }
            }) { IsBackground = true };
            th.Start();
        }

        void SetBusy(bool b)
        {
            btnInstall.Enabled = !b; btnPlay.Enabled = !b; btnCancel.Enabled = b;
            if (!b) bar.Value = 0;
        }

        void AppendLog(string s)
        {
            if (InvokeRequired) { BeginInvoke((Action<string>)AppendLog, s); return; }
            log.AppendText(Loc.T(s) + "\r\n");
            try { File.AppendAllText(Path.Combine(Util.DataDir, "launcher.log"), DateTime.Now.ToString("s") + " " + s + "\r\n"); } catch { }
        }

        long lastUi;
        void SetProgress(string what, long done, long total)
        {
            if (InvokeRequired)
            {
                var now = Environment.TickCount;
                if (done < total && now - lastUi < 150) return;
                lastUi = now;
                BeginInvoke((Action<string, long, long>)SetProgress, what, done, total);
                return;
            }
            if (total > 0)
            {
                bar.Maximum = 1000; bar.Value = (int)Math.Max(0, Math.Min(1000, done * 1000 / total));
                bool bytes = total > 100000;
                status.Text = what + ": " + (bytes ? Util.Size(done) + " / " + Util.Size(total) : done + " / " + total) + "  (" + (done * 100 / total) + " %)";
            }
            else status.Text = what + ": " + Util.Size(done);
        }

        void SetStatus(string s)
        {
            if (InvokeRequired) { BeginInvoke((Action<string>)SetStatus, s); return; }
            status.Text = Loc.T(s);
        }
    }

    // ------------------------------------------------------------------ Nur ZombieBuddy installieren
    class ZbForm : Form
    {
        protected override void OnLoad(EventArgs e) { base.OnLoad(e); Loc.Apply(this); }
        const string FIX_WID = "3809837933";
        ComboBox cbVer, cbVar; NumericUpDown numRam; TextBox log; Button bLoad, bInst; Label lblStat;
        public bool Auto; bool autoDone;
        void AutoRun() { if (!Auto || autoDone) return; autoDone = true; cbVar.SelectedIndex = 1; DoInstall(); }   // Rueckfrage bestaetigt: Fix-Variante mit neuester DLL direkt installieren
        readonly Dictionary<string, string[]> rel = new Dictionary<string, string[]>(); // Tag -> {jarUrl, dllUrl}

        public ZbForm(int ramGb)
        {
            Text = "ZombieBuddy installieren"; Width = 760; Height = 600; StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            Controls.Add(new Label { Text = "Version (GitHub-Releases von zed-0xff/ZombieBuddy):", AutoSize = true, Location = new Point(14, 16) });
            cbVer = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(14, 38), Width = 300 };
            bLoad = new Button { Text = "Versionen neu laden", Location = new Point(324, 36), Width = 160 };
            Controls.Add(new Label { Text = "Jar-Variante:", AutoSize = true, Location = new Point(14, 74) });
            cbVar = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(14, 96), Width = 470 };
            cbVar.Items.Add("Original (Jar + DLL aus dem gewaehlten GitHub-Release)");
            cbVar.Items.Add("Compatibility-Fix (Workshop " + FIX_WID + ", Jar + DLL aus dem Fix-Mod)");
            cbVar.SelectedIndex = 0;
            Controls.Add(new Label { Text = "RAM fuer das Spiel (GB, 0 = nicht aendern):", AutoSize = true, Location = new Point(14, 132) });
            numRam = new NumericUpDown { Minimum = 0, Maximum = 64, Value = Math.Max(0, Math.Min(64, ramGb)), Location = new Point(300, 128), Width = 60 };
            bInst = new Button { Text = "Installieren", Location = new Point(14, 164), Width = 200, Height = 34, Enabled = false };
            lblStat = new Label { Text = "Lade Versionen ...", AutoSize = true, Location = new Point(226, 172) };
            log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Location = new Point(14, 210), Width = 716, Height = 340 };
            Controls.AddRange(new Control[] { cbVer, bLoad, cbVar, numRam, bInst, lblStat, log });
            cbVar.SelectedIndexChanged += (s, e) => { cbVer.Enabled = cbVar.SelectedIndex == 0; bInst.Enabled = cbVar.SelectedIndex == 1 || cbVer.Items.Count > 0; };
            bLoad.Click += (s, e) => LoadReleases();
            bInst.Click += (s, e) => DoInstall();
            Shown += (s, e) => LoadReleases();
        }

        void L(string t) { if (IsHandleCreated) BeginInvoke((Action)(() => log.AppendText(t + "\r\n"))); }

        void LoadReleases()
        {
            bLoad.Enabled = false; bInst.Enabled = false; lblStat.Text = "Lade Versionen ...";
            new Thread(() =>
            {
                try
                {
                    var json = Downloader.GetString("https://api.github.com/repos/zed-0xff/ZombieBuddy/releases?per_page=50", new Dictionary<string, string> { { "Accept", "application/vnd.github+json" } });
                    var rels = Util.Json.Deserialize<List<object>>(json).Cast<Dictionary<string, object>>();
                    var tmp = new List<KeyValuePair<string, string[]>>();
                    foreach (var r in rels)
                    {
                        if (r.ContainsKey("draft") && Equals(r["draft"], true)) continue;
                        var jar = Util.L2(r, "assets").FirstOrDefault(x => Util.S(x, "name").Equals("ZombieBuddy.jar", StringComparison.OrdinalIgnoreCase));
                        var dll = Util.L2(r, "assets").FirstOrDefault(x => Util.S(x, "name").Equals("zbNative.dll", StringComparison.OrdinalIgnoreCase));
                        if (jar == null || dll == null) continue;
                        var tag = Util.S(r, "tag_name") + (Equals(r.ContainsKey("prerelease") ? r["prerelease"] : false, true) ? " (Vorabversion)" : "");
                        tmp.Add(new KeyValuePair<string, string[]>(tag, new[] { Util.S(jar, "browser_download_url"), Util.S(dll, "browser_download_url") }));
                    }
                    BeginInvoke((Action)(() =>
                    {
                        rel.Clear(); cbVer.Items.Clear();
                        foreach (var kv in tmp) { rel[kv.Key] = kv.Value; cbVer.Items.Add(kv.Key); }
                        if (cbVer.Items.Count > 0) cbVer.SelectedIndex = 0;
                        lblStat.Text = tmp.Count + " Version(en) mit Jar + DLL gefunden."; bLoad.Enabled = true; bInst.Enabled = true; AutoRun();
                    }));
                }
                catch (Exception ex) { BeginInvoke((Action)(() => { lblStat.Text = "GitHub nicht erreichbar: " + ex.Message; bLoad.Enabled = true; bInst.Enabled = cbVar.SelectedIndex == 1; AutoRun(); })); }
            }) { IsBackground = true }.Start();
        }

        // Fix-Mod suchen: Zomboid\mods\ZombieBuddyFix oder Workshop-Ordner (alle Steam-Bibliotheken)
        static string FindFixLibs()
        {
            var cands = new List<string> { Path.Combine(Util.ZomboidDir, "mods", "ZombieBuddyFix", "libs") };
            foreach (var lib in SteamInfo.Libraries())
            {
                var m = Path.Combine(lib, "steamapps", "workshop", "content", "108600", FIX_WID, "mods");
                if (Directory.Exists(m)) foreach (var d in Directory.GetDirectories(m)) cands.Add(Path.Combine(d, "libs"));
            }
            return cands.FirstOrDefault(c => File.Exists(Path.Combine(c, "ZombieBuddy.jar")));
        }

        void DoInstall()
        {
            var useFix = cbVar.SelectedIndex == 1; var ram = (int)numRam.Value;
            var verKey = cbVer.SelectedItem as string;
            if (!useFix && (verKey == null || !rel.ContainsKey(verKey))) { Loc.Show(this, "Keine Version gewaehlt (GitHub nicht erreichbar?).", "ZombieBuddy"); return; }
            if (Process.GetProcessesByName("ProjectZomboid64").Length > 0) { Loc.Show(this, "Project Zomboid laeuft noch - bitte zuerst beenden.", "ZombieBuddy"); return; }
            bInst.Enabled = false; bLoad.Enabled = false;
            var urls = useFix ? null : rel[verKey];
            var firstRel = rel.Values.FirstOrDefault();
            new Thread(() =>
            {
                try
                {
                    var pz = SteamInfo.PZInstallDir();
                    if (pz == null) throw new Exception("Project Zomboid wurde nicht gefunden. Ist es ueber Steam installiert?");
                    L("Project Zomboid: " + pz);
                    var tmpDir = Path.Combine(Util.DataDir, "zb_install"); Directory.CreateDirectory(tmpDir);
                    string jar, dll, what;
                    if (useFix)
                    {
                        var libs = FindFixLibs();
                        if (libs == null) { try { Process.Start("steam://openurl/https://steamcommunity.com/sharedfiles/filedetails/?id=" + FIX_WID); L("Workshop-Seite des Fixes wird in Steam geoeffnet - bitte abonnieren, danach hier erneut 'Installieren' klicken."); } catch { } }
                        if (libs == null) throw new Exception("Der Compatibility-Fix wurde nicht gefunden. Bitte Workshop-Item " + FIX_WID + " in Steam abonnieren (oder den Mod 'ZombieBuddyFix' nach Zomboid\\mods legen) und erneut versuchen.");
                        L("Fix-Mod gefunden: " + libs);
                        jar = Path.Combine(libs, "ZombieBuddy.jar"); dll = Path.Combine(libs, "zbNative.dll");
                        if (!File.Exists(dll))
                        {
                            if (firstRel == null) throw new Exception("Im Fix-Mod liegt keine zbNative.dll und GitHub ist nicht erreichbar.");
                            dll = Path.Combine(tmpDir, "zbNative.dll"); L("zbNative.dll fehlt im Fix-Mod - lade die neueste vom GitHub ..."); Downloader.GetFile(firstRel[1], dll);
                        }
                        what = "Compatibility-Fix (Workshop " + FIX_WID + ")";
                    }
                    else
                    {
                        jar = Path.Combine(tmpDir, "ZombieBuddy.jar"); dll = Path.Combine(tmpDir, "zbNative.dll");
                        L("Lade " + verKey + " ..."); Downloader.GetFile(urls[0], jar); Downloader.GetFile(urls[1], dll);
                        var jl = new FileInfo(jar).Length; var dl = new FileInfo(dll).Length;
                        if (jl < 100 * 1024 || dl < 1024 || dl > 5 * 1024 * 1024) throw new Exception("Unerwartete Dateigroessen (Jar " + jl + ", DLL " + dl + " Bytes) - abgebrochen.");
                        what = "Original " + verKey;
                    }
                    new Installer { Log = L }.InstallZbFiles(pz, jar, dll, ram);
                    L("");
                    L("=== FERTIG: ZombieBuddy (" + what + ") ist installiert ===");
                    L("ProjectZomboid64.json wurde mit -agentlib:zbNative gepatcht (Sicherung: ProjectZomboid64.json.vfa-backup). Damit startet der normale Steam-Start (Play) schon mit ZombieBuddy.");
                    L("");
                    L("NOCH ZU TUN / WICHTIG:");
                    L("1) Steam-Startoptionen (nur noetig fuer den 'Alternate Launch' oder wenn du ZombieBuddy zusaetzlich ueber Steam erzwingen willst):");
                    L("   Steam > Bibliothek > Project Zomboid > Rechtsklick > Eigenschaften > Allgemein > Startoptionen, dort eintragen:");
                    L("      -agentlib:zbNative --");
                    L("   (Ohne Rueckfrage bei Java-Mods: -agentlib:zbNative=policy=allow-all --   |   nie neue Jars: -agentlib:zbNative=policy=deny-new --)");
                    L("   Dieses Programm aendert Steams Startoptionen NICHT selbst (dafuer muesste Steam beendet werden).");
                    L("2) Mods mit Java-Teil (mod.info: javaJarFile) brauchen den Mod 'ZombieBuddy' in der Mod-Liste (require=\\ZombieBuddy).");
                    L("3) Beim ersten Start fragt ZombieBuddy bei jedem Java-Mod nach Freigabe - 'Ja' und 'dauerhaft' waehlen (gespeichert in %USERPROFILE%\\.zombie_buddy\\mod_approvals.json).");
                    L("4) Nach einem Spiel-Update pruefen, ob ProjectZomboid64.json noch gepatcht ist (sonst hier erneut installieren).");
                    L("5) Rueckgaengig: ProjectZomboid64.json.vfa-backup nach ProjectZomboid64.json zurueckkopieren; ZombieBuddy.jar und zbNative.dll im Spielordner loeschen.");
                }
                catch (Exception ex) { L("FEHLER: " + ex.Message); }
                finally { BeginInvoke((Action)(() => { bInst.Enabled = true; bLoad.Enabled = true; })); }
            }) { IsBackground = true }.Start();
        }
    }

    class ModListForm : Form
    {
        class Row { public string Wid, Name, Folders, Ids; public bool Java, Present; }
        readonly List<Row> rows = new List<Row>();
        readonly HashSet<string> sel;
        readonly ListView lv; readonly TextBox search; readonly ComboBox filter; readonly Label info;
        bool filling;
        public List<string> Selected { get { return rows.Where(r => sel.Contains(r.Wid)).Select(r => r.Wid).Concat(sel.Where(w => !rows.Any(r => r.Wid == w))).ToList(); } }

        public ModListForm(string workshopDir, List<string> packWids, List<string> selected, MainForm owner)
        {
            sel = new HashSet<string>(selected);
            Text = "Mod-Liste (aktuelles Pack) - Haken = laedt ueber Steam-Workshop (WorkshopItems=)";
            Width = 1100; Height = 800; StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(22, 22, 26); ForeColor = Color.FromArgb(230, 230, 230);
            Cursor = Cursors.WaitCursor;
            foreach (var w in packWids.Distinct())
            {
                var r = new Row { Wid = w };
                var md = Path.Combine(workshopDir ?? "", w, "mods");
                var names = new List<string>(); var ids = new List<string>(); var folders = new List<string>();
                if (Directory.Exists(md))
                {
                    r.Present = true;
                    foreach (var d in Directory.GetDirectories(md))
                    {
                        folders.Add(Path.GetFileName(d));
                        var p = Checker.PickModInfo(d);
                        if (p == null) continue;
                        var mi = Checker.Parse(p, Path.GetFileName(d), w);
                        if (!string.IsNullOrEmpty(mi.Name) && !names.Contains(mi.Name)) names.Add(mi.Name);
                        if (!string.IsNullOrEmpty(mi.Id)) ids.Add(mi.Id);
                        r.Java |= mi.JavaJar;
                    }
                }
                r.Name = names.Count > 0 ? string.Join(" / ", names) : (r.Present ? "?" : "(nicht im Workshop-Ordner)");
                r.Ids = string.Join(", ", ids); r.Folders = string.Join(", ", folders);
                rows.Add(r);
            }
            Cursor = Cursors.Default;

            var top = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };
            top.Controls.Add(new Label { Text = "Suche:", Location = new Point(8, 13), AutoSize = true });
            search = new TextBox { Location = new Point(60, 10), Width = 380 };
            filter = new ComboBox { Location = new Point(450, 9), Width = 170, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(14, 14, 17), ForeColor = Color.FromArgb(230, 230, 230) };
            filter.Items.AddRange(new object[] { "Alle", "Nur angehakt", "Nur nicht angehakt", "Nur Java-Mods" }); filter.SelectedIndex = 0;
            info = new Label { Location = new Point(635, 13), AutoSize = true };
            top.Controls.AddRange(new Control[] { search, filter, info });

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 52 };
            var bAll = new Button { Text = "Sichtbare anhaken", Location = new Point(8, 10), Width = 160, Height = 32 };
            var bNone = new Button { Text = "Sichtbare abhaken", Location = new Point(176, 10), Width = 160, Height = 32 };
            var bOk = new Button { Text = "Uebernehmen", Location = new Point(760, 10), Width = 150, Height = 32, BackColor = Color.FromArgb(178, 34, 34), ForeColor = Color.White };
            var bCancel = new Button { Text = "Abbrechen", Location = new Point(920, 10), Width = 150, Height = 32, DialogResult = DialogResult.Cancel };
            bottom.Controls.Add(new Label { Text = "Doppelklick = Steam-Seite", Location = new Point(350, 18), AutoSize = true, ForeColor = Color.FromArgb(160, 160, 168) });
            bAll.Click += (s, e) => SetVisible(true); bNone.Click += (s, e) => SetVisible(false);
            bOk.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            var right = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 330, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 10, 8, 0) };
            bOk.Margin = bCancel.Margin = new Padding(4, 0, 4, 0);
            right.Controls.Add(bCancel); right.Controls.Add(bOk);
            bottom.Controls.AddRange(new Control[] { right, bAll, bNone });
            CancelButton = bCancel;

            lv = new ListView { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true, GridLines = false, HideSelection = false,
                                BackColor = Color.FromArgb(14, 14, 17), ForeColor = Color.FromArgb(230, 230, 230), BorderStyle = BorderStyle.None };
            lv.Columns.Add("Workshop-ID", 110); lv.Columns.Add("Name", 380); lv.Columns.Add("Mod-IDs", 270); lv.Columns.Add("Ordner", 220); lv.Columns.Add("Java", 50);
            lv.ItemChecked += (s, e) =>
            {
                if (filling) return;
                var w = (string)e.Item.Tag;
                if (e.Item.Checked) sel.Add(w); else sel.Remove(w);
                UpdateInfo();
            };
            lv.DoubleClick += (s, e) => { if (lv.SelectedItems.Count > 0) try { Process.Start("https://steamcommunity.com/sharedfiles/filedetails/?id=" + lv.SelectedItems[0].Tag); } catch { } };
            int sortCol = -1; bool asc = true;
            lv.ColumnClick += (s, e) => { asc = sortCol == e.Column ? !asc : true; sortCol = e.Column; Sort(e.Column, asc); };

            Controls.Add(lv); Controls.Add(bottom); Controls.Add(top);
            owner.ApplyTheme(this); Loc.Apply(this);
            search.TextChanged += (s, e) => Fill();
            filter.SelectedIndexChanged += (s, e) => Fill();
            Fill();
        }

        void Sort(int col, bool asc)
        {
            Func<Row, string> key = col == 0 ? (Func<Row, string>)(r => r.Wid.PadLeft(12, '0')) : col == 1 ? (r => r.Name) : col == 2 ? (r => r.Ids) : col == 3 ? (Func<Row, string>)(r => r.Folders) : (r => r.Java ? "1" : "0");
            var sorted = asc ? rows.OrderBy(key, StringComparer.OrdinalIgnoreCase).ToList() : rows.OrderByDescending(key, StringComparer.OrdinalIgnoreCase).ToList();
            rows.Clear(); rows.AddRange(sorted); Fill();
        }

        IEnumerable<Row> Visible()
        {
            var q = search.Text.Trim();
            return rows.Where(r =>
                (q == "" || r.Wid.Contains(q) || r.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || r.Ids.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || r.Folders.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                && (filter.SelectedIndex != 1 || sel.Contains(r.Wid)) && (filter.SelectedIndex != 2 || !sel.Contains(r.Wid)) && (filter.SelectedIndex != 3 || r.Java));
        }

        void Fill()
        {
            filling = true;
            lv.BeginUpdate(); lv.Items.Clear();
            foreach (var r in Visible())
            {
                var it = new ListViewItem(new[] { r.Wid, r.Name, r.Ids, r.Folders, r.Java ? "ja" : "" }) { Tag = r.Wid, Checked = sel.Contains(r.Wid) };
                if (!r.Present) it.ForeColor = Color.FromArgb(235, 80, 80);
                else if (r.Java) it.ForeColor = Color.FromArgb(240, 190, 80);
                lv.Items.Add(it);
            }
            lv.EndUpdate(); filling = false;
            UpdateInfo();
        }

        void SetVisible(bool on)
        {
            foreach (var r in Visible()) { if (on) sel.Add(r.Wid); else sel.Remove(r.Wid); }
            Fill();
        }

        void UpdateInfo() { info.Text = lv.Items.Count + " angezeigt / " + rows.Count + " im Pack  |  " + rows.Count(r => sel.Contains(r.Wid)) + " ueber Workshop"; }
    }

    static class Program
    {
        static void Crash(Exception e)
        {
            var msg = e == null ? "unbekannter Fehler" : e.ToString();
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "VFALauncher_crash.log"), DateTime.Now.ToString("s") + "\r\n" + msg + "\r\n\r\n"); } catch { }
            try { File.AppendAllText(Path.Combine(Util.ExeDir, "VFALauncher_crash.log"), DateTime.Now.ToString("s") + "\r\n" + msg + "\r\n\r\n"); } catch { }
            try { Loc.Show(msg, "Launcher-Fehler (Details in VFALauncher_crash.log)", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
        }

        [STAThread]
        static void Main(string[] args)
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Crash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Crash(e.ExceptionObject as Exception);
            try { Run(args); } catch (Exception e) { Crash(e); }
        }

        static void Run(string[] args)
        {
            try { File.AppendAllText(Path.Combine(Util.DataDir, "launcher.log"), DateTime.Now.ToString("s") + " Start " + string.Join(" ", args) + " (.NET " + Environment.Version + ", " + Environment.OSVersion + ")\r\n"); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool dev = args.Any(a => a.Equals("--dev", StringComparison.OrdinalIgnoreCase)) || File.Exists(Path.Combine(Util.ExeDir, "dev.flag"));
            Loc.Init();
            Application.Run(new MainForm(dev));
        }
    }
}
