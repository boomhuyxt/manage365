namespace manage365.Configuration;

public static class LocalEnvFile
{
    public static void LoadIntoProcessEnvironment(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        foreach (var (key, value) in Parse(File.ReadLines(path)))
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    public static IReadOnlyDictionary<string, string> Parse(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            if (value.Length >= 2 &&
                ((value[0] == '\'' && value[^1] == '\'') ||
                 (value[0] == '"' && value[^1] == '"')))
            {
                value = value[1..^1];
            }

            values[key] = value;
        }

        return values;
    }
}
