using System.Text.RegularExpressions;

// 简单未使用类型扫描器：统计每个 public class/interface/record 在其它文件中被引用次数
// 仅做启发式，忽略命名空间冲突与同名类型的精确匹配问题

var root = args.Length > 0 ? args[0] : Path.GetFullPath(Path.Combine(".."));
if (!Directory.Exists(root))
{
    Console.WriteLine($"Root path not found: {root}");
    return;
}

string[] includeProjects = new[]{"CryptoSpot.API","CryptoSpot.Application","CryptoSpot.Bus","CryptoSpot.Domain","CryptoSpot.Infrastructure","CryptoSpot.Persistence","CryptoSpot.Redis"};
var sourceFiles = includeProjects
    .SelectMany(p => Directory.Exists(Path.Combine(root, p))
        ? Directory.GetFiles(Path.Combine(root, p), "*.cs", SearchOption.AllDirectories)
        : Array.Empty<string>())
    .Where(f => !f.EndsWith("AssemblyInfo.cs"))
    .ToList();

var typePattern = new Regex(@"\b(public|internal)\s+(static\s+)?(class|interface|record|struct)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
var types = new Dictionary<string,string>(); // typeName -> filePath (first occurrence)
foreach (var file in sourceFiles)
{
    var text = File.ReadAllText(file);
    foreach (Match m in typePattern.Matches(text))
    {
        var name = m.Groups["name"].Value;
        if (!types.ContainsKey(name))
            types[name] = file;
    }
}

// 引用计数（简单字符串匹配）
var results = new List<(string Type, string File, int References)>();
foreach (var kv in types)
{
    int count = 0;
    foreach (var file in sourceFiles)
    {
        if (file == kv.Value) continue; // 自身文件不计
        var content = File.ReadAllText(file);
        // 粗匹配：避免把子串匹配进来，用单词边界
        count += Regex.Matches(content, $"\\b{Regex.Escape(kv.Key)}\\b").Count;
    }
    results.Add((kv.Key, kv.Value, count));
}

// 阈值：引用次数 == 0 视为候选
var unused = results.Where(r => r.References == 0)
    .OrderBy(r => r.Type)
    .ToList();

Console.WriteLine("=== 未使用类型候选 (引用次数=0) ===");
foreach (var u in unused)
{
    Console.WriteLine($"{u.Type,-30} {Path.GetRelativePath(root, u.File)}");
}
Console.WriteLine($"总计: {unused.Count} 条");

Console.WriteLine();
Console.WriteLine("提示: 可能存在反射/DI/泛型约束/局部类等未捕获的使用, 需人工复核.");
