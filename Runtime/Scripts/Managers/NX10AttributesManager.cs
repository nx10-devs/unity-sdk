using System.Collections.Generic;
using UnityEngine;

namespace NX10
{
    public class NX10AttributesManager : MonoBehaviour
    {
        private Dictionary<string, object> currentGameAttributes = new Dictionary<string, object>();
        public System.Action<Dictionary<string, object>> sendAttributesRequest;

        public void SetAttributes(Dictionary<string, object> attributes)
        {
            foreach (var kvp in attributes)
            {
                string key = kvp.Key;
                object value = kvp.Value;

                SetAttribute(key, value);
            }

            if (!NX10Manager.Instance.Initialised)
            {
                return;
            }

            SendAttributes();
        }

        public void SetAttribute(string key, object value, bool sendAttributes = false)
        {
            if (!NX10Manager.Instance.Initialised)
            {
                Debug.LogError("NX10 Manager not initialised, ensure it is before setting an attribute");
                return;
            }

            if (!currentGameAttributes.TryGetValue(key, out var existingValue))
            {
                currentGameAttributes[key] = value;
            }
            else if (!Equals(existingValue, value))
            {
                currentGameAttributes[key] = value;
            }

            if (sendAttributes)
                SendAttributes();
        }

        public void RemoveAttribute(string key)
        {
            if (!NX10Manager.Instance.Initialised)
            {
                Debug.LogError("NX10 Manager not initialised, ensure it is before removing an attribute");
                return;
            }

            if (currentGameAttributes.ContainsKey(key))
            {
                currentGameAttributes.Remove(key);
                SendAttributes();
            }
        }

        public void ClearAttributes()
        {
            if (!NX10Manager.Instance.Initialised)
            {
                Debug.LogError("NX10 Manager not initialised, ensure it is before clearing attributes");
                return;
            }

            currentGameAttributes.Clear();
            SendAttributes();
        }

        private void SendAttributes()
        {
            sendAttributesRequest?.Invoke(currentGameAttributes);
        }
    }
}
