#:package Microsoft.CodeAnalysis.CSharp@5.9.0

using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

string[] tryPatternExceptions = ["FormatException", "OverflowException", "KeyNotFoundException"];
string[] frameMethods = ["Update", "LateUpdate", "FixedUpdate"];
string[] redrawMethods = ["OnGUI", "OnSceneGUI", "OnInspectorGUI"];
string[] cachedLookups = ["GetComponent", "GetComponents", "GetComponentInChildren", "GetComponentInParent", "PropertyToID", "StringToHash", "FindObjectOfType", "FindObjectsOfType"];
string[] linqMethods = ["Where", "Select", "SelectMany", "OrderBy", "OrderByDescending", "ToList", "ToArray", "ToDictionary", "Any", "All", "First", "FirstOrDefault", "Last", "LastOrDefault", "Count", "Sum", "Min", "Max", "GroupBy", "Distinct", "Concat", "Skip", "Take"];
string[] meshArrays = ["vertices", "normals", "tangents", "uv", "triangles", "colors", "colors32", "boneWeights"];
string[] clockMembers = ["Now", "UtcNow", "Today"];
string[] rootFiles = ["global.json", "Directory.Build.props", ".editorconfig", ".gitattributes"];
string[] unityFolders = ["Core", "Runtime", "Editor", "Tests/Editor"];

return Run(Path.GetFullPath(args.Length > 0 ? args[0] : "."), args.Length > 1 ? args[1] : null);

int Run(string root, string? vendoredList)
{
    List<string> tracked = ListTracked(root);
    string list = vendoredList ?? Path.Combine(root, "vendored-paths.txt");
    if (!File.Exists(list))
    {
        Console.Error.WriteLine(list + ": missing, install lint/common first");
        return 2;
    }

    (List<string> vendoredPrefixes, HashSet<string> vendoredNames) = ReadVendoredList(list);

    List<string> own = [.. tracked.Where(path => !IsVendored(path, vendoredPrefixes, vendoredNames))];
    bool unity = own.Exists(path => path.EndsWith(".asmdef", StringComparison.Ordinal)) || File.Exists(Path.Combine(root, "ProjectSettings", "ProjectVersion.txt"));
    List<string> packageRoots = unity ? FindPackageRoots(root, own) : [];
    List<string> findings = [];
    List<(string Path, SyntaxNode Root)> trees = [];
    foreach (string path in own.Where(path => path.EndsWith(".cs", StringComparison.Ordinal)))
    {
        string text = File.ReadAllText(Path.Combine(root, path));
        trees.Add((path, CSharpSyntaxTree.ParseText(text, path: path).GetRoot()));
        if (PackageRoot(path, packageRoots) is not null && !text.StartsWith("#nullable enable", StringComparison.Ordinal))
        {
            findings.Add($"{path}(1,1): CS-3 first line must be #nullable enable");
        }
    }

    HashSet<string> caught = [.. trees.SelectMany(tree => tree.Root.DescendantNodes().OfType<CatchDeclarationSyntax>()).Select(declaration => LastName(declaration.Type))];
    foreach ((string path, SyntaxNode tree) in trees)
    {
        bool tests = path.Split('/').Any(segment => segment.EndsWith(".Tests", StringComparison.Ordinal) || segment is "Tests");
        bool core = path.Split('/').Any(segment => segment.EndsWith(".Core", StringComparison.Ordinal));
        CheckTypes(path, tree, caught, findings);
        CheckCatches(path, tree, findings);
        CheckStringLoops(path, tree, findings);
        if (unity)
        {
            CheckUnity(path, tree, findings);
        }
        else
        {
            CheckDotNet(path, tree, tests, core, findings);
        }
    }

    if (unity)
    {
        CheckUnityLayout(root, own, packageRoots, findings);
    }
    else
    {
        CheckDotNetLayout(root, own, trees, findings);
    }

    foreach (string finding in findings)
    {
        Console.WriteLine(finding);
    }

    return findings.Count == 0 ? 0 : 1;
}

static List<string> ListTracked(string root)
{
    ProcessStartInfo start = new("git")
    {
        RedirectStandardOutput = true,
        UseShellExecute = false,
    };
    start.ArgumentList.Add("-C");
    start.ArgumentList.Add(root);
    start.ArgumentList.Add("ls-files");
    start.ArgumentList.Add("-z");
    using Process git = Process.Start(start) ?? throw new InvalidOperationException("git ls-files didn't start");
    string output = git.StandardOutput.ReadToEnd();
    git.WaitForExit();
    return git.ExitCode == 0
        ? [.. output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Where(path => File.Exists(Path.Combine(root, path)))]
        : throw new InvalidOperationException($"git ls-files failed in {root}");
}

