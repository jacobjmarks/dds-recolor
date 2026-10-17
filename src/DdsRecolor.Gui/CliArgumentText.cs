using System.Text;

namespace DdsRecolor.Gui;

public static class CliArgumentText
{
    public static bool TrySplit(string text, out string[] arguments, out string error)
    {
        var result = new List<string>();
        var token = new StringBuilder();
        char quote = '\0';
        bool tokenStarted = false;

        for (int i = 0; i < text.Length; i++)
        {
            char current = text[i];
            if (quote != '\0')
            {
                if (current == quote)
                {
                    quote = '\0';
                }
                else if (current == '\\' && quote == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    token.Append('"');
                    i++;
                }
                else
                {
                    token.Append(current);
                }
                continue;
            }

            if (current is '\'' or '"')
            {
                quote = current;
                tokenStarted = true;
            }
            else if (char.IsWhiteSpace(current))
            {
                if (tokenStarted)
                {
                    result.Add(token.ToString());
                    token.Clear();
                    tokenStarted = false;
                }
            }
            else
            {
                token.Append(current);
                tokenStarted = true;
            }
        }

        if (quote != '\0')
        {
            arguments = [];
            error = "A quoted argument is missing its closing quote.";
            return false;
        }

        if (tokenStarted) result.Add(token.ToString());
        arguments = [.. result];
        error = string.Empty;
        return true;
    }
}