using System;
using System.Linq;
using System.Text;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed class DeadlockSelectionDetailService
    {
        public string BuildProcessDetail(DeadlockProcess process)
        {
            ArgumentNullException.ThrowIfNull(process);

            var builder = new StringBuilder();
            builder.AppendLine($"🔴 选中进程 (SPID {process.Spid}) 详情：");
            builder.AppendLine("----------------------------------------");
            builder.AppendLine($"标识 ID: {process.Id}");
            builder.AppendLine(
                $"当前状态: {process.Status} | 隔离级别: {process.Isolationlevel}");
            builder.AppendLine(
                $"事务名称: {(!string.IsNullOrEmpty(process.TransactionName) ? process.TransactionName : "无")}");
            builder.AppendLine(
                $"运行数据库: {(!string.IsNullOrEmpty(process.CurrentDbName) ? process.CurrentDbName : "Unknown")}");
            builder.AppendLine(
                $"登录账号: {process.Loginname} | 客户端主机: {process.Hostname}");

            if (!string.IsNullOrEmpty(process.ClientApp))
            {
                builder.AppendLine($"应用程序: {process.ClientApp}");
            }

            if (!string.IsNullOrEmpty(process.WaitResource))
            {
                builder.AppendLine($"等待资源: {process.WaitResource}");
            }

            if (!string.IsNullOrEmpty(process.WaitTime))
            {
                builder.AppendLine($"等待时间: {process.WaitTime} ms");
            }

            builder.AppendLine();
            builder.AppendLine("📝 正在执行的 SQL 语句 (inputbuf):");
            builder.AppendLine(process.Inputbuf);

            if (process.ExecutionStack.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("🥞 执行堆栈 (Execution Stack):");
                foreach (ExecutionFrame frame in process.ExecutionStack)
                {
                    builder.AppendLine($"  • 过程: {frame.Procname} | 行号: {frame.Line}");
                    if (!string.IsNullOrEmpty(frame.Statement))
                    {
                        builder.AppendLine($"    SQL: {frame.Statement}");
                    }
                }
            }

            AppendSargWarnings(builder, process);
            return builder.ToString();
        }

        public string BuildResourceDetail(LockResource resource)
        {
            ArgumentNullException.ThrowIfNull(resource);

            var builder = new StringBuilder();
            builder.AppendLine($"🔑 涉及资源 ({resource.LockType.ToUpperInvariant()}) 详情：");
            builder.AppendLine("----------------------------------------");
            builder.AppendLine($"数据库 ID (DBID): {resource.Dbid}");
            builder.AppendLine($"对象名称: {resource.ObjectName}");

            if (!string.IsNullOrEmpty(resource.IndexName))
            {
                builder.AppendLine($"关联索引: {resource.IndexName}");
            }

            builder.AppendLine($"HOBT ID: {resource.Hobtid}");
            builder.AppendLine();
            builder.AppendLine("✅ 持有该资源的进程 (Owners):");
            foreach (LockOwner owner in resource.Owners)
            {
                builder.AppendLine($"  • 标识 ID: {owner.Id}   模式 (Mode): {owner.Mode}");
            }

            builder.AppendLine();
            builder.AppendLine("⏳ 等待该资源的进程 (Waiters):");
            foreach (LockWaiter waiter in resource.Waiters)
            {
                builder.AppendLine(
                    $"  • 标识 ID: {waiter.Id}   请求模式 (Mode): {waiter.Mode}  类型: {waiter.RequestType}");
            }

            return builder.ToString();
        }

        public string BuildPatternDetail(DeadlockPattern pattern)
        {
            ArgumentNullException.ThrowIfNull(pattern);

            return
                $"类型: {pattern.TypeName}\n\n" +
                $"描述: {pattern.Description}\n\n" +
                $"可能原因: {pattern.LikelyCause}\n\n" +
                $"推荐措施: {pattern.Recommendation}";
        }

        private static void AppendSargWarnings(
            StringBuilder builder,
            DeadlockProcess process)
        {
            var warnings = SargAnalyzer.Analyze(process.Inputbuf);
            if (process.ExecutionStack.Count > 0)
            {
                foreach (ExecutionFrame frame in process.ExecutionStack)
                {
                    if (!string.IsNullOrEmpty(frame.Statement))
                    {
                        warnings.AddRange(SargAnalyzer.Analyze(frame.Statement));
                    }
                }
            }

            warnings = warnings
                .GroupBy(warning => warning.Title)
                .Select(group => group.First())
                .ToList();

            builder.AppendLine();
            if (warnings.Count > 0)
            {
                builder.AppendLine("⚡ SQL 语句性能与 SARG 扫描预警（DEADLOCK.py 专家级建议）：");
                builder.AppendLine("========================================================================");
                foreach (SargWarning warning in warnings)
                {
                    builder.AppendLine($"【问题标题】 {warning.Title}");
                    builder.AppendLine($"【物理成因】 {warning.Desc}");
                    builder.AppendLine($"【解决方案】 {warning.Solution}");
                    builder.AppendLine("------------------------------------------------------------------------");
                }
            }
            else
            {
                builder.AppendLine(
                    "💚 SQL 扫描通过：未检测到明显的前导模糊、函数致盲或负向查询等 SARG 索引致盲缺陷。");
            }
        }
    }
}