static (List<string> Prefixes, HashSet<string> Names) ReadVendoredList(string list)
{
    List<string> prefixes = [];
    HashSet<string> names = [];
    foreach (string raw in File.ReadLines(list))
    {
        string entry = raw.Trim();
        if (entry.StartsWith('/'))
        {
            prefixes.Add(entry.Trim('/') + "/");
        }
        else if (entry.Length > 0)
        {
            _ = names.Add(entry.Trim('/'));
        }
    }

    return (prefixes, names);
}

static bool IsVendored(string path, List<string> prefixes, HashSet<string> names) =>
    prefixes.Exists(prefix => path.StartsWith(prefix, StringComparison.Ordinal)) || path.Split('/')[..^1].Any(names.Contains);

static List<string> FindPackageRoots(string root, List<string> own) =>
    [.. own.Where(path => path is "package.json" || path.EndsWith("/package.json", StringComparison.Ordinal))
        .Where(path => File.ReadAllText(Path.Combine(root, path)).Contains("\"unity\"", StringComparison.Ordinal))
        .Select(path => path[..^"package.json".Length])];

static string? PackageRoot(string path, List<string> packageRoots) =>
    packageRoots.Where(prefix => path.StartsWith(prefix, StringComparison.Ordinal)).OrderByDescending(prefix => prefix.Length).FirstOrDefault();

static string PackageName(string root, string package)
{
    using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, package + "package.json")));
    return manifest.RootElement.TryGetProperty("name", out JsonElement name) ? name.GetString() ?? string.Empty : string.Empty;
}

static string LastName(TypeSyntax? type)
{
    return type switch
    {
        QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
        SimpleNameSyntax simple => simple.Identifier.ValueText,
        null => string.Empty,
        _ => type.ToString(),
    };
}

static void Add(List<string> findings, string path, SyntaxNode node, string rule, string message)
{
    FileLinePositionSpan span = node.GetLocation().GetLineSpan();
    findings.Add($"{path}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1}): {rule} {message}");
}

static void CheckTypes(string path, SyntaxNode tree, HashSet<string> caught, List<string> findings)
{
    foreach (TypeDeclarationSyntax type in tree.DescendantNodes().OfType<TypeDeclarationSyntax>())
    {
        if (type is ClassDeclarationSyntax or StructDeclarationSyntax && type.ParameterList is not null)
        {
            Add(findings, path, type, "CS-10", $"{type.Identifier.ValueText} has a primary constructor; assign readonly fields in a constructor");
        }

        string baseName = LastName(type.BaseList?.Types.FirstOrDefault()?.Type);
        if (type is ClassDeclarationSyntax && baseName.EndsWith("Exception", StringComparison.Ordinal) && !caught.Contains(type.Identifier.ValueText))
        {
            Add(findings, path, type, "CS-11", $"{type.Identifier.ValueText} is never caught; throw a BCL exception instead");
        }
    }
}

void CheckCatches(string path, SyntaxNode tree, List<string> findings)
{
    foreach (CatchClauseSyntax clause in tree.DescendantNodes().OfType<CatchClauseSyntax>())
    {
        if (tryPatternExceptions.Contains(LastName(clause.Declaration?.Type)))
        {
            Add(findings, path, clause, "CS-7", "use the Try method instead of catching " + LastName(clause.Declaration?.Type));
        }

        bool rethrowsConditionally = clause.Block.Statements.OfType<IfStatementSyntax>()
            .Any(branch => branch.Statement.DescendantNodesAndSelf().OfType<ThrowStatementSyntax>().Any(statement => statement.Expression is null));
        if (rethrowsConditionally)
        {
            Add(findings, path, clause, "CS-12", "use an exception filter (catch ... when) instead of a conditional throw;");
        }

        if (clause.Block.Statements.Count == 0)
        {
            Add(findings, path, clause, "CS-13", "empty catch");
        }
    }
}

static void CheckStringLoops(string path, SyntaxNode tree, List<string> findings)
{
    foreach (AssignmentExpressionSyntax assignment in tree.DescendantNodes().OfType<AssignmentExpressionSyntax>())
    {
        if (assignment.IsKind(SyntaxKind.AddAssignmentExpression) && IsStringExpression(assignment.Right) && InLoop(assignment))
        {
            Add(findings, path, assignment, "CS-18", "build strings in loops with StringBuilder");
        }
    }
}

