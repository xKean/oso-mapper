using NAudio.Wave;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using System.Numerics;

namespace OsuMapGenerator.Audio;

public class AudioAnalyzer
{
    private const int SampleRate = 44100;
    private const int FFTSize = 2048;
    private const int HopSize = 512;

    public AudioAnalysisResult AnalyzeAudio(string mp3FilePath)
    {
        var audioData = LoadMp3File(mp3FilePath);
        var beats = DetectBeats(audioData);
        var spectralFeatures = AnalyzeSpectralFeatures(audioData);

        return new AudioAnalysisResult
        {
            Duration = audioData.Length / (float)SampleRate,
            BeatTimestamps = beats,
            SpectralEnergy = spectralFeatures.SpectralEnergy,
            EstimatedBPM = EstimateBPM(beats),
            FrequencyBands = spectralFeatures.FrequencyBands
        };
    }

    private float[] LoadMp3File(string filePath)
    {
        using var reader = new Mp3FileReader(filePath);
        var sampleProvider = reader.ToSampleProvider();

        var samples = new List<float>();
        var buffer = new float[reader.WaveFormat.SampleRate];

        int samplesRead;
        while ((samplesRead = sampleProvider.Read(buffer, 0, buffer.Length)) > 0)
        {
            samples.AddRange(buffer.Take(samplesRead));
        }

        // Convert to mono if stereo
        var audioSamples = reader.WaveFormat.Channels == 2 ?
            ConvertToMono(samples.ToArray()) : samples.ToArray();

        // Simple resampling if needed (basic decimation/interpolation)
        if (reader.WaveFormat.SampleRate != SampleRate)
        {
            audioSamples = SimpleResample(audioSamples, reader.WaveFormat.SampleRate, SampleRate);
        }

        return audioSamples;
    }

    private float[] ConvertToMono(float[] stereoSamples)
    {
        var monoSamples = new float[stereoSamples.Length / 2];
        for (int i = 0; i < monoSamples.Length; i++)
        {
            monoSamples[i] = (stereoSamples[i * 2] + stereoSamples[i * 2 + 1]) / 2.0f;
        }
        return monoSamples;
    }

    private List<float> DetectBeats(float[] audioData)
    {
        var beats = new List<float>();
        var windowSize = SampleRate / 10; // 100ms windows
        var threshold = CalculateDynamicThreshold(audioData, windowSize);

        for (int i = 0; i < audioData.Length - windowSize; i += windowSize / 4)
        {
            var window = audioData.Skip(i).Take(windowSize).ToArray();
            var energy = CalculateSpectralFlux(window);

            if (i / (windowSize / 4) < threshold.Length && energy > threshold[i / (windowSize / 4)])
            {
                var beatTime = i / (float)SampleRate;

                // Avoid beats too close together
                if (!beats.Any() || beatTime - beats.Last() > 0.1f)
                {
                    beats.Add(beatTime);
                }
            }
        }

        return beats;
    }

    private float[] CalculateDynamicThreshold(float[] audioData, int windowSize)
    {
        var thresholds = new List<float>();
        var energyHistory = new Queue<float>();
        var historySize = 10;

        for (int i = 0; i < audioData.Length - windowSize; i += windowSize / 4)
        {
            var window = audioData.Skip(i).Take(windowSize).ToArray();
            var energy = CalculateSpectralFlux(window);

            energyHistory.Enqueue(energy);
            if (energyHistory.Count > historySize)
            {
                energyHistory.Dequeue();
            }

            var meanEnergy = energyHistory.Average();
            var variance = energyHistory.Sum(e => Math.Pow(e - meanEnergy, 2)) / energyHistory.Count;
            var threshold = meanEnergy + Math.Sqrt(variance) * 1.5f;

            thresholds.Add((float)threshold);
        }

        return thresholds.ToArray();
    }

    private float CalculateSpectralFlux(float[] window)
    {
        var fftData = new Complex[FFTSize];

        for (int i = 0; i < Math.Min(window.Length, FFTSize); i++)
        {
            fftData[i] = new Complex(window[i] * ApplyHammingWindow(i, window.Length), 0);
        }

        Fourier.Forward(fftData, FourierOptions.Matlab);

        var magnitude = fftData.Take(FFTSize / 2)
                              .Select(c => (float)c.Magnitude)
                              .ToArray();

        return magnitude.Sum();
    }

    private (float[] SpectralEnergy, float[][] FrequencyBands) AnalyzeSpectralFeatures(float[] audioData)
    {
        var spectralEnergy = new List<float>();
        var frequencyBands = new List<float[]>();

        for (int i = 0; i < audioData.Length - FFTSize; i += HopSize)
        {
            var window = audioData.Skip(i).Take(FFTSize).ToArray();
            var fftData = new Complex[FFTSize];

            for (int j = 0; j < FFTSize; j++)
            {
                fftData[j] = new Complex(window[j] * ApplyHammingWindow(j, FFTSize), 0);
            }

            Fourier.Forward(fftData, FourierOptions.Matlab);

            var magnitude = fftData.Take(FFTSize / 2)
                                  .Select(c => (float)c.Magnitude)
                                  .ToArray();

            spectralEnergy.Add(magnitude.Sum());

            // Divide into frequency bands (bass, mid, treble)
            var bassEnd = magnitude.Length / 8;
            var midEnd = magnitude.Length / 2;

            var bands = new[]
            {
                magnitude.Take(bassEnd).Sum(),          // Bass
                magnitude.Skip(bassEnd).Take(midEnd - bassEnd).Sum(),  // Mid
                magnitude.Skip(midEnd).Sum()            // Treble
            };

            frequencyBands.Add(bands);
        }

        return (spectralEnergy.ToArray(), frequencyBands.ToArray());
    }

    private float EstimateBPM(List<float> beats)
    {
        if (beats.Count < 2) return 120.0f; // Default BPM

        var intervals = new List<float>();
        for (int i = 1; i < beats.Count; i++)
        {
            intervals.Add(beats[i] - beats[i - 1]);
        }

        var avgInterval = intervals.Average();
        return 60.0f / avgInterval;
    }

    private float ApplyHammingWindow(int n, int N)
    {
        return (float)(0.54 - 0.46 * Math.Cos(2 * Math.PI * n / (N - 1)));
    }

    private float[] SimpleResample(float[] input, int fromSampleRate, int toSampleRate)
    {
        if (fromSampleRate == toSampleRate) return input;

        var ratio = (double)toSampleRate / fromSampleRate;
        var outputLength = (int)(input.Length * ratio);
        var output = new float[outputLength];

        for (int i = 0; i < outputLength; i++)
        {
            var sourceIndex = i / ratio;
            var index = (int)sourceIndex;

            if (index < input.Length - 1)
            {
                // Simple linear interpolation
                var fraction = sourceIndex - index;
                output[i] = (float)(input[index] * (1 - fraction) + input[index + 1] * fraction);
            }
            else if (index < input.Length)
            {
                output[i] = input[index];
            }
        }

        return output;
    }
}

public class AudioAnalysisResult
{
    public float Duration { get; set; }
    public List<float> BeatTimestamps { get; set; } = new();
    public float[] SpectralEnergy { get; set; } = Array.Empty<float>();
    public float EstimatedBPM { get; set; }
    public float[][] FrequencyBands { get; set; } = Array.Empty<float[]>();
}