using System.Numerics;
using OsuMapGenerator.Models;
using OsuMapGenerator.Audio;

namespace OsuMapGenerator.Core;

public class MapGenerator
{
    private const int ScreenWidth = 2200;
    private const int ScreenHeight = 1100;
    // Entfernt: Mindestabstand kommt nun aus DifficultySettings
    private readonly Random _random = new();
    private AudioAnalysisResult? audioAnalysisResult;

    public GameMap GenerateMap(AudioAnalysisResult audioAnalysis, DifficultyLevel difficulty, string songName)
    {
        var settings = DifficultySettings.GetSettings(difficulty);
        return GenerateMap(audioAnalysis, settings, songName, difficulty);
    }

    public GameMap GenerateMap(AudioAnalysisResult audioAnalysis, DifficultySettings settings, string songName, DifficultyLevel baseDifficulty = DifficultyLevel.Normal)
    {
        // Speichere AudioAnalysis für spätere Verwendung
        audioAnalysisResult = audioAnalysis;

        var (noteTimings, phrases) = GenerateNoteTimings(audioAnalysis, settings);
        var positions = GeneratePositions(noteTimings, audioAnalysis, settings, phrases);

        // Combine positions and timings into Note objects mit Phrasen-Info
        var notes = new Note[noteTimings.Length];
        for (int i = 0; i < noteTimings.Length; i++)
        {
            var (phraseId, phrasePosition) = FindPhraseInfo(noteTimings[i], phrases);
            notes[i] = new Note(positions[i], noteTimings[i], phraseId, phrasePosition);
        }

        return new GameMap
        {
            Notes = notes,
            Metadata = new MapMetadata
            {
                SongName = songName,
                Difficulty = baseDifficulty,
                Duration = audioAnalysis.Duration,
                BPM = audioAnalysis.EstimatedBPM,
                NoteCount = notes.Length
            }
        };
    }

    private (float[] timings, List<MusicalPhrase> phrases) GenerateNoteTimings(AudioAnalysisResult audioAnalysis, DifficultySettings settings)
    {
        // 1. Generiere musikalische Phrasen basierend auf Beats
        var phrases = GenerateMusicalPhrases(audioAnalysis, settings);

        // 2. Generiere Noten innerhalb der Phrasen
        var allNoteTimes = new List<float>();
        foreach (var phrase in phrases)
        {
            var phraseNotes = GenerateNotesForPhrase(phrase, audioAnalysis, settings);
            allNoteTimes.AddRange(phraseNotes);
        }

        // 3. Sort and ensure minimum spacing
        allNoteTimes.Sort();
        var noteTimings = EnforceMinimumSpacing(allNoteTimes, settings.MinTimeBetweenNotes);

        return (noteTimings.ToArray(), phrases);
    }

    private List<MusicalPhrase> GenerateMusicalPhrases(AudioAnalysisResult audioAnalysis, DifficultySettings settings)
    {
        var phrases = new List<MusicalPhrase>();

        // Musikalische Phrasen sind typischerweise 4-8 Beats lang
        var typicalPhraseLength = 60f / audioAnalysis.EstimatedBPM * 4; // 4 Beats in Sekunden
        var minPhraseLength = typicalPhraseLength * 0.75f; // Minimum 3 Beats
        var maxPhraseLength = typicalPhraseLength * 2f;    // Maximum 8 Beats

        var currentTime = 0f;
        var phraseId = 0;

        while (currentTime < audioAnalysis.Duration - 1f) // -1s Puffer am Ende
        {
            // Bestimme Phrasen-Ende basierend auf musikalischen Pausen oder Energy-Drops
            var phraseEnd = FindPhraseEnd(currentTime, minPhraseLength, maxPhraseLength, audioAnalysis);

            // Sammle alle Beats in dieser Phrase
            var phraseBeats = audioAnalysis.BeatTimestamps
                .Where(beat => beat >= currentTime && beat <= phraseEnd)
                .ToList();

            if (phraseBeats.Count >= 2) // Mindestens 2 Beats für eine Phrase
            {
                // Berechne durchschnittliche Intensität der Phrase
                var avgIntensity = GetAverageIntensityForTimeRange(currentTime, phraseEnd, audioAnalysis);

                var phrase = new MusicalPhrase
                {
                    Id = phraseId++,
                    StartTime = currentTime,
                    EndTime = phraseEnd,
                    NoteTimes = phraseBeats,
                    IntensityLevel = avgIntensity,
                    PatternType = _random.Next(0, 8) // Zufälliger Pattern-Typ für Konsistenz
                };

                phrases.Add(phrase);
            }

            // Nächste Phrase startet nach einer kleinen Pause
            currentTime = phraseEnd + 0.2f; // 200ms Pause zwischen Phrasen
        }

        return phrases;
    }