static bool IsStringExpression(ExpressionSyntax expression)
{
    return expression switch
    {
        InterpolatedStringExpressionSyntax => true,
        LiteralExpressionSyntax literal => literal.IsKind(SyntaxKind.StringLiteralExpression),
        BinaryExpressionSyntax binary => binary.IsKind(SyntaxKind.AddExpression) && (IsStringExpression(binary.Left) || IsStringExpression(binary.Right)),
        _ => false,
    };
}

static bool InLoop(SyntaxNode node)
{
    foreach (SyntaxNode ancestor in node.Ancestors())
    {
        if (ancestor is ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax)
        {
            return true;
        }

        if (ancestor is MemberDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax)
        {
            return false;
        }
    }

    return false;
}

void CheckUnity(string path, SyntaxNode tree, List<string> findings)
{
    foreach (InvocationExpressionSyntax call in tree.DescendantNodes().OfType<InvocationExpressionSyntax>())
    {
        if (call.Expression.ToString().StartsWith("Debug.Log", StringComparison.Ordinal)
            && call.ArgumentList.Arguments.Any(argument => argument.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>().Any(access => access.Name.Identifier.ValueText is "Message")))
        {
            Add(findings, path, call, "CS-14", "log exceptions with Debug.LogException(ex, context)");
        }

        if (InvokedName(call) is "StartAssetEditing" && !StopsInFinally(call))
        {
            Add(findings, path, call, "CS-15", "StartAssetEditing needs StopAssetEditing in a finally");
        }
    }

    foreach (MethodDeclarationSyntax method in tree.DescendantNodes().OfType<MethodDeclarationSyntax>())
    {
        string name = method.Identifier.ValueText;
        bool frame = frameMethods.Contains(name);
        if (frame || redrawMethods.Contains(name))
        {
            CheckFrameBody(path, method, frame, findings);
        }
    }
}

void CheckFrameBody(string path, MethodDeclarationSyntax method, bool frame, List<string> findings)
{
    string where = method.Identifier.ValueText;
    foreach (SyntaxNode node in method.DescendantNodes())
    {
        switch (node)
        {
            case InvocationExpressionSyntax call when cachedLookups.Contains(InvokedName(call)):
                Add(findings, path, call, "CS-19", $"cache {InvokedName(call)} in a field, not in {where}");
                break;
            case InvocationExpressionSyntax call when call.Expression is MemberAccessExpressionSyntax && linqMethods.Contains(InvokedName(call)):
                Add(findings, path, call, "CS-19", $"no LINQ in {where}");
                break;
            case ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax or CollectionExpressionSyntax:
                Add(findings, path, node, "CS-19", $"no new arrays in {where}");
                break;
            case MemberAccessExpressionSyntax access when meshArrays.Contains(access.Name.Identifier.ValueText) && access.Parent is not AssignmentExpressionSyntax:
                Add(findings, path, access, "CS-19", $"{access.Name.Identifier.ValueText} copies the array on every read; read it once outside {where}");
                break;
            case InterpolatedStringExpressionSyntax when frame:
                Add(findings, path, node, "CS-19", $"no string building in {where}");
                break;
            case BinaryExpressionSyntax binary when frame && binary.IsKind(SyntaxKind.AddExpression) && IsStringExpression(binary):
                Add(findings, path, node, "CS-19", $"no string building in {where}");
                break;
            default:
                break;
        }
    }
}

static string InvokedName(InvocationExpressionSyntax call)
{
    return call.Expression switch
    {
        MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
        SimpleNameSyntax simple => simple.Identifier.ValueText,
        _ => string.Empty,
    };
}

static bool StopsInFinally(InvocationExpressionSyntax call)
{
    static bool Stops(TryStatementSyntax? attempt) =>
        attempt?.Finally?.Block.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(stop => InvokedName(stop) is "StopAssetEditing") == true;

    StatementSyntax? statement = call.FirstAncestorOrSelf<StatementSyntax>();
    if (statement?.Parent is not BlockSyntax block)
    {
        return false;
    }

    int index = block.Statements.IndexOf(statement);
    bool followedByTry = index + 1 < block.Statements.Count && Stops(block.Statements[index + 1] as TryStatementSyntax);
    return followedByTry || (block.Parent is TryStatementSyntax enclosing && enclosing.Block == block && Stops(enclosing));
}

