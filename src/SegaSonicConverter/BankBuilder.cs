namespace SegaSonicConverter;

internal static class BankBuilder
{
    public static void Build(Assets assets, string libraryPath, string outputPath, double drumReductionDb)
    {
        SoundFont sf = new();
        Dictionary<int, int> samples = new();
        foreach (var (index, path) in assets.Samples)
        {
            var (pcm, rate) = assets.LoadWave(path);
            samples[index] = sf.AddSample($"PCM_{index:X2}", pcm, rate, root: 85);
        }
        // PCM programs and their original SoundFont transpose variants are preserved
        int pcmFine = (int)Math.Round(1200 * Math.Log2(0x3E6 / (double)0x400));
        foreach (var (program, p) in assets.Programs)
        {
            if (p.Kind != "pcm") continue;
            int inst = sf.AddInstrument($"PCM program {program:00}",
                new() { SoundFont.Zone(samples[p.Id], root:85, fine:pcmFine + (int)Math.Round(p.Detune * 6.25), release:1.7) });
            sf.AddPreset($"PCM {program:00} normal", program, 0, inst);
            sf.AddPreset($"PCM {program:00} -12", program + 40, 0, inst, -12);
            sf.AddPreset($"PCM {program:00} -24", program + 80, 0, inst, -24);
        }
        foreach (var (kit, notes) in assets.Kits)
        {
            var zones = new List<List<Generator>>();
            foreach (var (note, drum) in notes)
            {
                if (!samples.TryGetValue(drum.Sample, out int sampleId)) continue;
                double cents = 1200 * Math.Log2(drum.Step / 1024.0);
                int whole = (int)Math.Round(cents / 100);
                int fine = (int)Math.Round(cents - whole * 100);
                int root = Math.Clamp(note - whole, 0, 127);
                int pan = (int)((drum.Right - drum.Left) / 15.0 * 450);
                zones.Add(SoundFont.Zone(sampleId, root:root, fine:fine, low:note, high:note, pan:pan, release:2.0));
            }
            int inst = sf.AddInstrument($"Drum kit {kit}", zones);
            int attenuation = (int)Math.Round(drumReductionDb * 25); // Match tested 5 dB attenuation settings
            sf.AddPreset($"Drums {kit}", 120 + kit, 0, inst, attenuation:attenuation);
            sf.AddPreset($"Drums GM {kit}", kit, 128, inst, attenuation:attenuation);
        }
        using var fm = new FmRenderer(libraryPath);
        Dictionary<int, List<List<Generator>>> patchZones = new();
        int looped = 0, oneShots = 0, unmodified = 0;
        foreach (var (program, def) in assets.Programs)
        {
            if (def.Kind != "fm") continue;
            if (!patchZones.TryGetValue(def.Id, out var normalZones))
            {
                var patch = assets.Patches[def.Id];
                normalZones = new List<List<Generator>>();
                foreach (int note in Enumerable.Range(0, 20).Select(i => 12 + i * 6))
                {
                    short[] audio = fm.RenderPatch(patch, note);
                    LoopResult? improvement = LoopOptimizer.Optimize(audio);
                    int from = improvement is { OneShot: false } ? improvement.Value.Start : (int)(.70 * 44100);
                    int to = improvement is { OneShot: false } ? improvement.Value.End : (int)(1.46 * 44100);
                    bool shouldLoop = improvement is not { OneShot: true };
                    if (improvement is null) unmodified++;
                    else if (improvement.Value.OneShot) oneShots++;
                    else looped++;
                    int sampleId = sf.AddSample($"FM{def.Id:X2}_{note:000}", audio, 44100, root:note, start:from, end:to);
                    normalZones.Add(SoundFont.Zone(sampleId, root:note, low:Math.Max(0,note-3), high:Math.Min(127,note+2),
                        loop:shouldLoop, release:.18));
                }
                patchZones[def.Id] = normalZones;
            }
            int original = sf.AddInstrument($"FM program {program:00} patch {def.Id:X2}", normalZones);
            sf.AddPreset($"FM {program:00} normal", program, 0, original);
            // Original SoundFont stored a -12 / -24 coarse tune at preset level
            // Shift the key ranges in the dedicated instrument as well, so those presets select a lower native YM3438 sample before coarse tuning
            foreach (int transpose in new[] { -12, -24 })
            {
                var shifted = ShiftFmZones(normalZones, -transpose);
                int newInstrument = sf.AddInstrument($"FM program {program:00} k{-transpose:+0}", shifted);
                sf.AddPreset($"FM {program:00} {transpose}", program + (transpose == -12 ? 40 : 80), 0, newInstrument, transpose);
            }
            Console.WriteLine($"FM program {program:00}: patch {def.Id:X2}");
        }
        Console.WriteLine($"Loop optimization: {looped} loops improved; {oneShots} naturally fading FM samples made one-shot; {unmodified} left unchanged");
        sf.Write(outputPath);
    }

    private static List<List<Generator>> ShiftFmZones(List<List<Generator>> original, int shift)
    {
        var result = new List<List<Generator>>();
        foreach (var zone in original)
        {
            int keyIndex = zone.FindIndex(g => g.Op == 43);
            if (keyIndex < 0) throw new InvalidDataException("Original FM zone has no key range");
            int low = zone[keyIndex].Lo + shift;
            int high = zone[keyIndex].Hi + shift;
            if (low > 127) continue;
            var copy = new List<Generator>(zone);
            copy[keyIndex] = Generator.Range(low, Math.Min(127, high));
            result.Add(copy);
        }
        if (result.Count == 0) throw new InvalidDataException("No FM samples reachable after transposition");
        // Identical edge expansion to the previous Python keymap repair
        int firstIndex = result[0].FindIndex(g => g.Op == 43);
        result[0][firstIndex] = Generator.Range(0, result[0][firstIndex].Hi);
        int last = result.Count - 1;
        int lastIndex = result[last].FindIndex(g => g.Op == 43);
        result[last][lastIndex] = Generator.Range(result[last][lastIndex].Lo, 127);
        int covered = -1;
        foreach (var zone in result)
        {
            Generator g = zone.Single(g => g.Op == 43);
            if (g.Lo != covered + 1) throw new InvalidDataException("Gap or overlap in transposed FM sample zones");
            covered = g.Hi;
        }
        if (covered != 127) throw new InvalidDataException("Transposed FM sample mapping doesn't cover MIDI keys 0-127");
        return result;
    }
}