    private float FindPhraseEnd(float startTime, float minLength, float maxLength, AudioAnalysisResult audioAnalysis)
    {
        var idealEnd = startTime + minLength + (float)_random.NextDouble() * (maxLength - minLength);
        var searchStart = startTime + minLength;
        var searchEnd = Math.Min(startTime + maxLength, audioAnalysis.Duration);

        // Suche nach musikalischen Pausen (Energy-Drops)
        var bestPauseTime = idealEnd;
        var lowestEnergy = float.MaxValue;

        for (float t = searchStart; t < searchEnd; t += 0.1f)
        {
            var energy = GetSongIntensityAt(t, audioAnalysis);
            if (energy < lowestEnergy)
            {
                lowestEnergy = energy;
                bestPauseTime = t;
            }
        }

        // Wenn wir eine deutliche Pause gefunden haben, nutze sie
        if (lowestEnergy < GetSongIntensityAt(startTime, audioAnalysis) * 0.4f)
        {
            return bestPauseTime;
        }

        // Sonst nutze ideale Zeit
        return idealEnd;
    }

    private float GetAverageIntensityForTimeRange(float startTime, float endTime, AudioAnalysisResult audioAnalysis)
    {
        var samples = 10;
        var totalIntensity = 0f;
        var step = (endTime - startTime) / samples;

        for (int i = 0; i < samples; i++)
        {
            var sampleTime = startTime + i * step;
            totalIntensity += GetSongIntensityAt(sampleTime, audioAnalysis);
        }

        return totalIntensity / samples;
    }

    private List<float> GenerateNotesForPhrase(MusicalPhrase phrase, AudioAnalysisResult audioAnalysis, DifficultySettings settings)
    {
        var notes = new List<float>();

        // Analysiere Rhythm-Patterns in dieser Phrase
        var rhythmType = AnalyzeRhythmPattern(phrase, audioAnalysis);

        foreach (var beat in phrase.NoteTimes)
        {
            // Basis musikalische Eignung
            var musicalFitness = GetMusicalFitnessAt(beat, audioAnalysis);

            // IMMER Hauptbeats verwenden wenn sie stark genug sind
            if (musicalFitness >= 0.4f) // Viel weniger konservativ!
            {
                notes.Add(beat);
            }

            // Für intensivere Phrasen: Füge Subdivisions basierend auf Rhythm-Pattern hinzu
            if (phrase.IntensityLevel > 0.8f && settings.NotesPerMinute >= 150)
            {
                AddRhythmSubdivisions(beat, phrase, audioAnalysis, settings, notes, rhythmType);
            }
        }

        // Für sehr intensive Phrasen: Noch mehr Noten zwischen den Beats
        if (phrase.IntensityLevel > 1.2f && settings.NotesPerMinute >= 250)
        {
            AddIntensityBursts(phrase, audioAnalysis, settings, notes);
        }

        return notes;
    }

    private RhythmType AnalyzeRhythmPattern(MusicalPhrase phrase, AudioAnalysisResult audioAnalysis)
    {
        if (phrase.NoteTimes.Count < 3) return RhythmType.Steady;

        // Analysiere Beat-Abstände in der Phrase
        var intervals = new List<float>();
        for (int i = 1; i < phrase.NoteTimes.Count; i++)
        {
            intervals.Add(phrase.NoteTimes[i] - phrase.NoteTimes[i - 1]);
        }

        var avgInterval = intervals.Average();
        var variability = intervals.Select(x => Math.Abs(x - avgInterval)).Average();

        // Bestimme Rhythm-Typ basierend auf Variabilität
        if (variability < avgInterval * 0.1f)
            return RhythmType.Steady; // Gleichmäßig
        else if (phrase.IntensityLevel > 1.3f && avgInterval < 0.4f)
            return RhythmType.Burst; // Schnelle Bursts
        else if (avgInterval > 1.2f)
            return RhythmType.Slow; // Langsame Passagen
        else
            return RhythmType.Syncopated; // Synkopiert/unregelmäßig
    }

    private void AddRhythmSubdivisions(float beat, MusicalPhrase phrase, AudioAnalysisResult audioAnalysis, DifficultySettings settings, List<float> notes, RhythmType rhythmType)
    {
        var currentBeatIndex = phrase.NoteTimes.IndexOf(beat);
        if (currentBeatIndex < phrase.NoteTimes.Count - 1)
        {
            var nextBeat = phrase.NoteTimes[currentBeatIndex + 1];
            var interval = nextBeat - beat;

            switch (rhythmType)
            {
                case RhythmType.Burst:
                    // Schnelle 16th note Bursts in intensiven Momenten
                    if (interval > 0.2f)
                    {
                        for (float subdivTime = beat + interval * 0.25f; subdivTime < nextBeat; subdivTime += interval * 0.25f)
                        {
                            if (GetMusicalFitnessAt(subdivTime, audioAnalysis) > 0.3f)
                            {
                                notes.Add(subdivTime);
                            }
                        }
                    }
                    break;

                case RhythmType.Syncopated:
                    // Off-Beat Noten für Synkopierung
                    var offBeatTime = beat + interval * 0.375f; // Zwischen 8th notes
                    if (GetMusicalFitnessAt(offBeatTime, audioAnalysis) > 0.5f)
                    {
                        notes.Add(offBeatTime);
                    }
                    break;

                case RhythmType.Steady:
                    // Normale 8th notes
                    if (interval > settings.MinTimeBetweenNotes * 2)
                    {
                        var eighthNote = beat + interval * 0.5f;
                        if (GetMusicalFitnessAt(eighthNote, audioAnalysis) > 0.4f)
                        {
                            notes.Add(eighthNote);
                        }
                    }
                    break;

                case RhythmType.Slow:
                    // Keine zusätzlichen Noten - langsame Passage
                    break;
            }
        }
    }

