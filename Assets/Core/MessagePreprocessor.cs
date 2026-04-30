using System.Globalization;
using System.Linq;
using System.Text;

namespace Mikk.Avatar
{
    public static class MessagePreprocessor
    {
        private static readonly string[] LaughEmojis = { "😂", "🤣", "😄", "😃", "😆", "😁" };
        private static readonly string[] SadEmojis = { "😢", "😭", "😔", "😞", "🥺" };
        private static readonly string[] AngryEmojis = { "😡", "🤬", "😤" };

        public struct ProcessedMessage
        {
            public string CleanText;
            public bool HasLaughter;
            public bool HasSadness;
            public bool HasAnger;
            public bool IsEmpty;
        }

        public static ProcessedMessage Process(string raw)
        {
            var result = new ProcessedMessage();

            if (string.IsNullOrWhiteSpace(raw))
            {
                result.IsEmpty = true;
                return result;
            }

            result.HasLaughter = LaughEmojis.Any(e => raw.Contains(e));
            result.HasSadness = SadEmojis.Any(e => raw.Contains(e));
            result.HasAnger = AngryEmojis.Any(e => raw.Contains(e));

            string textOnly = ExtractText(raw);

            if (string.IsNullOrWhiteSpace(textOnly))
            {
                if (result.HasLaughter) { result.CleanText = "hahahaha!"; return result; }
                if (result.HasSadness) { result.CleanText = "..."; return result; }
                if (result.HasAnger) { result.CleanText = "hmph!"; return result; }
                result.IsEmpty = true;
                return result;
            }



            result.CleanText = textOnly.Trim();
            return result;
        }

        private static string ExtractText(string input)
        {
            var sb = new StringBuilder();
            var enumerator = StringInfo.GetTextElementEnumerator(input);

            while (enumerator.MoveNext())
            {
                string element = enumerator.GetTextElement();

                if (element.Length == 1)
                {
                    char c = element[0];
                    if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || char.IsPunctuation(c))
                        sb.Append(c);
                }
                else
                {
                    // Keep Devanagari
                    foreach (char c in element)
                    {
                        if (c >= 0x0900 && c <= 0x097F)
                            sb.Append(c);
                    }
                }
            }

            return sb.ToString();
        }
    }
}