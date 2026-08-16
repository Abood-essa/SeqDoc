using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SeqDoc.Core.ProgramIndex;

namespace SeqDoc.Analysis.Roslyn.ProgramIndex;

internal static class Fingerprinting
{
    public static string Text(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static string Sequence(string kind, IEnumerable<string> values)
    {
        var builder = new StringBuilder(kind);
        foreach (var value in values)
        {
            builder.Append('|').Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
        }

        return Text(builder.ToString());
    }

    public static string Index(ProgramIndexSnapshot snapshot) => ProgramIndexFingerprint.Compute(snapshot);
}
