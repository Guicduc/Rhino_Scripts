using System;
using System.Runtime.InteropServices;
using Rhino.PlugIns;

namespace NestingRhino
{
    [Guid("52b05541-5ff6-4c93-8cc8-9158de55d87f")]
    public sealed class NestingRhinoPlugin : PlugIn
    {
        public NestingRhinoPlugin()
        {
            Instance = this;
        }

        public static NestingRhinoPlugin Instance { get; private set; }
    }
}

