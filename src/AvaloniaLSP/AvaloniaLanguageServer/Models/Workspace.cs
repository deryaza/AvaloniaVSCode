using System.Collections;
using System.Diagnostics;
using System.Text.Json;
using Avalonia.Ide.CompletionEngine;
using Avalonia.Ide.CompletionEngine.AssemblyMetadata;
using Avalonia.Ide.CompletionEngine.DnlibMetadataProvider;
using AvaloniaLanguageServer.Services;
using AvaloniaLanguageServer.Utilities;
using Microsoft.Build.Framework;
using Microsoft.Build.Tasks;
using Microsoft.Build.Utilities;
using Task = System.Threading.Tasks.Task;


namespace AvaloniaLanguageServer.Models;

public class Workspace
{
    public ProjectInfo? ProjectInfo { get; private set; }
    public BufferService BufferService { get; } = new();

    public async Task InitializeAsync(DocumentUri uri)
    {
        try
        {
            ProjectInfo = await ProjectInfo.GetProjectInfoAsync(uri);
            CompletionMetadata = BuildCompletionMetadata(uri);
        }
        catch (Exception e)
        {
            throw new Exception($"Failed to initialize workspace: {uri}", e);
        }
    }

    Metadata? BuildCompletionMetadata(DocumentUri uri)
    {
        Debugger.Launch();
        var slnFile = SolutionName(uri) ?? Path.GetFileNameWithoutExtension(ProjectInfo?.ProjectDirectory);

        if (slnFile == null)
            return null;


        var slnFilePath = Path.Combine(Path.GetTempPath(), $"{slnFile}.json");

        if (!File.Exists(slnFilePath))
            return null;

        string content = File.ReadAllText(slnFilePath);
        var package = JsonSerializer.Deserialize<SolutionData>(content);

        string fileName = Path.GetFileNameWithoutExtension(ProjectInfo?.ProjectPath);
        var exeProj = package.Projects.FirstOrDefault(x => string.Equals(x.Name, fileName, StringComparison.OrdinalIgnoreCase));

        IEnumerable<string> rar = null;
        if (exeProj?.IntermediateOutputPath is { } s)
        {
            string rarPath = Path.Combine(s, $"{exeProj.Name}.csproj.AssemblyReference.cache");
            if (File.Exists(rarPath))
            {
                var cache = StateFileBase.DeserializeCache<SystemState>(rarPath, new(new DummyBuildEngine(), "AvaloniaLSP"));
                rar = cache.instanceLocalFileStateCache.Keys;
            }
        }

        return _metadataReader.GetForTargetAssembly(exeProj?.TargetPath ?? "", rar);
    }

    class DummyBuildEngine : IBuildEngine
    {
        public void LogErrorEvent(BuildErrorEventArgs e)
        {
        }

        public void LogWarningEvent(BuildWarningEventArgs e)
        {
        }

        public void LogMessageEvent(BuildMessageEventArgs e)
        {
        }

        public void LogCustomEvent(CustomBuildEventArgs e)
        {
        }

        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs)
        {
            return false;
        }

        public bool ContinueOnError { get; }
        public int LineNumberOfTaskNode { get; }
        public int ColumnNumberOfTaskNode { get; }
        public string ProjectFileOfTaskNode { get; }
    }

    string? SolutionName(DocumentUri uri)
    {
        string path = Utils.FromUri(uri);
        string root = Directory.GetDirectoryRoot(path);
        string? current = Path.GetDirectoryName(path);

        if (!File.Exists(path) || current == null)
            return null;

        var files = Array.Empty<FileInfo>();

        while (root != current && files.Length == 0)
        {
            var directory = new DirectoryInfo(current!);
            files = directory.GetFiles("*.sln", SearchOption.TopDirectoryOnly);

            if (files.Length != 0)
                break;

            current = Path.GetDirectoryName(current);
        }

        return files.FirstOrDefault()?.Name;
    }

    public Metadata? CompletionMetadata { get; private set; }
    readonly MetadataReader _metadataReader = new(new DnlibMetadataProvider());
}