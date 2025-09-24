## OSU Songmapper – Anleitung

Diese App generiert aus einer MP3-Datei eine osu!-ähnliche Map und speichert sie als JSON (DataTable-Format).

### 1) Nutzung – fertige EXE (empfohlen)
- Baue eine selbstenthaltende Single-File-EXE (wurde bereits gezeigt):
  - Release liegt typischerweise hier: `bin/Release/net9.0-windows/win-x64/publish/OsuMapGenerator.exe`
- Starte die EXE per Doppelklick oder im Terminal:

```bash
OsuMapGenerator.exe
```

Du wirst interaktiv durch Preset‑Wahl und Konfiguration geführt.

Optional kannst du direkt MP3‑Pfad und Preset mitgeben (Rest bleibt interaktiv):

```bash
OsuMapGenerator.exe "C:\\Pfad\\zu\\song.mp3" Normal
```

Gültige Presets: `Easy`, `Normal`, `Hard`, `Expert`, `Master`.

### 2) Nutzung – aus dem Source
- Voraussetzungen: .NET SDK 9 (zum Bauen/Starten)

Direkt starten:

```bash
dotnet run --configuration Release
```

Self‑contained Single‑File‑Build (zum Versenden):

```bash
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeAllContentForSelfExtract=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:PublishTrimmed=false
```

Die EXE findest du danach unter `bin/Release/net9.0-windows/win-x64/publish/OsuMapGenerator.exe`.

### 3) Interaktive Konfiguration
Nach dem Einlesen der MP3 und Preset‑Wahl wirst du nach folgenden Werten gefragt. Der Default wird in eckigen Klammern angezeigt; Enter übernimmt den Default.
- Noten pro Minute (NPM)
- Beat‑Sensitivität (0.1–1.5)
- Mindestzeit zwischen Noten (Sekunden)
- Mindestabstand zwischen Nodes (Pixel)

Am Ende wirst du zusätzlich nach diesen Identifikatoren gefragt (frei wählbar, auch leer möglich):
- SongIdentifier
- DifficultyIdentifier

Die Ausgabe‑Datei wird als `<Songname>_<Preset>.json` neben der MP3 gespeichert.

### 4) Ausgabeformat (JSON)
Die JSON enthält eine Liste von Zeilen (DataTable‑Format):

```json
[
  {
    "Position": { "X": 0, "Y": 1234, "Z": 567 },
    "TimeSec": 12.34,
    "Index": 0,
    "Name": "Row_000",
    "SongIdentifier": "<dein-wert>",
    "DifficultyIdentifier": "<dein-wert>"
  }
]
```

Hinweise:
- Achsen: `Y` ist die Breite (0..ScreenWidth), `Z` ist die Höhe (0..ScreenHeight), `X` ist immer 0.
- Der Mindestabstand zwischen Nodes wird erzwungen (konfigurierbar). Positionen werden stets innerhalb der Bildschirmgrenzen gehalten.

### 5) Presets (Defaults)
Jedes Preset setzt sinnvolle Startwerte (NPM, Sensitivität, min. Zeit, min. Node‑Abstand). Du kannst jeden Wert interaktiv überschreiben.

### 6) Troubleshooting
- Die EXE startet nicht: Stelle sicher, dass du die EXE aus dem `publish`‑Ordner verwendest. Manche Virenscanner blockieren unbekannte Single‑File‑EXEs; ggf. freigeben.
- Kein Ton/Analysefehler: Prüfe, ob die MP3‑Datei existiert und nicht geschützt ist.
- Unerwartete Muster/Abstände: Passe NPM, `MinTimeBetweenNotes` und `MinNodeDistancePx` an.

### 7) Lizenz / Kontakt
Interne Testanwendung. Falls du Anpassungen brauchst, melde dich.

