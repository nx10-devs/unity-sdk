using System;
using System.Collections.Generic;

namespace NX10
{
    [Serializable]
    public class SessionStartResponse
    {
        public SessionStartData data;
    }

    [Serializable]
    public class SessionStartData
    {
        public string token;
        public List<EndpointInfo> endpoints;
    }

    [Serializable]
    public class EndpointInfo
    {
        public string location;
        public string type;
        public string version;
    }
}
