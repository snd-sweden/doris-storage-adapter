namespace DorisStorageAdapter.BagIt;

internal static class BagItParsing
{
    public static int FindLinearWhiteSpaceIndex(string line, int start = 0)
    {
        for (int i = start; i < line.Length; i++)
        {
            char c = line[i];
            if (c == ' ' || c == '\t')
            {
                return i;
            }
        }

        return -1;
    }

    public static int SkipLinearWhiteSpace(string line, int start)
    {
        int i = start;

        while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
        {
            i++;
        }

        return i;
    }
}