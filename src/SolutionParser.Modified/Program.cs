using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Build.Construction;
using Microsoft.Build.Definition;
using Microsoft.Build.Locator;
using MSProject = Microsoft.Build.Evaluation.Project;
using Models;

if (args.Length < 1)
{
    throw new ArgumentException("Solution path expected as first argument");
}
Console.WriteLine(RuntimeInformation.FrameworkDescription);
string solution = args[0];

InitializeMSBuilePath(solution);
ExecuteCore(solution);

static void InitializeMSBuilePath(string solution)
{
    var installations = MSBuildLocator.QueryVisualStudioInstances(new VisualStudioInstanceQueryOptions()
    {
        DiscoveryTypes = DiscoveryType.DotNetSdk,
        AllowAllRuntimeVersions = true,
    });

    MSBuildLocator.RegisterInstance(installations.First());
}

static int ExecuteCore(string solution)
{
    string solutionPath = Path.GetFullPath(solution);
    IEnumerable<ProjectRecord>? projFiles = null;

    if (!solutionPath.EndsWith(".sln") && Directory.Exists(solutionPath))
    {
        string[] projFileGlobs = new string[] { "*.csproj", "*.fsproj" };
        projFiles = projFileGlobs
            .SelectMany(glob => Directory.GetFiles(solutionPath, glob))
            .Select(p => new ProjectRecord(Path.GetFileNameWithoutExtension(p), p));
    }

    if (File.Exists(solution) && projFiles is null)
    {
        var sln = SolutionFile.Parse(solution);
        projFiles = sln.ProjectsInOrder.Where(prj => prj.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat)
           .Select(prj => new ProjectRecord(prj.ProjectName, prj.AbsolutePath));
    }

    if (projFiles is null)
    {
        Console.WriteLine("Invalid solution path");
        return 1;
    }

    var projects = new ConcurrentBag<Project>();
    Parallel.ForEach(projFiles, proj =>
    {
        var projectDetails = GetProjectDetails(proj.Name, proj.Path);
        if (projectDetails != null)
            projects.Add(projectDetails);
    });

    var allProjects = projects.ToList();

    List<ProjectFile> designerFiles = new();

    foreach (var proj in allProjects)
    {
        proj.CoreProject?.GetItems("AvaloniaXaml").ToList().ForEach(item =>
        {
            var filePath = Path.GetFullPath(item.EvaluatedInclude, proj.DirectoryPath ?? "");
            var designerFile = new ProjectFile
            {
                Path = filePath,
                TargetPath = proj.TargetPath,
                ProjectPath = proj.Path
            };
            designerFiles.Add(designerFile);
        });
    }

    var json = new { solution, Projects = allProjects, Files = designerFiles };

    var jsonStr = JsonSerializer.Serialize(json, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });

    string jsonFilePath = Path.Combine(Path.GetTempPath(), $"{Path.GetFileName(solution)}.json");
    File.WriteAllText(jsonFilePath, jsonStr);

    Console.WriteLine(jsonStr);

    return 0;
}

static Project? GetProjectDetails(string name, string projPath)
{
    try
    {
        var proj = MSProject.FromFile(projPath, new ProjectOptions());

        var assembly = proj.GetPropertyValue("TargetPath");
        var outputType = proj.GetPropertyValue("outputType");
        var desingerHostPath = proj.GetPropertyValue("AvaloniaPreviewerNetCoreToolPath");

        var targetfx = proj.GetPropertyValue("TargetFramework");
        var projectDepsFilePath = proj.GetPropertyValue("ProjectDepsFilePath");
        var projectRuntimeConfigFilePath = proj.GetPropertyValue("ProjectRuntimeConfigFilePath");

        var references = proj.GetItems("ProjectReference");
        var referencesPath = references.Select(p => Path.GetFullPath(p.EvaluatedInclude, projPath)).ToArray();
        desingerHostPath = string.IsNullOrEmpty(desingerHostPath) ? "" : Path.GetFullPath(desingerHostPath);

        var intermediateOutputPath = GetIntermediateOutputPath(proj);

        return new Project
        {
            Name = name,
            Path = projPath,
            TargetPath = assembly,
            OutputType = outputType,
            DesignerHostPath = desingerHostPath,

            TargetFramework = targetfx,
            DepsFilePath = projectDepsFilePath,
            RuntimeConfigFilePath = projectRuntimeConfigFilePath,

            CoreProject = proj,
            ProjectReferences = referencesPath,
            IntermediateOutputPath = intermediateOutputPath

        };
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error parsing project {name}: {ex.Message}");
        return null;
    }
}

static string GetIntermediateOutputPath(MSProject proj)
{
    var intermediateOutputPath = proj.GetPropertyValue("IntermediateOutputPath");
    var iop = Path.Combine(intermediateOutputPath, "Avalonia", "references");

    if (!Path.IsPathRooted(intermediateOutputPath))
    {
        iop = Path.Combine(proj.DirectoryPath ?? "", iop);
        if (Path.DirectorySeparatorChar == '/')
            iop = iop.Replace("\\", "/");
    }

    return iop;
}

record ProjectRecord(string Name, string Path);