    private void AddIntensityBursts(MusicalPhrase phrase, AudioAnalysisResult audioAnalysis, DifficultySettings settings, List<float> notes)
    {
        // Finde Energy-Peaks in der Phrase für zusätzliche Noten
        var timeStep = 0.05f; // Prüfe alle 50ms

        for (float t = phrase.StartTime; t < phrase.EndTime; t += timeStep)
        {
            var currentEnergy = GetSongIntensityAt(t, audioAnalysis);
            var fitness = GetMusicalFitnessAt(t, audioAnalysis);

            // Sehr hohe Energy-Peaks verdienen extra Noten
            if (currentEnergy > 1.6f && fitness > 0.6f)
            {
                // Prüfe ob nicht zu nah an anderen Noten
                var tooClose = notes.Any(note => Math.Abs(note - t) < settings.MinTimeBetweenNotes * 0.5f);
                if (!tooClose)
                {
                    notes.Add(t);
                }
            }
        }
    }

    private (int phraseId, int phrasePosition) FindPhraseInfo(float noteTime, List<MusicalPhrase> phrases)
    {
        for (int i = 0; i < phrases.Count; i++)
        {
            var phrase = phrases[i];
            if (noteTime >= phrase.StartTime && noteTime <= phrase.EndTime)
            {
                var position = phrase.NoteTimes.FindIndex(t => Math.Abs(t - noteTime) < 0.05f);
                return (phrase.Id, Math.Max(0, position));
            }
        }
        return (0, 0); // Fallback
    }

    // OBSOLET - wird nicht mehr verwendet, da Phrasen-basierte Generierung verwendet wird
    private List<float> GenerateBeatBasedNotes(List<float> beatTimestamps, DifficultySettings settings)
    {
        var notes = new List<float>();

        for (int i = 0; i < beatTimestamps.Count; i++)
        {
            var beat = beatTimestamps[i];

            // Analysiere musikalische Eignung an dieser Stelle
            var musicalFitness = GetMusicalFitnessAt(beat, audioAnalysisResult!);

            // NUR Noten setzen wo es musikalisch passt!
            if (musicalFitness < 0.3f) continue; // Skip wenn nicht musikalisch passend

            // Hauptbeats: Sehr konservativ, nur bei klarer musikalischer Rechtfertigung
            var mainBeatThreshold = Math.Max(0.6f, settings.BeatSensitivity); // Mindestens 60%
            if (musicalFitness >= mainBeatThreshold)
            {
                notes.Add(beat);
            }

            // Subdivisions: NUR bei sehr starker musikalischer Evidenz
            if (i < beatTimestamps.Count - 1)
            {
                var nextBeat = beatTimestamps[i + 1];
                var interval = nextBeat - beat;

                // 8th Notes: Nur bei sehr hoher musikalischer Eignung
                if (settings.NotesPerMinute >= 150 &&
                    interval > settings.MinTimeBetweenNotes * 3 && // Mehr Abstand erforderlich
                    musicalFitness > 0.8f) // Sehr hohe musikalische Eignung
                {
                    var eighthNote = beat + interval / 2;
                    var eighthFitness = GetMusicalFitnessAt(eighthNote, audioAnalysisResult!);

                    // Nur wenn 8th Note AUCH musikalisch passt
                    if (eighthFitness > 0.7f)
                    {
                        notes.Add(eighthNote);
                    }
                }

                // 16th Notes: Nur bei extremer musikalischer Rechtfertigung
                if (settings.NotesPerMinute >= 300 &&
                    interval > settings.MinTimeBetweenNotes * 6 && // Viel mehr Abstand
                    musicalFitness > 1.5f) // Extreme musikalische Eignung
                {
                    var sixteenthNote1 = beat + interval / 4;
                    var sixteenthNote2 = beat + 3 * interval / 4;

                    // Prüfe jeden 16th note individuell auf musikalische Eignung
                    var fitness1 = GetMusicalFitnessAt(sixteenthNote1, audioAnalysisResult!);
                    var fitness2 = GetMusicalFitnessAt(sixteenthNote2, audioAnalysisResult!);

                    if (fitness1 > 1.2f) // Sehr strenge Kriterien
                    {
                        notes.Add(sixteenthNote1);
                    }
                    if (fitness2 > 1.2f) // Sehr strenge Kriterien
                    {
                        notes.Add(sixteenthNote2);
                    }
                }
            }
        }

        return notes;
    }