void CheckDotNet(string path, SyntaxNode tree, bool tests, bool core, List<string> findings)
{
    foreach (MethodDeclarationSyntax method in tree.DescendantNodes().OfType<MethodDeclarationSyntax>())
    {
        bool asynchronous = LastName(method.ReturnType) is "Task" or "ValueTask";
        bool exempt = tests
            || method.Modifiers.Any(SyntaxKind.OverrideKeyword)
            || method.ExplicitInterfaceSpecifier is not null
            || method.Identifier.ValueText is "Main" or "DisposeAsync";
        bool hasToken = method.ParameterList.Parameters.Any(parameter => LastName(parameter.Type) is "CancellationToken");
        if (asynchronous && !exempt && !hasToken)
        {
            Add(findings, path, method, "CS-6", $"{method.Identifier.ValueText} takes no CancellationToken");
        }
    }

    foreach (AttributeSyntax attribute in tree.DescendantNodes().OfType<AttributeSyntax>())
    {
        if (attribute.Parent is AttributeListSyntax { Target.Identifier.ValueText: "assembly" } && LastName(attribute.Name).StartsWith("InternalsVisibleTo", StringComparison.Ordinal))
        {
            Add(findings, path, attribute, "CS-22", "declare InternalsVisibleTo as an item in the csproj");
        }
    }

    if (!core)
    {
        return;
    }

    foreach (MemberAccessExpressionSyntax access in tree.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
    {
        if (access.Expression.ToString() is "DateTime" or "DateTimeOffset" && clockMembers.Contains(access.Name.Identifier.ValueText))
        {
            Add(findings, path, access, "CS-23", "inject TimeProvider instead of reading the clock");
        }
    }
}

void CheckDotNetLayout(string root, List<string> own, List<(string Path, SyntaxNode Root)> trees, List<string> findings)
{
    foreach (string required in rootFiles.Where(file => !own.Contains(file)))
    {
        findings.Add($"{required}(1,1): CS-24 missing at the repo root");
    }

    if (own.Count(path => !path.Contains('/', StringComparison.Ordinal) && path.EndsWith(".slnx", StringComparison.Ordinal)) != 1)
    {
        findings.Add("<App>.slnx(1,1): CS-24 the repo root holds exactly one .slnx");
    }

    if (own.Contains("global.json"))
    {
        using var global = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "global.json")));
        bool pinned = global.RootElement.TryGetProperty("sdk", out JsonElement sdk)
            && sdk.TryGetProperty("version", out JsonElement version) && version.GetString()?.StartsWith("10.", StringComparison.Ordinal) == true
            && sdk.TryGetProperty("rollForward", out JsonElement roll) && roll.GetString() is "latestFeature";
        if (!pinned)
        {
            findings.Add("global.json(1,1): CS-24 pin sdk.version 10.x with rollForward latestFeature");
        }
    }

    foreach (string project in own.Where(path => path.EndsWith(".csproj", StringComparison.Ordinal)))
    {
        var xml = XDocument.Load(Path.Combine(root, project));
        CheckProject(project, xml, findings);
        CheckEntryHooks(project, xml, trees, findings);
    }
}

static string Property(XDocument xml, string name) => xml.Descendants(name).LastOrDefault()?.Value.Trim() ?? string.Empty;

static bool References(XDocument xml, string package) =>
    xml.Descendants("PackageReference").Any(reference => ((string?)reference.Attribute("Include"))?.StartsWith(package, StringComparison.OrdinalIgnoreCase) == true);

static void CheckProject(string project, XDocument xml, List<string> findings)
{
    string name = Path.GetFileNameWithoutExtension(project);
    string folder = project.Contains('/', StringComparison.Ordinal) ? project[..project.LastIndexOf('/')] : string.Empty;
    bool benchmarks = References(xml, "BenchmarkDotNet");
    string expected = name.EndsWith(".Tests", StringComparison.Ordinal) ? "tests/" : benchmarks ? "benchmarks/" : "src/";
    if (!string.Equals(folder, expected + name, StringComparison.Ordinal))
    {
        findings.Add($"{project}(1,1): CS-24 belongs at {expected}{name}/{name}.csproj");
    }

    if (name.EndsWith(".Tests", StringComparison.Ordinal) && !References(xml, "xunit"))
    {
        findings.Add($"{project}(1,1): CS-21 test projects use xUnit");
    }

    if (benchmarks && !name.EndsWith(".Benchmarks", StringComparison.Ordinal))
    {
        findings.Add($"{project}(1,1): CS-16 benchmark projects are named <App>.Benchmarks");
    }

    if (Property(xml, "LangVersion").Length > 0)
    {
        findings.Add($"{project}(1,1): CS-24 leave LangVersion unset");
    }

    string framework = Property(xml, "TargetFramework");
    bool ui = Property(xml, "UseWPF") is "true" || Property(xml, "UseWindowsForms") is "true";
    if (!framework.StartsWith("net10.0", StringComparison.Ordinal) || (name.EndsWith(".Core", StringComparison.Ordinal) && (framework is not "net10.0" || ui)))
    {
        findings.Add($"{project}(1,1): CS-24 target net10.0 (Core projects plain net10.0 with no UI)");
    }
}

