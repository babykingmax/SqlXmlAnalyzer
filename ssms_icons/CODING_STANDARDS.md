# SqlXmlAnalyzer - 编码规范与最佳实践

本文档为项目建立编码标准和最佳实践指南。

---

## 📐 命名约定

### ✅ 类和接口
```csharp
// ✅ 好
public class DeadlockAnalysisService { }
public record DeadlockProcess( ) { }
public interface IDeadlockRepository { }

// ❌ 避免
public class deadlock_analysis_service { }
public class DAS { }
public class Service1 { }
```

### ✅ 方法和属性
```csharp
// ✅ 好
public async Task<List<DeadlockProcess>> LoadProcessesAsync(string filePath) { }
public int EstimatedRowCount { get; set; }
public void ValidateInput(string input) { }

// ❌ 避免
public async Task<List<DeadlockProcess>> load_processes_async(string filePath) { }
public int ERC { get; set; }
public void Validate() { }  // 太模糊
```

### ✅ 常数和字段
```csharp
// ✅ 好
private const int MAX_FILE_SIZE = 100_000_000;  // 100 MB
private const string QUERY_TIMEOUT = "30s";
private readonly Dictionary<string, object> _cache = new();

// ❌ 避免
private const int maxFileSize = 100000000;
private const string query_timeout = "30s";
private Dictionary<string, object> cache = new();  // 应该 readonly
```

### ✅ 布尔属性
```csharp
// ✅ 好
public bool IsValidated { get; set; }
public bool CanExecute { get; private set; }
public bool HasErrors { get; }

// ❌ 避免
public bool Valid { get; set; }  // 不清楚
public bool Execute { get; }  // 名词而非形容词
```

---

## 🏗️ 架构模式

### 推荐模式 1: 服务类 + 模型分离

```csharp
// ✅ 推荐
namespace SqlXmlAnalyzer.Services
{
	public class DeadlockAnalysisService {
		public DeadlockAnalysisResult Analyze(string filePath) { }
	}
}

namespace SqlXmlAnalyzer.Models
{
	public sealed record DeadlockAnalysisResult {
		public List<DeadlockProcess> Processes { get; init; }
		public DeadlockGraph Graph { get; init; }
	}
}

// 在 UI 中使用
private void AnalyzeFile(string path) {
	var service = new DeadlockAnalysisService();
	var result = service.Analyze(path);
	UpdateUI(result);
}

// ❌ 避免 - 一锅乱炖
private void AnalyzeFile(string path) {
	// 数据加载、解析、分析、UI 更新全部混在一起
}
```

### 推荐模式 2: 依赖注入

```csharp
// ✅ 推荐 - 在 App.xaml.cs 中
public partial class App : Application {
	private readonly IServiceProvider _serviceProvider;

	protected override void OnStartup(StartupEventArgs e) {
		var services = new ServiceCollection();

		services.AddSingleton<ILogger>(Logger.Instance);
		services.AddSingleton<IXmlParser, XmlParser>();
		services.AddSingleton<IDeadlockAnalysisService, DeadlockAnalysisService>();
		services.AddSingleton<MainWindow>();

		_serviceProvider = services.BuildServiceProvider();

		var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
		mainWindow.Show();
	}
}

// 在 MainWindow 中使用
public partial class MainWindow : Window {
	private readonly IDeadlockAnalysisService _analysisService;

	public MainWindow(IDeadlockAnalysisService analysisService) {
		_analysisService = analysisService;
		InitializeComponent();
	}
}
```

### 推荐模式 3: 策略模式用于不同的分析类型

```csharp
// ✅ 推荐
public interface IAnalysisStrategy {
	AnalysisResult Analyze(string filePath);
	bool CanHandle(string fileExtension);
}

public class DeadlockAnalysisStrategy : IAnalysisStrategy {
	public bool CanHandle(string fileExtension) => fileExtension.Equals(".xml", StringComparison.OrdinalIgnoreCase);
	public AnalysisResult Analyze(string filePath) { /* ... */ }
}

public class ExecutionPlanAnalysisStrategy : IAnalysisStrategy {
	public bool CanHandle(string fileExtension) => fileExtension.Equals(".sqlplan", StringComparison.OrdinalIgnoreCase);
	public AnalysisResult Analyze(string filePath) { /* ... */ }
}

// 使用
private AnalysisResult AnalyzeFile(string filePath) {
	var ext = Path.GetExtension(filePath);
	var strategy = _strategies.FirstOrDefault(s => s.CanHandle(ext));
	if (strategy == null) throw new NotSupportedException($"不支持的文件类型: {ext}");
	return strategy.Analyze(filePath);
}
```

---

## 📝 注释和文档

### ✅ 有用的注释