    private float GetSongIntensityAt(float timing, AudioAnalysisResult audioAnalysis)
    {
        // Konvertiere Timing zu Spectral Frame Index
        var timeIndex = (int)(timing * 44100 / 512);
        timeIndex = Math.Clamp(timeIndex, 0, audioAnalysis.SpectralEnergy.Length - 1);

        if (audioAnalysis.SpectralEnergy.Length == 0) return 1.0f;

        // Hole Energie-Wert an dieser Position
        var currentEnergy = audioAnalysis.SpectralEnergy[timeIndex];

        // Berechne lokalen Durchschnitt (±2 Sekunden Fenster)
        var windowSize = Math.Min(44100 / 512 * 4, audioAnalysis.SpectralEnergy.Length / 4); // 4 Sekunden oder 1/4 des Songs
        var startIndex = Math.Max(0, timeIndex - windowSize / 2);
        var endIndex = Math.Min(audioAnalysis.SpectralEnergy.Length - 1, timeIndex + windowSize / 2);

        var localAverage = 0f;
        for (int i = startIndex; i <= endIndex; i++)
        {
            localAverage += audioAnalysis.SpectralEnergy[i];
        }
        localAverage /= (endIndex - startIndex + 1);

        // Globaler Durchschnitt für Normalisierung
        var globalAverage = audioAnalysis.SpectralEnergy.Average();

        // Intensitäts-Multiplikator: 0.5 (sehr ruhig) bis 2.0 (sehr intensiv)
        var localIntensity = localAverage > 0 ? currentEnergy / localAverage : 1.0f;
        var globalIntensity = globalAverage > 0 ? currentEnergy / globalAverage : 1.0f;

        // Kombiniere lokale und globale Intensität
        var finalIntensity = (localIntensity * 0.7f + globalIntensity * 0.3f);

        // Begrenze auf sinnvolle Bereiche
        return Math.Clamp(finalIntensity, 0.4f, 2.0f);
    }

    private float GetMusicalFitnessAt(float timing, AudioAnalysisResult audioAnalysis)
    {
        // Konvertiere Timing zu Spectral Frame Index
        var timeIndex = (int)(timing * 44100 / 512);
        timeIndex = Math.Clamp(timeIndex, 0, audioAnalysis.SpectralEnergy.Length - 1);

        if (audioAnalysis.SpectralEnergy.Length == 0) return 0.0f;

        var fitness = 0.0f;

        // 1. BEAT-NÄHE PRÜFUNG (Wichtigster Faktor)
        var closestBeatDistance = audioAnalysis.BeatTimestamps
            .Select(beat => Math.Abs(beat - timing))
            .Min();

        if (closestBeatDistance <= 0.05f) // Sehr nah am Beat
        {
            fitness += 1.0f;
        }
        else if (closestBeatDistance <= 0.1f) // Nah am Beat
        {
            fitness += 0.7f;
        }
        else if (closestBeatDistance <= 0.2f) // Mäßig nah
        {
            fitness += 0.3f;
        }
        else // Zu weit vom Beat entfernt
        {
            fitness += 0.0f; // Sehr schlechte Eignung
        }

        // 2. ONSET-ERKENNUNG (Neue Klänge beginnen)
        var currentEnergy = audioAnalysis.SpectralEnergy[timeIndex];

        // Prüfe Energy-Anstieg (Onset-Indikator)
        if (timeIndex > 2 && timeIndex < audioAnalysis.SpectralEnergy.Length - 2)
        {
            var previousEnergy = (audioAnalysis.SpectralEnergy[timeIndex - 1] + audioAnalysis.SpectralEnergy[timeIndex - 2]) / 2;
            var energyIncrease = currentEnergy - previousEnergy;
            var threshold = audioAnalysis.SpectralEnergy.Average() * 0.3f;

            if (energyIncrease > threshold) // Klarer Onset
            {
                fitness += 0.5f;
            }
        }

        // 3. FREQUENZ-AKTIVITÄT PRÜFUNG
        if (timeIndex < audioAnalysis.FrequencyBands.Length)
        {
            var bands = audioAnalysis.FrequencyBands[timeIndex];
            var totalActivity = bands[0] + bands[1] + bands[2]; // Bass + Mid + Treble

            if (totalActivity > 0.1f) // Mindest-Aktivität erforderlich
            {
                fitness += 0.3f;
            }

            // Bonus für ausgewogene Frequenz-Aktivität (nicht nur Bass oder nur Treble)
            var balancedActivity = Math.Min(Math.Min(bands[0], bands[1]), bands[2]) / Math.Max(Math.Max(bands[0], bands[1]), bands[2]);
            if (balancedActivity > 0.3f) // Ausgewogene Frequenzen
            {
                fitness += 0.2f;
            }
        }

        // 4. KONTEXT-PRÜFUNG (Pausen vermeiden)
        // Prüfe ob wir in einer sehr ruhigen Passage sind
        var localWindow = Math.Min(22, audioAnalysis.SpectralEnergy.Length / 20); // ~1 Sekunde oder 1/20 des Songs
        var startIdx = Math.Max(0, timeIndex - localWindow / 2);
        var endIdx = Math.Min(audioAnalysis.SpectralEnergy.Length - 1, timeIndex + localWindow / 2);

        var localMaxEnergy = 0f;
        for (int i = startIdx; i <= endIdx; i++)
        {
            localMaxEnergy = Math.Max(localMaxEnergy, audioAnalysis.SpectralEnergy[i]);
        }

        if (localMaxEnergy > 0 && currentEnergy < localMaxEnergy * 0.1f) // In sehr ruhiger Passage
        {
            fitness *= 0.2f; // Stark reduzierte Eignung in Pausen
        }

        return Math.Clamp(fitness, 0.0f, 2.0f);
    }

