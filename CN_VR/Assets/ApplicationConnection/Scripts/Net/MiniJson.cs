using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CollectiveNarratives.Net
{
    // Net genoeg JSON om SignalR-berichten uit elkaar te halen. JsonUtility kan de envelop niet lezen
    // (arguments is een lijst met verschillende types), dus we knippen hier de ruwe stukken eruit
    // en geven die daarna aan JsonUtility. Geen Unity-code: ook getest vanuit webapp/tests.
    public static class MiniJson
    {
        // {"a":1,"b":{"c":2}} -> a = "1", b = "{\"c\":2}" (ruwe JSON per veld, alleen het bovenste niveau).
        public static Dictionary<string, string> ParseObject(string json)
        {
            var result = new Dictionary<string, string>();
            int i = SkipWhitespace(json, 0);
            if (i >= json.Length || json[i] != '{') throw new FormatException("JSON-object verwacht");
            i = SkipWhitespace(json, i + 1);
            if (i < json.Length && json[i] == '}') return result;
            while (i < json.Length)
            {
                int keyEnd = SkipString(json, i);
                string key = ReadString(json.Substring(i, keyEnd - i));
                i = SkipWhitespace(json, keyEnd);
                if (i >= json.Length || json[i] != ':') throw new FormatException("':' verwacht");
                i = SkipWhitespace(json, i + 1);
                int valueEnd = SkipValue(json, i);
                result[key] = json.Substring(i, valueEnd - i);
                i = SkipWhitespace(json, valueEnd);
                if (i < json.Length && json[i] == ',') { i = SkipWhitespace(json, i + 1); continue; }
                if (i < json.Length && json[i] == '}') return result;
                throw new FormatException("',' of '}' verwacht");
            }
            throw new FormatException("Onvolledig JSON-object");
        }

        // [1,"x",{...}] -> "1", "\"x\"", "{...}"
        public static List<string> ParseArray(string json)
        {
            var result = new List<string>();
            int i = SkipWhitespace(json, 0);
            if (i >= json.Length || json[i] != '[') throw new FormatException("JSON-lijst verwacht");
            i = SkipWhitespace(json, i + 1);
            if (i < json.Length && json[i] == ']') return result;
            while (i < json.Length)
            {
                int end = SkipValue(json, i);
                result.Add(json.Substring(i, end - i));
                i = SkipWhitespace(json, end);
                if (i < json.Length && json[i] == ',') { i = SkipWhitespace(json, i + 1); continue; }
                if (i < json.Length && json[i] == ']') return result;
                throw new FormatException("',' of ']' verwacht");
            }
            throw new FormatException("Onvolledige JSON-lijst");
        }

        // "\"tekst\"" -> "tekst". null bij "null".
        public static string ReadString(string raw)
        {
            raw = raw.Trim();
            if (raw == "null") return null;
            if (raw.Length < 2 || raw[0] != '"' || raw[raw.Length - 1] != '"') throw new FormatException("JSON-tekst verwacht");
            var sb = new StringBuilder(raw.Length);
            for (int i = 1; i < raw.Length - 1; i++)
            {
                char c = raw[i];
                if (c != '\\') { sb.Append(c); continue; }
                char e = raw[++i];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        sb.Append((char)int.Parse(raw.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: sb.Append(e); break; // \" \\ \/
                }
            }
            return sb.ToString();
        }

        // tekst -> "\"tekst\"" (of null -> "null")
        public static string Quote(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        public static string Number(int n) => n.ToString(CultureInfo.InvariantCulture);

        public static int ReadInt(string raw) => int.Parse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);

        public static bool ReadBool(string raw) => raw.Trim() == "true";

        static int SkipWhitespace(string s, int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            return i;
        }

        // Geeft de positie net na de afsluitende ".
        static int SkipString(string s, int i)
        {
            if (i >= s.Length || s[i] != '"') throw new FormatException("JSON-tekst verwacht");
            for (i++; i < s.Length; i++)
            {
                if (s[i] == '\\') i++;
                else if (s[i] == '"') return i + 1;
            }
            throw new FormatException("Onvolledige JSON-tekst");
        }

        static int SkipValue(string s, int i)
        {
            if (i >= s.Length) throw new FormatException("JSON-waarde verwacht");
            char c = s[i];
            if (c == '"') return SkipString(s, i);
            if (c == '{' || c == '[')
            {
                int depth = 0;
                while (i < s.Length)
                {
                    char d = s[i];
                    if (d == '"') { i = SkipString(s, i); continue; }
                    if (d == '{' || d == '[') depth++;
                    else if (d == '}' || d == ']') depth--;
                    i++;
                    if (depth == 0) return i;
                }
                throw new FormatException("Onvolledige JSON");
            }
            // getal, true, false, null
            while (i < s.Length && s[i] != ',' && s[i] != '}' && s[i] != ']' && !char.IsWhiteSpace(s[i])) i++;
            return i;
        }
    }
}
