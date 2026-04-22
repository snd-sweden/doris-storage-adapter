using System.Text;

namespace DorisStorageAdapter.BagIt;

internal static class BagItPathCodec
{
    public static string EncodeFilePath(string filePath)
    {
        var sb = new StringBuilder(filePath.Length);

        foreach (char c in filePath)
        {
            switch (c)
            {
                case '%':
                    sb.Append("%25");
                    break;
                case '\n':
                    sb.Append("%0A");
                    break;
                case '\r':
                    sb.Append("%0D");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    public static string DecodeFilePath(string filePath)
    {
        var sb = new StringBuilder(filePath.Length);

        for (int i = 0; i < filePath.Length; i++)
        {
            char c = filePath[i];

            if (c != '%')
            {
                sb.Append(c);
                continue;
            }

            if (i + 2 >= filePath.Length)
            {
                throw new BagItParseException("Invalid escape sequence in file path.");
            }

            char c1 = filePath[i + 1];
            char c2 = filePath[i + 2];

            if (c1 == '2' && c2 == '5')
            {
                sb.Append('%');
            }
            else if (c1 == '0' && (c2 == 'A' || c2 == 'a'))
            {
                sb.Append('\n');
            }
            else if (c1 == '0' && (c2 == 'D' || c2 == 'd'))
            {
                sb.Append('\r');
            }
            else
            {
                throw new BagItParseException(
                    $"Invalid escape sequence '%{c1}{c2}' in file path.");
            }

            i += 2;
        }

        return sb.ToString();
    }
}
