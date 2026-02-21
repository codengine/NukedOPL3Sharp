namespace NukedOPL3Sharp.MidiPlayer.Core.Patches;

public static class PatchBankLoader
{
    public static Dictionary<ushort, OplPatch> LoadFromFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".wopl" => WoplBankLoader.LoadFromFile(path),
            ".op2" => Op2BankLoader.LoadFromFile(path),
            _ => LoadBySignature(path)
        };
    }

    private static Dictionary<ushort, OplPatch> LoadBySignature(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 11 && bytes.AsSpan(0, 10).SequenceEqual("WOPL3-BANK"u8))
        {
            return WoplBankLoader.LoadFromBytes(bytes);
        }

        if (bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual("#OPL_II#"u8))
        {
            return Op2BankLoader.LoadFromBytes(bytes);
        }

        throw new InvalidDataException("Unknown patch bank format (expected .wopl or .op2).");
    }
}