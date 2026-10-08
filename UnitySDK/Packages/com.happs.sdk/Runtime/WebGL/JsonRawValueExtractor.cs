using System;

namespace HAppsSDK
{
    internal static class JsonRawValueExtractor
    {
        public static bool TryGetNestedProperty(
            string json,
            string parentProperty,
            string childProperty,
            out string rawValue)
        {
            rawValue = null;
            return TryGetProperty(json, parentProperty, out var parentJson) &&
                TryGetProperty(parentJson, childProperty, out rawValue);
        }

        private static bool TryGetProperty(string json, string propertyName, out string rawValue)
        {
            rawValue = null;
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(propertyName))
                return false;

            var index = 0;
            SkipWhitespace(json, ref index);
            if (!Consume(json, ref index, '{'))
                return false;

            while (true)
            {
                SkipWhitespace(json, ref index);
                if (Consume(json, ref index, '}'))
                    return false;

                if (!TryReadPropertyName(json, ref index, out var name))
                    return false;

                SkipWhitespace(json, ref index);
                if (!Consume(json, ref index, ':'))
                    return false;

                SkipWhitespace(json, ref index);
                var valueStart = index;
                if (!TrySkipValue(json, ref index))
                    return false;

                if (string.Equals(name, propertyName, StringComparison.Ordinal))
                {
                    rawValue = json.Substring(valueStart, index - valueStart);
                    return true;
                }

                SkipWhitespace(json, ref index);
                if (Consume(json, ref index, '}'))
                    return false;
                if (!Consume(json, ref index, ','))
                    return false;
            }
        }

        private static bool TryReadPropertyName(string json, ref int index, out string name)
        {
            name = null;
            if (index >= json.Length || json[index] != '"')
                return false;

            index++;
            var start = index;
            while (index < json.Length)
            {
                if (json[index] == '\\')
                {
                    // Contract property names are emitted without escapes. Still skip a
                    // valid escape so parsing can continue safely for unrelated fields.
                    index += 2;
                    continue;
                }

                if (json[index] == '"')
                {
                    name = json.Substring(start, index - start);
                    index++;
                    return true;
                }

                index++;
            }

            return false;
        }

        private static bool TrySkipValue(string json, ref int index)
        {
            if (index >= json.Length)
                return false;

            switch (json[index])
            {
                case '"':
                    return TrySkipString(json, ref index);
                case '{':
                    return TrySkipObject(json, ref index);
                case '[':
                    return TrySkipArray(json, ref index);
                default:
                    var start = index;
                    while (index < json.Length &&
                        json[index] != ',' &&
                        json[index] != '}' &&
                        json[index] != ']' &&
                        !char.IsWhiteSpace(json[index]))
                    {
                        index++;
                    }
                    return index > start;
            }
        }

        private static bool TrySkipObject(string json, ref int index)
        {
            if (!Consume(json, ref index, '{'))
                return false;

            SkipWhitespace(json, ref index);
            if (Consume(json, ref index, '}'))
                return true;

            while (true)
            {
                if (!TrySkipString(json, ref index))
                    return false;
                SkipWhitespace(json, ref index);
                if (!Consume(json, ref index, ':'))
                    return false;
                SkipWhitespace(json, ref index);
                if (!TrySkipValue(json, ref index))
                    return false;
                SkipWhitespace(json, ref index);
                if (Consume(json, ref index, '}'))
                    return true;
                if (!Consume(json, ref index, ','))
                    return false;
                SkipWhitespace(json, ref index);
            }
        }

        private static bool TrySkipArray(string json, ref int index)
        {
            if (!Consume(json, ref index, '['))
                return false;

            SkipWhitespace(json, ref index);
            if (Consume(json, ref index, ']'))
                return true;

            while (true)
            {
                if (!TrySkipValue(json, ref index))
                    return false;
                SkipWhitespace(json, ref index);
                if (Consume(json, ref index, ']'))
                    return true;
                if (!Consume(json, ref index, ','))
                    return false;
                SkipWhitespace(json, ref index);
            }
        }

        private static bool TrySkipString(string json, ref int index)
        {
            if (!Consume(json, ref index, '"'))
                return false;

            while (index < json.Length)
            {
                if (json[index] == '\\')
                {
                    index += 2;
                    continue;
                }

                if (json[index] == '"')
                {
                    index++;
                    return true;
                }

                index++;
            }

            return false;
        }

        private static void SkipWhitespace(string value, ref int index)
        {
            while (index < value.Length && char.IsWhiteSpace(value[index]))
                index++;
        }

        private static bool Consume(string value, ref int index, char expected)
        {
            if (index >= value.Length || value[index] != expected)
                return false;
            index++;
            return true;
        }
    }
}
