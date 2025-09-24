using System.Numerics;
using Newtonsoft.Json;

namespace OsuMapGenerator.Models;

public class GameMap
{
    [JsonProperty("notes")]
    public Note[] Notes { get; set; } = Array.Empty<Note>();

    [JsonProperty("metadata")]
    public MapMetadata Metadata { get; set; } = new();

    // Convert to DataTable format as array
    public List<DataTableRow> ToDataTableFormat(bool includeMetadata = false, string songIdentifier = "", string difficultyIdentifier = "")
    {
        var result = new List<DataTableRow>();

        // Add note rows
        for (int i = 0; i < Notes.Length; i++)
        {
            result.Add(new DataTableRow
            {
                Position = new DataTablePosition
                {
                    X = Notes[i].X,
                    Y = Notes[i].Y,
                    Z = Notes[i].Z
                },
                TimeSec = Notes[i].Time,
                Index = i,
                Name = $"Row_{i:D3}",
                SongIdentifier = songIdentifier,
                DifficultyIdentifier = difficultyIdentifier
            });
        }

        return result;
    }
}

public class DataTableRow
{
    [JsonProperty("Position")]
    public DataTablePosition Position { get; set; } = new();

    [JsonProperty("TimeSec")]
    public float TimeSec { get; set; }

    [JsonProperty("Index")]
    public int Index { get; set; }

    [JsonProperty("Name")]
    public string Name { get; set; } = "";

    [JsonProperty("SongIdentifier")]
    public string SongIdentifier { get; set; } = "";

    [JsonProperty("DifficultyIdentifier")]
    public string DifficultyIdentifier { get; set; } = "";
}

public class DataTablePosition
{
    [JsonProperty("X")]
    public float X { get; set; }

    [JsonProperty("Y")]
    public float Y { get; set; }

    [JsonProperty("Z")]
    public float Z { get; set; }
}

public class Note
{
    [JsonProperty("x")]
    public float X { get; set; }

    [JsonProperty("y")]
    public float Y { get; set; }

    [JsonProperty("z")]
    public float Z { get; set; }

    [JsonProperty("time")]
    public float Time { get; set; }

    [JsonProperty("phraseId")]
    public int PhraseId { get; set; }

    [JsonProperty("phrasePosition")]
    public int PhrasePosition { get; set; } // Position innerhalb der Phrase (0, 1, 2, ...)

    public Note() { }

    public Note(Vector3 position, float time, int phraseId = 0, int phrasePosition = 0)
    {
        X = position.X;
        Y = position.Y;
        Z = position.Z;
        Time = time;
        PhraseId = phraseId;
        PhrasePosition = phrasePosition;
    }

    public Vector3 Position => new(X, Y, Z);
}

public class MusicalPhrase
{
    public int Id { get; set; }
    public float StartTime { get; set; }
    public float EndTime { get; set; }
    public List<float> NoteTimes { get; set; } = new();
    public int PatternType { get; set; } // Konsistenter Pattern für die Phrase
    public float IntensityLevel { get; set; } // Durchschnittliche Intensität der Phrase

    public float Duration => EndTime - StartTime;
    public int NoteCount => NoteTimes.Count;
}

public class MapMetadata
{
    [JsonProperty("songName")]
    public string SongName { get; set; } = "";

    [JsonProperty("difficulty")]
    public DifficultyLevel Difficulty { get; set; }

    [JsonProperty("duration")]
    public float Duration { get; set; }

    [JsonProperty("bpm")]
    public float BPM { get; set; }

    [JsonProperty("noteCount")]
    public int NoteCount { get; set; }

    [JsonProperty("created")]
    public DateTime Created { get; set; } = DateTime.Now;
}

public enum DifficultyLevel
{
    Easy = 1,
    Normal = 2,
    Hard = 3,
    Expert = 4,
    Master = 5
}

public class DifficultySettings
{
    public int NotesPerMinute { get; set; }
    public float BeatSensitivity { get; set; }
    public float MinTimeBetweenNotes { get; set; }
    public float MinNodeDistancePx { get; set; }

    public static DifficultySettings GetSettings(DifficultyLevel level)
    {
        return level switch
        {
            DifficultyLevel.Easy => new() { NotesPerMinute = 60, BeatSensitivity = 0.7f, MinTimeBetweenNotes = 0.8f, MinNodeDistancePx = 200f },
            DifficultyLevel.Normal => new() { NotesPerMinute = 120, BeatSensitivity = 0.6f, MinTimeBetweenNotes = 0.5f, MinNodeDistancePx = 175f },
            DifficultyLevel.Hard => new() { NotesPerMinute = 200, BeatSensitivity = 0.5f, MinTimeBetweenNotes = 0.3f, MinNodeDistancePx = 150f },
            DifficultyLevel.Expert => new() { NotesPerMinute = 300, BeatSensitivity = 0.4f, MinTimeBetweenNotes = 0.2f, MinNodeDistancePx = 125f },
            DifficultyLevel.Master => new() { NotesPerMinute = 450, BeatSensitivity = 0.3f, MinTimeBetweenNotes = 0.15f, MinNodeDistancePx = 100f },
            _ => new() { NotesPerMinute = 120, BeatSensitivity = 0.6f, MinTimeBetweenNotes = 0.5f, MinNodeDistancePx = 175f }
        };
    }
}

public enum InstrumentType
{
    None,
    Bass,
    Melody,
    Drums
}

public enum RhythmType
{
    Steady,      // Gleichmäßiger Beat
    Burst,       // Schnelle Bursts
    Syncopated,  // Synkopiert/Off-Beat
    Slow         // Langsame Passage
}

public class InstrumentModifier
{
    public float DistanceMultiplier { get; set; } = 1.0f;
    public int ForcePatternType { get; set; } = -1;
    public string Description { get; set; } = "";
}