    private List<float> EnforceMinimumSpacing(List<float> timings, float minSpacing)
    {
        var result = new List<float>();

        foreach (var timing in timings)
        {
            if (!result.Any() || timing - result.Last() >= minSpacing)
            {
                result.Add(timing);
            }
        }

        return result;
    }

    private Vector3[] GeneratePositions(float[] noteTimings, AudioAnalysisResult audioAnalysis, DifficultySettings settings, List<MusicalPhrase> phrases)
    {
        var positions = new Vector3[noteTimings.Length];
        Vector3 lastPosition = GenerateRandomPosition();
        int currentPhraseId = -1;
        int phrasePatternType = 0;

        for (int i = 0; i < noteTimings.Length; i++)
        {
            var timing = noteTimings[i];

            // Verwende Melodie-bewusste Positionierung statt komplett random
            var basePosition = GenerateMelodyAwarePosition(timing, audioAnalysis);

            // Finde aktuelle Phrase für diese Note
            var (phraseId, phrasePosition) = FindPhraseInfo(timing, phrases);

            // Neue Phrase? → Neuer Pattern-Typ und "Atempause"
            if (phraseId != currentPhraseId)
            {
                currentPhraseId = phraseId;
                var phrase = phrases.FirstOrDefault(p => p.Id == phraseId);
                if (phrase != null)
                {
                    phrasePatternType = phrase.PatternType;

                    // Bei neuer Phrase: Springe zu neuem Bereich für "Atem-Effekt"
                    if (i > 0)
                    {
                        basePosition = GenerateNewPhrasePosition(lastPosition);
                    }
                }
            }

            // Pattern-Anwendung basierend auf Phrase
            var shouldApplyPattern = ShouldApplyMovementPattern(timing, audioAnalysis, phrasePosition);

            if (shouldApplyPattern && i > 0)
            {
                positions[i] = ApplyMovementPattern(basePosition, lastPosition, settings, phrasePosition, phrasePatternType, timing);
            }
            else
            {
                positions[i] = basePosition;
            }

            // Erzwinge Mindestabstand zwischen allen bereits platzierten Noten
            positions[i] = EnforceMinimumSpatialDistance(positions[i], positions, i, settings.MinNodeDistancePx);

            lastPosition = positions[i];
        }

        return positions;
    }

    private Vector3 GenerateNewPhrasePosition(Vector3 lastPosition)
    {
        // Größere Sprünge für bessere Screen-Verteilung zwischen Phrasen
        var direction = (float)(_random.NextDouble() * 2 * Math.PI);
        var distance = 500f + (float)_random.NextDouble() * 800f; // 500-1300px Sprung - viel größer!

        var newY = lastPosition.Y + distance * (float)Math.Cos(direction);
        var newZ = lastPosition.Z + distance * (float)Math.Sin(direction);

        // Manchmal komplett zum anderen Ende des Screens springen
        if (_random.NextDouble() < 0.3f) // 30% Chance
        {
            // Springe zu komplett anderem Bereich
            newY = _random.NextDouble() < 0.5 ? _random.Next(0, ScreenWidth / 3) : _random.Next(2 * ScreenWidth / 3, ScreenWidth);
            newZ = _random.NextDouble() < 0.5 ? _random.Next(0, ScreenHeight / 3) : _random.Next(2 * ScreenHeight / 3, ScreenHeight);
        }

        return ClampToBounds(new Vector3(0, newY, newZ));
    }


    private Vector3 GenerateRandomPosition()
    {
        return new Vector3(
            0, // X is always 0
            _random.Next(0, ScreenWidth), // Y is width (0-2200)
            _random.Next(0, ScreenHeight) // Z is height (0-1100)
        );
    }

