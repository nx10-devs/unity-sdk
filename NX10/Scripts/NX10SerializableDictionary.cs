using System;
using System.Collections.Generic;

namespace NX10
{
    [Serializable]
    public class NX10SerializableDictionary
    {
        [Serializable]
        public class Entry
        {
            public string key;
            public object value;
        }

        public List<Entry> entries = new List<Entry>();

        public NX10SerializableDictionary() { }

        public NX10SerializableDictionary(Dictionary<string, object> dictionary)
        {
            FromDictionary(dictionary);
        }

        public void FromDictionary(Dictionary<string, object> dictionary)
        {
            entries.Clear();

            if (dictionary == null)
                return;

            foreach (var kvp in dictionary)
            {
                entries.Add(new Entry
                {
                    key = kvp.Key,
                    value = kvp.Value
                });
            }
        }

        public Dictionary<string, object> ToDictionary()
        {
            var dict = new Dictionary<string, object>();

            foreach (var entry in entries)
            {
                dict[entry.key] = entry.value;
            }

            return dict;
        }

        public bool IsEmpty()
        {
            return entries == null || entries.Count == 0;
        }
    }

}
