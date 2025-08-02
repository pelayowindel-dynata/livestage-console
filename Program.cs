// Program.cs
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using NAudio.Wave;
using NAudio.Midi;
using MeltySynth;

namespace projectSynth
{
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

    class Program
    {
        private static Synthesizer? synth;

        static void Main(string[] args)
        {
            string soundFontPath = @"C:\Users\Windel.Pelayo\source\repos\livestage-console\FluidR3_GM.sf2";
            if (!File.Exists(soundFontPath))
            {
                Console.WriteLine("SoundFont file not found!");
                return;
            }

            // Load SF2 and list presets
            var sf = new SoundFont(soundFontPath);
            Console.WriteLine("Instruments in SoundFont:");
            foreach (var preset in sf.Presets)
                Console.WriteLine($"Bank {preset.BankNumber}, Program {preset.PatchNumber}, Name: {preset.Name}");

            synth = new Synthesizer(sf, 44100);

            // Prompt user for bank and program selection (validate against presets)
            Console.Write("Enter Bank number (0-127): ");
            if (!int.TryParse(Console.ReadLine(), out int bankNumber) || bankNumber < 0 || bankNumber > 127)
            {
                Console.WriteLine("Invalid bank number, defaulting to 0.");
                bankNumber = 0;
            }
            Console.Write("Enter Program number (0-127): ");
            if (!int.TryParse(Console.ReadLine(), out int programNumber) || programNumber < 0 || programNumber > 127)
            {
                Console.WriteLine("Invalid program number, defaulting to 0.");
                programNumber = 0;
            }

            // Find matching preset
            var match = sf.Presets.FirstOrDefault(p => p.BankNumber == bankNumber && p.PatchNumber == programNumber);
            if (match == null)
            {
                Console.WriteLine($"No preset at Bank {bankNumber}, Program {programNumber}. Reverting to default.");
                bankNumber = sf.Presets.First().BankNumber;
                programNumber = sf.Presets.First().PatchNumber;
                Console.WriteLine($"Defaulting to Bank {bankNumber}, Program {programNumber}.");
            }

            // Send MIDI messages to set bank and program on channel 0
            for (int ch = 0; ch < 16; ch++)
            {
                synth.ProcessMidiMessage(ch, 0xB0, 0, bankNumber);   // Bank Select MSB
                synth.ProcessMidiMessage(ch, 0xB0, 32, 0);           // Bank Select LSB
                synth.ProcessMidiMessage(ch, 0xC0, programNumber, 0); // Program Change
            }

            // Setup low-latency audio output (pull model)
            var provider = new SynthWaveProvider(synth, 44100);
            var waveOut = new WaveOutEvent
            {
                DesiredLatency = 50,
                NumberOfBuffers = 2
            };
            waveOut.Init(provider);
            waveOut.Play();

            // Play a test note to hear the selected preset
            Console.WriteLine("Playing test note (Middle C)...");
            synth.NoteOn(0, 60, 100); // Channel 0, Middle C
            Thread.Sleep(1000);       // Hold for 1 second
            synth.NoteOff(0, 60);

            // Setup MIDI inputs for live playing
            var midiIns = new List<MidiIn>();
            Console.WriteLine("MIDI Input Devices:");
            for (int i = 0; i < MidiIn.NumberOfDevices; i++)
            {
                var info = MidiIn.DeviceInfo(i);
                Console.WriteLine($"[{i}] {info.ProductName}");
                var midiIn = new MidiIn(i);
                midiIn.MessageReceived += OnMidiIn;
                midiIn.Start();
                midiIns.Add(midiIn);
            }

            Console.WriteLine("Press ENTER to exit...");
            Console.ReadLine();

            // Cleanup
            foreach (var mi in midiIns)
            {
                mi.Stop();
                mi.Dispose();
            }
            waveOut.Stop();
            waveOut.Dispose();
        }

        private static void OnMidiIn(object? sender, MidiInMessageEventArgs e)
        {
            var me = e.MidiEvent;
            switch (me)
            {
                case NoteOnEvent noteOn when noteOn.Velocity > 0:
                    synth?.NoteOn(noteOn.Channel, noteOn.NoteNumber, noteOn.Velocity);
                    break;

                case NoteEvent noteOff when noteOff.CommandCode == MidiCommandCode.NoteOff
                     || (noteOff is NoteOnEvent off && off.Velocity == 0):
                    synth?.NoteOff(noteOff.Channel, noteOff.NoteNumber);
                    break;

                case PatchChangeEvent pc:
                    synth.ProcessMidiMessage(pc.Channel, 0xC0, pc.Patch, 0);
                    Console.WriteLine($"Program changed to {pc.Patch} on channel {pc.Channel}");
                    break;

                case ControlChangeEvent cc:
                    synth.ProcessMidiMessage(cc.Channel, 0xB0, (int)cc.Controller, cc.ControllerValue);
                    Console.WriteLine($"Control change: Controller {(int)cc.Controller}, Value {cc.ControllerValue}");
                    break;
            }
        }
    }
}
