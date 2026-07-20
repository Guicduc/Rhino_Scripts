using System;
using System.Runtime.InteropServices;
using Rhino.PlugIns;

namespace ThreadRhino
{
    [Guid("a71bfe5e-1f0c-4684-8a82-d1034e8fbaf7")]
    public sealed class ThreadRhinoPlugin : PlugIn
    {
        public ThreadRhinoPlugin()
        {
            Instance = this;
        }

        public static ThreadRhinoPlugin Instance { get; private set; }
    }
}
