namespace SegaSonicConverter;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            string? sound = null, native = null;
            string output = "Output";
            double drums = 5;
            if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
            {
                Console.WriteLine("SegaSonicConverter - C# MIDI and SoundFont generator");
                Console.WriteLine("Usage: SegaSonicConverter --sound sound.zip --fm-library segasonic_fm.dll [--output Output] [--drums-db 5]");
                Console.WriteLine("Uses user-extracted assets from SegaSonic Decomp and its unmodified YM3438 C emulator.");
                return args.Length == 0 ? 1 : 0;
            }
            for (int i = 0; i < args.Length; i++)
            {
                if (++i >= args.Length) throw new ArgumentException($"Missing argument after {args[i-1]}");
                string value = args[i];
                switch (args[i - 1])
                {
                    case "--sound": sound = value; break;
                    case "--fm-library": native = value; break;
                    case "--output": output = value; break;
                    case "--drums-db": drums = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
                    default: throw new ArgumentException($"Unknown option: {args[i - 1]}");
                }
            }
            if (sound is null || native is null) throw new ArgumentException("Both --sound and --fm-library are required");
            if (!File.Exists(sound)) throw new FileNotFoundException("Cannot find extracted sound.zip", sound);
            if (!File.Exists(native)) throw new FileNotFoundException("Cannot find compiled SegaSonic YM3438 bridge", native);
            if (double.IsNaN(drums) || drums < 0 || drums > 60) throw new ArgumentOutOfRangeException(nameof(drums), "Drum reduction must be 0..60 dB");
            Directory.CreateDirectory(output);
            using Assets assets = new(sound);
            MidiConverter.Convert(assets, output);
            string sf2 = Path.Combine(output, "SegaSonic.sf2");
            BankBuilder.Build(assets, native, sf2, drums);
            Console.WriteLine($"Success. Output: {Path.GetFullPath(sf2)}");
            Console.WriteLine($"MIDI files: {Path.GetFullPath(Path.Combine(output, "MIDI"))}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex.Message);
            return 1;
        }
    }
}
