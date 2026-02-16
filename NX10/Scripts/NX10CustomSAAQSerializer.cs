using System.Text;
using UnityEngine;

namespace NX10
{
    public static class NX10CustomSAAQSerializer
    {
        public static string Serialize(NX10BackendManager.NX10SAAQResponse request)
        {
            string json = JsonUtility.ToJson(request);

            if (request.metaData == null || request.metaData.IsEmpty())
            {
                return ReplaceMetaData(json, "{}");
            }

            var dict = request.metaData.ToDictionary();
            var sb = new StringBuilder();
            sb.Append("{");

            bool first = true;
            foreach (var kvp in dict)
            {
                if (!first)
                    sb.Append(",");

                sb.Append($"\"{Escape(kvp.Key)}\":\"{Escape(kvp.Value.ToString())}\"");
                first = false;
            }

            sb.Append("}");

            return ReplaceMetaData(json, sb.ToString());
        }

        private static string ReplaceMetaData(string json, string newMetaDataObject)
        {
            const string metaKey = "\"metaData\":";

            int keyIndex = json.IndexOf(metaKey);
            if (keyIndex == -1)
                return json;

            int startBrace = json.IndexOf('{', keyIndex);
            if (startBrace == -1)
                return json;

            int braceCount = 0;
            int endBrace = -1;

            for (int i = startBrace; i < json.Length; i++)
            {
                if (json[i] == '{') braceCount++;
                if (json[i] == '}') braceCount--;

                if (braceCount == 0)
                {
                    endBrace = i;
                    break;
                }
            }

            if (endBrace == -1)
                return json;

            string before = json.Substring(0, keyIndex);
            string after = json.Substring(endBrace + 1);

            return before + "\"metaData\":" + newMetaDataObject + after;
        }

        private static string Escape(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }
    }
}

