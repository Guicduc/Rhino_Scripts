using System;
using System.Runtime.InteropServices;
using Rhino;
using Rhino.Commands;

namespace NestingRhino
{
    [Guid("6bed3c02-5627-468a-b6cc-8f25c5f31c1d")]
    public sealed class NestingRhinoCommand : Command
    {
        public override string EnglishName
        {
            get { return "NestingRhino"; }
        }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            NestingRhinoScript.Run();
            return Result.Success;
        }
    }
}
