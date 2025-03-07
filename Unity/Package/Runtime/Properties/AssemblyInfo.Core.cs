// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.





using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("ClearScript Core Library")]
[assembly: AssemblyProduct("ClearScript")]
[assembly: AssemblyCopyright("(c) Microsoft Corporation")]
[assembly: InternalsVisibleTo("Decentraland.ClearScript.Tests")]

[assembly: ComVisible(false)]
[assembly: AssemblyVersion("7.4.5")]
[assembly: AssemblyFileVersion("7.4.5")]
[assembly: AssemblyInformationalVersion("7.4.5")]

namespace Microsoft.ClearScript.Properties
{
    internal static class ClearScriptVersion
    {
        public const string Triad = "7.4.5";
        public const string Informational = "7.4.5";
    }
}
