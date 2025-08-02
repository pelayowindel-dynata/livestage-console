using NAudio.Wave;
using MeltySynth;

namespace livestage_console;

// Continuous audio provider for MeltySynth
class SynthWaveProvider : IWaveProvider
{
    private readonly Synthesizer synth;
    private readonly WaveFormat waveFormat;

    public SynthWaveProvider(Synthesizer synth, int sampleRate = 44100)
    {
        this.synth = synth;
        waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
    }

    public WaveFormat WaveFormat => waveFormat;

    public int Read(byte[] buffer, int offset, int count)
    {
        int samples = count / sizeof(float);
        var floatBuffer = new float[samples];
        synth.RenderInterleaved(floatBuffer);
        Buffer.BlockCopy(floatBuffer, 0, buffer, offset, count);
        return count;
    }
}
