using System.Collections.Generic;
using UnityEngine;

namespace NX10
{
    public class NX10AttributesManager : MonoBehaviour
    {
        private readonly Dictionary<string, object> currentGameAttributes = new Dictionary<string, object>();
        private bool hasUnsentChanges;

        public System.Action<Dictionary<string, object>> sendAttributesRequest;

        public void SetAttributes(Dictionary<string, object> attributes)
        {
            if (attributes == null || !NX10Manager.Instance.HasConsent)
            {
                return;
            }

            bool changed = false;
            foreach (var kvp in attributes)
            {
                if (SetAttributeInternal(kvp.Key, kvp.Value))
                {
                    changed = true;
                }
            }

            if (!changed && !hasUnsentChanges)
                return;

            if (!NX10Manager.Instance.IsSessionValid)
            {
                hasUnsentChanges = true;
                return;
            }

            SendAttributes();
        }

        public void SetAttribute(string key, object value, bool sendAttributes = false)
        {
            if (string.IsNullOrEmpty(key) || !NX10Manager.Instance.HasConsent)
            {
                return;
            }

            bool changed = SetAttributeInternal(key, value);

            if (!NX10Manager.Instance.IsSessionValid)
            {
                if (changed)
                    hasUnsentChanges = true;
                return;
            }

            if (sendAttributes && (changed || hasUnsentChanges))
            {
                SendAttributes();
            }
        }

        private bool SetAttributeInternal(string key, object value)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            if (!currentGameAttributes.TryGetValue(key, out var existingValue) || !Equals(existingValue, value))
            {
                currentGameAttributes[key] = value;
                return true;
            }

            return false;
        }

        public void RemoveAttribute(string key)
        {
            if (string.IsNullOrEmpty(key) || !NX10Manager.Instance.HasConsent)
            {
                return;
            }

            if (currentGameAttributes.Remove(key))
            {
                if (NX10Manager.Instance.IsSessionValid)
                {
                    SendAttributes();
                }
                else
                {
                    hasUnsentChanges = true;
                }
            }
        }

        public void ClearAttributes()
        {
            if (!NX10Manager.Instance.HasConsent)
            {
                return;
            }

            if (currentGameAttributes.Count == 0)
                return;

            currentGameAttributes.Clear();

            if (NX10Manager.Instance.IsSessionValid)
            {
                SendAttributes();
            }
            else
            {
                hasUnsentChanges = false;
            }
        }

        public void FlushPendingAttributes()
        {
            if (!hasUnsentChanges || !NX10Manager.Instance.CanCollect || currentGameAttributes.Count == 0)
                return;

            SendAttributes();
        }

        public void ResetLocalState()
        {
            currentGameAttributes.Clear();
            hasUnsentChanges = false;
        }

        private void SendAttributes()
        {
            if (!NX10Manager.Instance.CanCollect)
                return;

            hasUnsentChanges = false;
            sendAttributesRequest?.Invoke(new Dictionary<string, object>(currentGameAttributes));
        }
    }
}