    private Vector3 GenerateMelodyAwarePosition(float timing, AudioAnalysisResult audioAnalysis)
    {
        var timeIndex = (int)(timing * 44100 / 512);
        timeIndex = Math.Clamp(timeIndex, 0, audioAnalysis.FrequencyBands.Length - 1);

        if (timeIndex < audioAnalysis.FrequencyBands.Length)
        {
            var bands = audioAnalysis.FrequencyBands[timeIndex];
            var bassEnergy = bands[0];      // 0-200 Hz
            var midEnergy = bands[1];       // 200-2000 Hz
            var trebleEnergy = bands[2];    // 2000+ Hz

            // Berechne dominante Frequenz basierend auf Energy-Gewichtung
            var totalEnergy = bassEnergy + midEnergy + trebleEnergy;

            if (totalEnergy > 0.01f) // Mindest-Aktivität erforderlich
            {
                // Gewichtete Berechnung: Bass → unten, Treble → oben
                var bassWeight = bassEnergy / totalEnergy;
                var midWeight = midEnergy / totalEnergy;
                var trebleWeight = trebleEnergy / totalEnergy;

                // Melodie-Höhe: 0.0 (tief/Bass) bis 1.0 (hoch/Treble)
                var melodyHeight = (midWeight * 0.5f + trebleWeight * 1.0f);

                // Mappe auf Screen-Höhe: Bass → unten (niedrige Z), Treble → oben (hohe Z)
                var zPosition = melodyHeight * ScreenHeight;

                // Y bleibt random für Horizontale Variation
                var yPosition = _random.Next(0, ScreenWidth);

                return new Vector3(0, yPosition, zPosition);
            }
        }

        // Fallback auf random wenn keine Frequenz-Info
        return GenerateRandomPosition();
    }

    private bool ShouldApplyMovementPattern(float timing, AudioAnalysisResult audioAnalysis, int noteIndex)
    {
        // Da alle Noten jetzt beat-basiert sind, prüfen wir ob es ein Hauptbeat ist
        var isMainBeat = audioAnalysis.BeatTimestamps.Any(beat => Math.Abs(beat - timing) < 0.05f);

        if (isMainBeat)
        {
            // Bei Hauptbeats häufiger Movement Patterns (60% Chance)
            return _random.NextDouble() < 0.6;
        }
        else
        {
            // Bei Subdivisions (8th/16th notes) seltener (25% Chance)
            return _random.NextDouble() < 0.25;
        }
    }