static void CheckEntryHooks(string project, XDocument xml, List<(string Path, SyntaxNode Root)> trees, List<string> findings)
{
    string name = Path.GetFileNameWithoutExtension(project);
    if (Property(xml, "OutputType") is not ("Exe" or "WinExe") || name.EndsWith(".Tests", StringComparison.Ordinal) || name.EndsWith(".Benchmarks", StringComparison.Ordinal))
    {
        return;
    }

    string prefix = project.Contains('/', StringComparison.Ordinal) ? project[..(project.LastIndexOf('/') + 1)] : string.Empty;
    List<string> hooks = [.. trees.Where(tree => tree.Path.StartsWith(prefix, StringComparison.Ordinal))
        .SelectMany(tree => tree.Root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        .Where(assignment => assignment.IsKind(SyntaxKind.AddAssignmentExpression))
        .Select(assignment => assignment.Left switch
        {
            MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
            IdentifierNameSyntax name => name.Identifier.ValueText,
            _ => string.Empty,
        })];
    List<string> needed = ["UnhandledException", "UnobservedTaskException"];
    if (Property(xml, "UseWindowsForms") is "true")
    {
        needed.Add("ThreadException");
    }

    if (Property(xml, "UseWPF") is "true")
    {
        needed.Add("DispatcherUnhandledException");
    }

    foreach (string hook in needed.Where(hook => !hooks.Contains(hook)))
    {
        findings.Add($"{project}(1,1): CS-13 the entry point subscribes to {hook}");
    }
}

void CheckUnityLayout(string root, List<string> own, List<string> packageRoots, List<string> findings)
{
    bool hasCore = false;
    foreach (string asmdef in own.Where(path => path.EndsWith(".asmdef", StringComparison.Ordinal)))
    {
        string? package = PackageRoot(asmdef, packageRoots);
        string relative = package is null ? asmdef : asmdef[package.Length..];
        string folder = relative.Contains('/', StringComparison.Ordinal) ? relative[..relative.LastIndexOf('/')] : string.Empty;
        hasCore |= folder is "Core";
        if (package is null || !PackageName(root, package).StartsWith("dev.whyknot.", StringComparison.Ordinal) || !unityFolders.Contains(folder))
        {
            findings.Add($"{asmdef}(1,1): CS-25 asmdefs sit in Core, Runtime, Editor or Tests/Editor of a dev.whyknot.<name> package");
        }

        using var definition = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, asmdef)));
        JsonElement json = definition.RootElement;
        if (!(json.TryGetProperty("rootNamespace", out JsonElement space) && space.GetString()?.StartsWith("WhyKnot.", StringComparison.Ordinal) == true))
        {
            findings.Add($"{asmdef}(1,1): CS-25 rootNamespace is WhyKnot.<Name>");
        }

        bool editorOnly = json.TryGetProperty("includePlatforms", out JsonElement platforms) && platforms.ValueKind == JsonValueKind.Array
            && platforms.GetArrayLength() == 1 && platforms[0].GetString() is "Editor";
        if (folder is "Editor" or "Tests/Editor" && !editorOnly)
        {
            findings.Add($"{asmdef}(1,1): CS-25 includePlatforms is [\"Editor\"]");
        }

        if (folder is "Core" && !(json.TryGetProperty("noEngineReferences", out JsonElement engine) && engine.ValueKind == JsonValueKind.True))
        {
            findings.Add($"{asmdef}(1,1): CS-25 the Core asmdef sets noEngineReferences");
        }
    }

    bool headlessTests = own.Exists(path => path.StartsWith("headless/", StringComparison.Ordinal) && path.EndsWith(".Tests.csproj", StringComparison.Ordinal)
        && File.ReadAllText(Path.Combine(root, path)).Contains("\"NUnit\"", StringComparison.Ordinal));
    if (hasCore && !headlessTests)
    {
        findings.Add("headless(1,1): CS-21 the Core assembly is tested headless with NUnit in headless/<App>.Tests");
    }
}