```csharp
/// <summary>
/// 从 XML 文件解析死锁进程列表
/// </summary>
/// <param name="filePath">XML 文件的完整路径</param>
/// <returns>死锁进程集合，如果文件无效则返回空列表</returns>
/// <exception cref="FileNotFoundException">文件不存在时抛出</exception>
/// <exception cref="XmlException">XML 格式无效时抛出</exception>
public List<DeadlockProcess> ParseProcesses(string filePath) {
	if (!File.Exists(filePath))
		throw new FileNotFoundException($"文件不存在: {filePath}");

	var doc = XDocument.Load(filePath);
	// 接下来的逻辑...
}

// ✅ 解释复杂的业务逻辑
private List<WaitForEdge> BuildWaitForGraph(List<DeadlockProcess> processes, List<LockResource> resources) {
	// 等待图的构建基于以下原理：
	// 1. 进程 A 等待进程 B 持有的资源 → 添加边 A → B
	// 2. 通过检测环路来识别死锁循环
	// 3. 最短环路通常表示最严重的死锁

	var edges = new List<WaitForEdge>();
	// ... 实现
	return edges;
}

// ✅ 警告和已知问题
/// <summary>
/// 加载大型 XML 文件时可能非常缓慢（>500MB）
/// 考虑在后台线程中调用此方法以避免 UI 冻结
/// </summary>
public XDocument LoadXmlFile(string filePath) { }

// ✅ 标记性能关键代码
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private static double ParseCost(string costString) {
	// 性能关键路径 - 每次数据解析都会调用
}
```

### ❌ 避免的注释

```csharp
// ❌ 显而易见的注释
i++; // 增加 i

// ❌ 过时的注释
void OldMethod() {
	// 这是在 v1.0 中添加的
	// TODO: 在 v3.0 中删除
}

// ❌ 位置标记（使用代码导航而不是注释）
// ===== 加载数据 =====
// ===== 处理数据 =====
// ===== 显示数据 =====

// ❌ 注释掉的代码（使用版本控制）
// var x = old_logic();
// var y = another_old_line();
```

---

## 🔒 异常处理最佳实践

### ✅ 好的异常处理

```csharp
// ✅ 1. 捕获特定异常
try {
	var doc = XDocument.Load(filePath);
} catch (FileNotFoundException ex) {
	Logger.Warning($"文件未找到: {filePath}");
	throw new DeadlockAnalysisException($"无法加载文件: {filePath}", ex);
} catch (XmlException ex) {
	Logger.Error($"XML 格式错误: {ex.Message}");
	throw new DeadlockAnalysisException("文件格式无效", ex);
} catch (UnauthorizedAccessException ex) {
	Logger.Warning($"权限不足: {filePath}");
	throw new DeadlockAnalysisException("无权限访问文件", ex);
}

// ✅ 2. 提供自定义异常
public class DeadlockAnalysisException : Exception {
	public DeadlockAnalysisException(string message) : base(message) { }
	public DeadlockAnalysisException(string message, Exception inner) 
		: base(message, inner) { }
}

// ✅ 3. 使用 using 块确保资源释放
using (var stream = File.OpenRead(filePath)) {
	var doc = XDocument.Load(stream);
	return doc;
}

// ✅ 4. 保留堆栈跟踪
catch (Exception ex) {
	Logger.Error($"执行失败: {ex.Message}\n堆栈: {ex.StackTrace}");
	throw; // 保留堆栈信息
}

// ✅ 5. 异步异常处理
try {
	var result = await AnalyzeAsync(filePath);
} catch (OperationCanceledException) {
	Logger.Info("操作被用户取消");
} catch (AggregateException ae) {
	foreach (var ex in ae.InnerExceptions) {
		Logger.Error($"并发错误: {ex.Message}");
	}
}
```

### ❌ 避免的异常处理

```csharp
// ❌ 1. 吞掉异常
try {
	Process();
} catch { }  // 隐藏所有错误！

// ❌ 2. 通用异常捕获
try {
	LoadFile(path);
} catch (Exception ex) {
	MessageBox.Show("出错了");
	// 用户无法了解具体是什么错误
}

// ❌ 3. 异常链中丢失原始信息
catch (Exception ex) {
	throw new Exception("出错");  // 丢失了 ex 信息
}

// ❌ 4. 在异常处理中重新抛出
try { } catch (Exception ex) {
	throw ex;  // 重置堆栈信息，改用 throw 保留
}

// ❌ 5. 忽视异步异常
var task = AnalyzeAsync(path);  // 异常在这里被吞掉
```

---

## 🎯 性能最佳实践

### ✅ 性能优化建议