    private Vector3 ApplyMovementPattern(Vector3 basePosition, Vector3 lastPosition, DifficultySettings settings, int noteIndex, int phrasePatternType = -1, float timing = 0f)
    {
        // Verwende Phrasen-Pattern-Typ für Konsistenz, oder zufällig wenn nicht verfügbar
        var patternType = phrasePatternType >= 0 ? phrasePatternType : _random.Next(0, 8);
        var targetPosition = basePosition;

        // Basis-Bewegungsdistanz mit besserer Verteilung
        var baseDist = settings.NotesPerMinute switch
        {
            <= 100 => 350f,    // Easy - größere Sprünge
            <= 150 => 450f,    // Normal - mehr Verteilung
            <= 250 => 550f,    // Hard - breitere Patterns
            <= 350 => 650f,    // Expert - große Bewegungen
            _ => 800f           // Master - maximale Verteilung
        };

        // Dynamik-Multiplikator basierend auf Song-Intensität
        var currentIntensity = GetSongIntensityAt(timing, audioAnalysisResult!);
        var dynamicsMultiplier = Math.Clamp(currentIntensity, 0.3f, 2.0f);

        var movementDistance = baseDist * dynamicsMultiplier;

        // Instrumenten-Bewusstsein: Analysiere dominantes Instrument
        var instrumentType = GetDominantInstrument(timing, audioAnalysisResult!);
        var instrumentModifier = GetInstrumentModifier(instrumentType);

        // Anpasse Movement Distance basierend auf Instrument
        movementDistance *= instrumentModifier.DistanceMultiplier;

        var angle = (float)(_random.NextDouble() * 2 * Math.PI);

        // Überschreibe Pattern-Type wenn Instrument sehr dominant ist
        if (instrumentModifier.ForcePatternType >= 0)
        {
            patternType = instrumentModifier.ForcePatternType;
        }

        switch (patternType)
        {
            case 0: // Smooth Flow - sanfte Verbindung zur letzten Position
                var flowDistance = movementDistance * 0.8f; // Größer für bessere Verteilung
                var flowAngle = Math.Atan2(lastPosition.Z - basePosition.Z, lastPosition.Y - basePosition.Y) +
                               (_random.NextDouble() - 0.5) * Math.PI * 0.7; // Mehr Variation
                targetPosition.Y = lastPosition.Y + flowDistance * (float)Math.Cos(flowAngle);
                targetPosition.Z = lastPosition.Z + flowDistance * (float)Math.Sin(flowAngle);
                break;

            case 1: // Stream Pattern - schnelle aufeinanderfolgende Noten in eine Richtung
                var streamDistance = movementDistance * 0.6f; // Größer für mehr Spread
                var streamAngle = angle;
                // Behalte Richtung für mehrere Noten bei
                if (noteIndex % 4 != 0) streamAngle = (noteIndex % 8) * (float)Math.PI / 4; // 8 Richtungen
                targetPosition.Y = lastPosition.Y + streamDistance * (float)Math.Cos(streamAngle);
                targetPosition.Z = lastPosition.Z + streamDistance * (float)Math.Sin(streamAngle);
                break;

            case 2: // Triangle Pattern - Dreiecksbewegung
                var triangleRadius = movementDistance * 1.0f; // Vollständige Distanz für größere Dreiecke
                var triangleAngle = (noteIndex % 3) * 2 * Math.PI / 3; // 120° Winkel
                targetPosition.Y = lastPosition.Y + triangleRadius * (float)Math.Cos(triangleAngle + angle);
                targetPosition.Z = lastPosition.Z + triangleRadius * (float)Math.Sin(triangleAngle + angle);
                break;

            case 3: // Square Pattern - Quadratische Bewegung
                var squareSize = movementDistance * 0.9f; // Größere Quadrate
                var squareSide = noteIndex % 4;
                switch (squareSide)
                {
                    case 0: targetPosition.Y = lastPosition.Y + squareSize; break;              // Rechts
                    case 1: targetPosition.Z = lastPosition.Z + squareSize; break;              // Oben
                    case 2: targetPosition.Y = lastPosition.Y - squareSize; break;              // Links
                    case 3: targetPosition.Z = lastPosition.Z - squareSize; break;              // Unten
                }
                break;

            case 4: // Spiral Pattern - erweitert
                var spiralRadius = movementDistance * (0.7f + 0.15f * (noteIndex % 10)); // Größere Spirale
                var spiralAngle = angle + (noteIndex * 0.5f); // Mehr Rotation
                targetPosition.Y = lastPosition.Y + spiralRadius * (float)Math.Cos(spiralAngle);
                targetPosition.Z = lastPosition.Z + spiralRadius * (float)Math.Sin(spiralAngle);
                break;

            case 5: // Zigzag Pattern - erweitert
                var zigzagAmplitude = movementDistance * 1.0f; // Vollständige Amplitude
                var zigzagDirection = (noteIndex % 2 == 0) ? 1 : -1;
                var zigzagAngle = angle + zigzagDirection * Math.PI / 2.5; // Schärfere Winkel
                targetPosition.Y = lastPosition.Y + zigzagAmplitude * (float)Math.Cos(zigzagAngle);
                targetPosition.Z = lastPosition.Z + zigzagAmplitude * (float)Math.Sin(zigzagAngle);
                break;

            case 6: // Star Pattern - 5-zackiger Stern
                var starRadius = movementDistance * 1.1f; // Größere Sterne
                var starAngle = (noteIndex % 5) * 2 * Math.PI / 5; // 72° Winkel
                targetPosition.Y = lastPosition.Y + starRadius * (float)Math.Cos(starAngle + angle);
                targetPosition.Z = lastPosition.Z + starRadius * (float)Math.Sin(starAngle + angle);
                break;

            case 7: // Jump Pattern - größere, zufällige Sprünge
                var jumpDistance = movementDistance * 1.5f; // Noch größere Sprünge
                targetPosition.Y = basePosition.Y + jumpDistance * (float)Math.Cos(angle);
                targetPosition.Z = basePosition.Z + jumpDistance * (float)Math.Sin(angle);
                break;
        }

        // Mische zwischen original random position und pattern
        var blendFactor = 0.25f; // Noch weniger random, mehr pattern-driven movement
        targetPosition.Y = basePosition.Y * blendFactor + targetPosition.Y * (1 - blendFactor);
        targetPosition.Z = basePosition.Z * blendFactor + targetPosition.Z * (1 - blendFactor);

        return ClampToBounds(targetPosition);
    }

    private Vector3 EnforceMinimumSpatialDistance(Vector3 candidate, Vector3[] positions, int count, float minDistance)
    {
        var minDistSq = minDistance * minDistance;
        const int maxIterations = 25;

        for (int iter = 0; iter < maxIterations; iter++)
        {
            var adjusted = false;

            for (int j = 0; j < count; j++)
            {
                var other = positions[j];
                var dy = candidate.Y - other.Y;
                var dz = candidate.Z - other.Z;
                var distSq = dy * dy + dz * dz;

                if (distSq < minDistSq)
                {
                    var dist = (float)Math.Sqrt(Math.Max(distSq, 1e-6f));
                    float dirY;
                    float dirZ;

                    if (dist < 1e-3f)
                    {
                        // Zufällige Richtung wählen, wenn exakt gleicher Punkt
                        var angle = (float)(_random.NextDouble() * 2 * Math.PI);
                        dirY = (float)Math.Cos(angle);
                        dirZ = (float)Math.Sin(angle);
                    }
                    else
                    {
                        dirY = dy / dist;
                        dirZ = dz / dist;
                    }

                    candidate.Y = other.Y + dirY * minDistance;
                    candidate.Z = other.Z + dirZ * minDistance;

                    candidate = ClampToBounds(candidate);
                    adjusted = true;
                }
            }

            if (!adjusted)
            {
                break;
            }
        }

        return candidate;
    }

