using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NX10
{
    public class NX10SDKSession
    {
        public string Token { get; private set; }
        public IReadOnlyList<EndpointInfo> Endpoints => _endpoints;

        private List<EndpointInfo> _endpoints = new List<EndpointInfo>();

        public void Initialize(SessionStartData data)
        {
            Token = data.token;
            _endpoints = data.endpoints ?? new List<EndpointInfo>();
        }

        public string GetEndpoint(string type, string version)
        {
            return _endpoints.FindAll(e => e.type == type)?.FirstOrDefault(e => e.version == version)?.location;
        }

        public void Clear()
        {
            Token = null;
            _endpoints.Clear();
        }
    }
}
