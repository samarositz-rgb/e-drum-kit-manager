namespace TD12KitManager;

/// <summary>Checks Roland system exclusive data (TD-12 bulk dumps).</summary>
public static class Sysex
{
    /// <summary>Counts F0..F7 messages; for Roland DT1 messages, verifies the checksum.</summary>
    public static (int Count, int Bad) Check(byte[] data)
    {
        int count = 0, bad = 0;
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] != 0xF0) continue;
            int end = Array.IndexOf(data, (byte)0xF7, i);
            count++;
            if (end < 0) { bad++; break; }
            // F0 41 dev 00 00 09 12 [address + data] sum F7: address + data + sum must be 0 modulo 128.
            if (end - i > 8 && data[i + 1] == 0x41 && data[i + 6] == 0x12)
            {
                int sum = 0;
                for (int k = i + 7; k < end; k++) sum += data[k];
                if (sum % 128 != 0) bad++;
            }
            i = end;
        }
        return (count, bad);
    }
}