    private InstrumentType GetDominantInstrument(float timing, AudioAnalysisResult audioAnalysis)
    {
        var timeIndex = (int)(timing * 44100 / 512);
        timeIndex = Math.Clamp(timeIndex, 0, audioAnalysis.FrequencyBands.Length - 1);

        if (timeIndex < audioAnalysis.FrequencyBands.Length)
        {
            var bands = audioAnalysis.FrequencyBands[timeIndex];
            var bassEnergy = bands[0];      // 0-200 Hz -> Bass/Drums
            var midEnergy = bands[1];       // 200-2000 Hz -> Melodie/Vocals
            var trebleEnergy = bands[2];    // 2000+ Hz -> Hi-Hat/Cymbals/Lead

            var totalEnergy = bassEnergy + midEnergy + trebleEnergy;
            if (totalEnergy < 0.01f) return InstrumentType.None;

            // Bestimme dominantes Frequenzband
            var maxEnergy = Math.Max(Math.Max(bassEnergy, midEnergy), trebleEnergy);

            // Analyse für Drums: Hohe Bass-Energie + Percussion-Indikator
            var isPercussive = IsPercussiveHit(timing, audioAnalysis);

            if (bassEnergy == maxEnergy && bassEnergy > totalEnergy * 0.5f)
            {
                return isPercussive ? InstrumentType.Drums : InstrumentType.Bass;
            }
            else if (trebleEnergy == maxEnergy && trebleEnergy > totalEnergy * 0.6f && isPercussive)
            {
                return InstrumentType.Drums; // Hi-Hat, Cymbals
            }
            else if (midEnergy == maxEnergy)
            {
                return InstrumentType.Melody; // Vocals, Lead Instruments
            }
            else if (bassEnergy > totalEnergy * 0.3f)
            {
                return InstrumentType.Bass;
            }
        }

        return InstrumentType.None;
    }

    private bool IsPercussiveHit(float timing, AudioAnalysisResult audioAnalysis)
    {
        // Percussion hat schnelle Energy-Anstiege und -Abfälle
        var timeIndex = (int)(timing * 44100 / 512);
        if (timeIndex < 2 || timeIndex >= audioAnalysis.SpectralEnergy.Length - 2) return false;

        var currentEnergy = audioAnalysis.SpectralEnergy[timeIndex];
        var prevEnergy = audioAnalysis.SpectralEnergy[timeIndex - 1];
        var nextEnergy = audioAnalysis.SpectralEnergy[timeIndex + 1];

        // Schneller Anstieg und Abfall = Percussion-Charakteristik
        var hasQuickAttack = currentEnergy > prevEnergy * 1.8f;
        var hasQuickDecay = nextEnergy < currentEnergy * 0.6f;

        return hasQuickAttack && hasQuickDecay;
    }

    private InstrumentModifier GetInstrumentModifier(InstrumentType instrument)
    {
        return instrument switch
        {
            InstrumentType.Bass => new InstrumentModifier
            {
                DistanceMultiplier = 1.3f, // Größere, "schwerere" Bewegungen
                ForcePatternType = 3, // Square Pattern - blocky wie Bass
                Description = "Bass: Größere, blockige Bewegungen"
            },

            InstrumentType.Melody => new InstrumentModifier
            {
                DistanceMultiplier = 1.0f, // Normale, fließende Bewegungen
                ForcePatternType = 0, // Smooth Flow für Melodie-Linien
                Description = "Melody: Fließende, melodische Bewegungen"
            },

            InstrumentType.Drums => new InstrumentModifier
            {
                DistanceMultiplier = 0.7f, // Kürzere, schärfere Bewegungen
                ForcePatternType = 5, // Zigzag Pattern - scharf und perkussiv
                Description = "Drums: Scharfe, präzise Bewegungen"
            },

            _ => new InstrumentModifier
            {
                DistanceMultiplier = 1.0f,
                ForcePatternType = -1, // Kein forced pattern
                Description = "Default: Standard Bewegungen"
            }
        };
    }

    private Vector3 ClampToBounds(Vector3 position)
    {
        return new Vector3(
            0, // X is always 0
            Math.Clamp(position.Y, 0, ScreenWidth), // Y is width (0-2200)
            Math.Clamp(position.Z, 0, ScreenHeight) // Z is height (0-1100)
        );
    }
}

// Extension method for standard deviation calculation
public static class Extensions
{
    public static float StandardDeviation(this float[] values)
    {
        if (values.Length == 0) return 0;

        var average = values.Average();
        var sumOfSquares = values.Sum(val => (val - average) * (val - average));
        return (float)Math.Sqrt(sumOfSquares / values.Length);
    }
}