```csharp
// ✅ 1. 使用 StringBuilding 而不是字符串连接
var sb = new StringBuilder();
for (int i = 0; i < 1000000; i++) {
	sb.Append("item");  // O(n)
}
// 而不是
string result = "";
for (int i = 0; i < 1000000; i++) {
	result += "item";  // O(n²)
}

// ✅ 2. LINQ 延迟执行 - 合理利用
IEnumerable<Process> GetExpensiveProcesses(IEnumerable<DeadlockProcess> processes) {
	return processes
		.Where(p => p.Status == "blocked")  // 延迟执行
		.OrderBy(p => p.Spid)
		.Take(10);  // 只获取前 10 个
}

// ✅ 3. 缓存重复计算
private Dictionary<string, DeadlockPattern> _patternCache = new();

public DeadlockPattern GetPattern(string key) {
	if (_patternCache.TryGetValue(key, out var pattern))
		return pattern;

	pattern = AnalyzePattern(key);
	_patternCache[key] = pattern;
	return pattern;
}

// ✅ 4. 使用 ValueTask 处理轻型异步操作
public ValueTask<int> GetCountAsync() {
	if (_cachedCount.HasValue)
		return new ValueTask<int>(_cachedCount.Value);

	return new ValueTask<int>(ComputeCountAsync());
}

// ✅ 5. 避免装箱
List<int> numbers = new();
foreach (var i in Enumerable.Range(0, 100)) {
	numbers.Add(i);  // 避免自动装箱
}
```

### ❌ 常见性能错误

```csharp
// ❌ 1. 在循环中创建对象
for (int i = 0; i < 1000; i++) {
	var pattern = new Regex(/* ... */);  // 每次都重新编译！
}

// ❌ 2. 过度枚举 LINQ
var items = GetItems().ToList();
var filtered = items.Where(/* ... */);
var sorted = filtered.OrderBy(/* ... */);
foreach (var item in sorted) { /* ... */ }
// 应该链接 Where 和 OrderBy，最后再枚举

// ❌ 3. 同步等待异步操作
var result = AnalyzeAsync(path).Result;  // 可能死锁 + 性能差

// ❌ 4. 文字 XPath 查询
foreach (var element in doc.Descendants("process")) {
	// 每次都解析 XPath
}

// ❌ 5. 在热路径中使用反射
var method = type.GetMethod("Process");
for (int i = 0; i < 1000000; i++) {
	method.Invoke(obj, parameters);
}
```

---

## ✅ 代码审查检查清单

在提交代码前，请逐项检查：

### 功能性检查
- [ ] 代码实现符合需求规范
- [ ] 所有边界条件都已处理
- [ ] 错误情况已充分测试
- [ ] 性能满足预期（>1MB 文件也要快速响应）

### 可读性检查
- [ ] 变量名清晰且有意义
- [ ] 方法名动词+名词（如 `AnalyzeDeadlock`）
- [ ] 代码不超过 100 行/方法（特殊情况可到 150）
- [ ] 复杂逻辑有注释说明

### 健壮性检查
- [ ] 所有输入都已验证
- [ ] 异常使用特定类型捕获
- [ ] 资源使用 using 块或 Dispose
- [ ] 没有空引用异常风险

### 性能检查
- [ ] 没有 O(n²) 循环
- [ ] 字符串连接使用 StringBuilder
- [ ] 正则表达式已预编译
- [ ] 没有不必要的对象创建

### 安全性检查
- [ ] 用户输入已验证
- [ ] HTML 内容已转义
- [ ] 文件路径已检查
- [ ] 没有硬编码的敏感信息

### 测试检查
- [ ] 新功能包含单元测试
- [ ] 核心路径已集成测试
- [ ] 边界条件已测试
- [ ] 代码覆盖率 >80%

---

## 📚 推荐阅读

1. **Code Complete by Steve McConnell** - 代码完整性
2. **Clean Code by Robert Martin** - 代码整洁之道
3. **Effective C# by Bill Wagner** - C# 高效实践
4. **Async in C# by Alex Davies** - 异步编程

---

## 🔄 代码审查流程

### 提交前 (Developer)
1. 自审 - 检查清单
2. 本地构建 - 无错误无警告
3. 本地测试 - 所有测试通过
4. 代码格式化 - 使用 Roslyn 格式化

### 审查中 (Reviewer)
1. 阅读意图 - 理解变更目的
2. 检查实现 - 代码是否符合规范
3. 测试覆盖 - 是否添加了测试
4. 性能审查 - 是否有性能隐患

### 审查后 (Merge)
1. 至少 2 个审查者同意
2. 所有反馈已解决
3. CI/CD 管道通过
4. 合并到主分支

---

## 🎓 持续学习

- 每周阅读一篇 C# 最佳实践文章
- 月度代码审查总结
- 季度架构讨论会
- 年度性能优化评估

---

**最后更新**: 2026.06.01  
**维护者**: 开发团队  
**适用范围**: 所有新代码 + 主要重构
