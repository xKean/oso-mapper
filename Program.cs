using OsuMapGenerator.Audio;
using OsuMapGenerator.Core;
using OsuMapGenerator.Models;
using Newtonsoft.Json;
using System.Globalization;

namespace OsuMapGenerator;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== OSU MAP GENERATOR ===");
        Console.WriteLine("Generiert Osu-ähnliche Maps aus MP3-Dateien\n");

        try
        {
            string mp3FilePath;
            DifficultyLevel baseDifficulty;

            // Parse command line arguments or get user input
            if (args.Length >= 2)
            {
                mp3FilePath = args[0];
                if (!Enum.TryParse<DifficultyLevel>(args[1], true, out baseDifficulty))
                {
                    Console.WriteLine("Ungültiger Schwierigkeitsgrad. Verwende 'Normal'.");
                    baseDifficulty = DifficultyLevel.Normal;
                }
            }
            else
            {
                mp3FilePath = GetMp3FilePath();
                baseDifficulty = GetDifficultyFromUserWithDefault(DifficultyLevel.Normal);
            }

            if (!File.Exists(mp3FilePath))
            {
                Console.WriteLine($"Fehler: Datei '{mp3FilePath}' nicht gefunden!");
                return;
            }

            Console.WriteLine($"Analysiere MP3-Datei: {Path.GetFileName(mp3FilePath)}");
            Console.WriteLine($"Basis-Schwierigkeit: {baseDifficulty}");
            Console.WriteLine("Bitte warten...\n");

            // Analyze audio
            var audioAnalyzer = new AudioAnalyzer();
            var audioAnalysis = audioAnalyzer.AnalyzeAudio(mp3FilePath);

            Console.WriteLine($"Audio-Analyse abgeschlossen:");
            Console.WriteLine($"  Dauer: {audioAnalysis.Duration:F1} Sekunden");
            Console.WriteLine($"  Geschätztes BPM: {audioAnalysis.EstimatedBPM:F1}");
            Console.WriteLine($"  Beats erkannt: {audioAnalysis.BeatTimestamps.Count}");

            // Interaktive Konfiguration (Preset mit optionalen Overrides)
            var settings = DifficultySettings.GetSettings(baseDifficulty);

            Console.WriteLine("\nKonfiguration (Enter = Default übernehmen):");
            settings.NotesPerMinute = PromptInt($"Noten pro Minute", settings.NotesPerMinute, 10, 1000);
            settings.BeatSensitivity = PromptFloat($"Beat-Sensitivität (0.1-1.5)", settings.BeatSensitivity, 0.1f, 1.5f);
            settings.MinTimeBetweenNotes = PromptFloat($"Min. Zeit zwischen Noten (s)", settings.MinTimeBetweenNotes, 0.05f, 2.0f);
            settings.MinNodeDistancePx = PromptFloat($"Min. Abstand zwischen Nodes (px)", settings.MinNodeDistancePx, 10f, 1000f);

            // Generate map mit individuellen Settings
            var mapGenerator = new MapGenerator();
            var gameMap = mapGenerator.GenerateMap(
                audioAnalysis,
                settings,
                Path.GetFileNameWithoutExtension(mp3FilePath),
                baseDifficulty
            );

            Console.WriteLine($"\nMap generiert:");
            Console.WriteLine($"  Anzahl Noten: {gameMap.Notes.Length}");
            Console.WriteLine($"  Noten pro Minute: {(gameMap.Notes.Length / audioAnalysis.Duration * 60):F0}");

            // Identifiers am Ende abfragen
            Console.WriteLine("\nZusätzliche Identifikatoren (Enter = leer lassen):");
            Console.Write("SongIdentifier: ");
            var songIdentifier = Console.ReadLine() ?? "";
            Console.Write("DifficultyIdentifier: ");
            var difficultyIdentifier = Console.ReadLine() ?? "";

            // Save as JSON
            var outputFileName = $"{Path.GetFileNameWithoutExtension(mp3FilePath)}_{baseDifficulty}.json";
            var outputPath = Path.Combine(Path.GetDirectoryName(mp3FilePath) ?? "", outputFileName);

            await SaveMapAsJson(gameMap, outputPath, songIdentifier, difficultyIdentifier);

            Console.WriteLine($"\nMap gespeichert als: {outputFileName}");

            // Show sample data
            ShowMapPreview(gameMap);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fehler: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Details: {ex.InnerException.Message}");
            }
        }

        Console.WriteLine("\nDrücke eine beliebige Taste zum Beenden...");
        Console.ReadKey();
    }

    private static string GetMp3FilePath()
    {
        while (true)
        {
            Console.Write("MP3-Dateipfad eingeben: ");
            var path = Console.ReadLine()?.Trim().Trim('"');

            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                return path;
            }

            Console.WriteLine("Datei nicht gefunden. Bitte erneut versuchen.");
        }
    }

    private static DifficultyLevel GetDifficultyFromUserWithDefault(DifficultyLevel defaultDifficulty)
    {
        Console.WriteLine("\nSchwierigkeitsstufen:");
        Console.WriteLine("1 - Easy (60 Noten/Min)");
        Console.WriteLine("2 - Normal (120 Noten/Min)");
        Console.WriteLine("3 - Hard (200 Noten/Min)");
        Console.WriteLine("4 - Expert (300 Noten/Min)");
        Console.WriteLine("5 - Master (450 Noten/Min)");

        while (true)
        {
            Console.Write($"\nSchwierigkeit wählen (1-5) [{(int)defaultDifficulty}]: ");
            var input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
            {
                return defaultDifficulty;
            }

            if (int.TryParse(input, out var choice) && choice >= 1 && choice <= 5)
            {
                return (DifficultyLevel)choice;
            }

            Console.WriteLine("Ungültige Eingabe. Bitte 1-5 eingeben oder Enter für Default.");
        }
    }

    private static int PromptInt(string label, int defaultValue, int min, int max)
    {
        while (true)
        {
            Console.Write($"{label} [{defaultValue}]: ");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) return defaultValue;
            if (int.TryParse(input.Trim(), out var value))
            {
                value = Math.Clamp(value, min, max);
                return value;
            }
            Console.WriteLine("Ungültige Zahl. Bitte erneut versuchen.");
        }
    }

    private static float PromptFloat(string label, float defaultValue, float min, float max)
    {
        while (true)
        {
            Console.Write($"{label} [{defaultValue.ToString(CultureInfo.InvariantCulture)}]: ");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) return defaultValue;

            input = input.Trim().Replace(',', '.');
            if (float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                value = Math.Clamp(value, min, max);
                return value;
            }
            Console.WriteLine("Ungültige Zahl. Bitte erneut versuchen.");
        }
    }

    private static async Task SaveMapAsJson(GameMap map, string filePath, string songIdentifier = "", string difficultyIdentifier = "")
    {
        var jsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            Culture = CultureInfo.InvariantCulture
        };

        // Convert to DataTable format
        var dataTableFormat = map.ToDataTableFormat(songIdentifier: songIdentifier, difficultyIdentifier: difficultyIdentifier);
        var json = JsonConvert.SerializeObject(dataTableFormat, jsonSettings);
        await File.WriteAllTextAsync(filePath, json);
    }

    private static void ShowMapPreview(GameMap map)
    {
        Console.WriteLine("\n=== MAP VORSCHAU ===");
        Console.WriteLine($"Song: {map.Metadata.SongName}");
        Console.WriteLine($"Schwierigkeit: {map.Metadata.Difficulty}");
        Console.WriteLine($"BPM: {map.Metadata.BPM:F1}");
        Console.WriteLine($"Dauer: {map.Metadata.Duration:F1}s");
        Console.WriteLine($"Anzahl Noten: {map.Metadata.NoteCount}");

        Console.WriteLine("\nErste 5 Noten:");
        for (int i = 0; i < Math.Min(5, map.Notes.Length); i++)
        {
            var note = map.Notes[i];
            Console.WriteLine($"  {i + 1}. Zeit: {note.Time:F2}s, Position: X={note.X:F0}, Y={note.Y:F0} (Breite), Z={note.Z:F0} (Höhe)");
        }

        if (map.Notes.Length > 5)
        {
            Console.WriteLine($"  ... und {map.Notes.Length - 5} weitere Noten");
        }
    }
}
