using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

try
{
    string rootPath = GetGitRepositoryRootPath();
    string binPath = CombinePath(rootPath, "bin/Release");
    string pluginsPath = CombinePath(rootPath, "Unity/Package/Plugins");
    string runtimePath = CombinePath(rootPath, "Unity/Package/Runtime");
    string sourcePath = CombinePath(rootPath, "ClearScript");

    try
    {
        foreach (string file in Directory.GetFiles(runtimePath, "*.cs",
            SearchOption.AllDirectories))
            File.Delete(file);
    }
    catch (DirectoryNotFoundException)
    {
        Directory.CreateDirectory(runtimePath);
    }

    foreach (string inFile in Directory.GetFiles(sourcePath, "*.cs",
        SearchOption.AllDirectories))
    {
        if (PathContains(inFile, "/ICUData/")
            || PathContains(inFile, "/Properties/")
                && !PathEndsWith(inFile, "/AssemblyInfo.Core.cs")
            || PathContains(inFile, "/Windows/")
            || PathContains(inFile, ".Net5.")
            || PathContains(inFile, ".NetCore.")
            || PathContains(inFile, ".NetFramework.")
            || PathContains(inFile, ".UWP.")
            || PathContains(inFile, ".Windows."))
        {
            continue;
        }

        string outFile = string.Concat(runtimePath,
            inFile.AsSpan(sourcePath.Length));

        Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);

        if (PathEndsWith(inFile, "/AssemblyInfo.Core.cs"))
        {
            using var reader = new StreamReader(inFile);
            using var writer = new StreamWriter(outFile);
            writer.NewLine = "\n";

            while (true)
            {
                string? line = reader.ReadLine();

                if (line == null)
                    goto endOfFile;

                if (line.StartsWith("[assembly: InternalsVisibleTo("))
                    break;

                writer.WriteLine(line);
            }

            writer.WriteLine("[assembly: InternalsVisibleTo(\"Decentraland.ClearScript.Tests\")]");

            while (true)
            {
                string? line = reader.ReadLine();

                if (line == null)
                    goto endOfFile;

                if (!line.StartsWith("[assembly: InternalsVisibleTo("))
                {
                    writer.WriteLine(line);
                    break;
                }
            }

            while (true)
            {
                string? line = reader.ReadLine();

                if (line == null)
                    goto endOfFile;

                writer.WriteLine(line);
            }

        endOfFile:;
        }
        else if (PathEndsWith(inFile, "/V8SplitProxyManaged.cs"))
        {
            using var reader = new StreamReader(inFile);
            using var writer = new StreamWriter(outFile);
            writer.NewLine = "\n";
            var methods = new Dictionary<string, string>();

            while (true)
            {
                string? line = reader.ReadLine();

                if (line == null)
                    break;

                {
                    Match match = Regex.Match(line, @"\bGetMethodPair<(\w+)>\((\w+)\)");

                    if (match.Success)
                    {
                        string delegateType = match.Groups[1].Value;
                        string method = match.Groups[2].Value;
                        methods.Add(method, delegateType);
                    }
                }

                {
                    Match match = Regex.Match(line, @"^(\s*)private static \w+ (\w+)\([^)]*\)");

                    if (match.Success)
                    {
                        string method = match.Groups[2].Value;

                        if (methods.TryGetValue(method, out string? delegateType))
                        {
                            writer.Write(match.Groups[1].Value);
                            writer.Write("[AOT.MonoPInvokeCallback(typeof(");
                            writer.Write(delegateType);
                            writer.WriteLine("))]");
                        }
                    }
                }

                writer.WriteLine(line);
            }
        }
        else
        {
            File.Copy(inFile, outFile);
        }
    }



    DeleteEmptyFolders(runtimePath);
}
catch (Exception ex)
{
    Console.WriteLine(ex);
    throw;
}

// TODO: Write tests in MSTest and convert them to NUnit.
/*try
{
    string srcPath = @"..\..\..\..\..\ClearScriptTest";
    string dstPath = @"..\..\..\..\Package\Tests\Runtime";

    foreach (string file in Directory.GetFiles(dstPath, "*.cs", SearchOption.AllDirectories))
    {
        File.Delete(file);
    }

    foreach (string file in Directory.GetFiles(srcPath, "*.cs", SearchOption.AllDirectories))
    {
        if (file.Contains(".NetCore.")
            || file.Contains(".NetFramework."))
        {
            continue;
        }

        string dstFile = string.Concat(dstPath, file.AsSpan(srcPath.Length));
        Directory.CreateDirectory(Path.GetDirectoryName(dstFile)!);
        using var reader = new StreamReader(file);
        using var writer = new StreamWriter(dstFile);
        writer.NewLine = "\n";

        while (true)
        {
            string? line = reader.ReadLine();

            if (line == null)
                break;
            else if (line == "using Microsoft.VisualStudio.TestTools.UnitTesting;")
                writer.WriteLine("using NUnit.Framework;");
            else if (line == "    [TestClass]")
                writer.WriteLine("    [TestFixture]");
            else if (line == "        [TestInitialize]")
                writer.WriteLine("        [SetUp]");
            else if (line == "        [TestCleanup]")
                writer.WriteLine("        [TearDown]");
            else if (line.StartsWith("        [TestMethod, TestCategory(\""))
                writer.WriteLine("        [Test]");
            else
                writer.WriteLine(line);
        }
    }

    DeleteEmptyFolders(dstPath);
}
catch (Exception ex)
{
    Console.WriteLine(ex);
    try { Console.ReadKey(true); }
    catch (InvalidOperationException) { }
    throw;
}*/

static void DeleteEmptyFolders(string path)
{
    foreach (string metaFile in Directory.GetFiles(path, "*.meta", SearchOption.AllDirectories))
    {
        string fileOrFolder = metaFile[..^".meta".Length];

        if (File.Exists(fileOrFolder))
            continue;

        if (Directory.Exists(fileOrFolder))
        {
            string[] files = Directory.GetFiles(fileOrFolder, "", SearchOption.AllDirectories);

            if (files.Any(i => !i.EndsWith(".meta")))
                continue;

            // Delete meta files of directories that contain nothing but meta files. These directories
            // will then become empty and eligible for deletion in the next step.
        }

        File.Delete(metaFile);
    }

    foreach (string folder in Directory.GetDirectories(path, "", SearchOption.AllDirectories))
    {
        try
        {
            if (Directory.GetFiles(folder, "", SearchOption.AllDirectories).Length == 0)
                Directory.Delete(folder, true);
        }
        catch (DirectoryNotFoundException) { }
    }
}

static string GetGitRepositoryRootPath()
{
    string? path = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location);

    if (string.IsNullOrEmpty(path))
        throw new DirectoryNotFoundException("Can't obtain own executable path");

    while (path is not null)
    {
        if (Directory.Exists(Path.Combine(path, ".git")))
            return path;

        path = Path.GetDirectoryName(path);
    }

    throw new DirectoryNotFoundException("No ancestor folder contains a .git folder");
}

static string NormalizePath(string path) =>
    path.Replace('/', Path.DirectorySeparatorChar);

static bool PathContains(string path, string value) =>
    NormalizePath(path).Contains(value);

static bool PathEndsWith(string path, string value) =>
    path.EndsWith(NormalizePath(value));

static void CopyFileIfExists(string sourceFileName, string destFileName)
{
    try { File.Copy(sourceFileName, destFileName); }
    catch (FileNotFoundException) { }
}
