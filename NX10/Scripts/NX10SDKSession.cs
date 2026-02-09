using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NX10
{
    public class NX10SDKSession
    {
        public string Token { get; private set; }
        public IReadOnlyList<NX10BackendManager.EndpointInfo> Endpoints => _endpoints;

        private List<NX10BackendManager.EndpointInfo> _endpoints = new List<NX10BackendManager.EndpointInfo>();

        public void Initialize(NX10BackendManager.SessionStartData data)
        {
            Token = data.token;
            _endpoints = data.endpoints ?? new List<NX10BackendManager.EndpointInfo>();
        }

        public string GetEndpoint(string type)
        {
            return _endpoints.FirstOrDefault(e => e.type == type)?.location;
        }

        public void Clear()
        {
            Token = null;
            _endpoints.Clear();
        }
    }
}
