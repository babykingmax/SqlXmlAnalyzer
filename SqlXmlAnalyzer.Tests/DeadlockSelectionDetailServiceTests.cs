using System;
using System.Collections.Generic;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class DeadlockSelectionDetailServiceTests
    {
        [Fact]
        public void BuildProcessDetail_IncludesProcessStackAndSargWarnings()
        {
            var service = new DeadlockSelectionDetailService();
            var process = new DeadlockProcess(
                "process1",
                "57",
                "app_user",
                "WEB01",
                "read committed",
                "suspended",
                "SELECT * FROM Orders WHERE LEFT(CustomerCode, 2) = 'AA'",
                new List<ExecutionFrame>
                {
                    new("dbo.GetOrders", "42", "SELECT * FROM Orders WHERE Name LIKE '%abc'")
                },
                TransactionName: "user_transaction",
                CurrentDbName: "Sales",
                ClientApp: "SqlXmlAnalyzer",
                WaitResource: "KEY: 5:720575940",
                WaitTime: "1000");

            string detail = service.BuildProcessDetail(process);

            detail.Should().Contain("选中进程 (SPID 57)");
            detail.Should().Contain("事务名称: user_transaction");
            detail.Should().Contain("运行数据库: Sales");
            detail.Should().Contain("应用程序: SqlXmlAnalyzer");
            detail.Should().Contain("等待时间: 1000 ms");
            detail.Should().Contain("过程: dbo.GetOrders | 行号: 42");
            detail.Should().Contain("SQL 语句性能与 SARG 扫描预警");
            detail.Should().Contain("【问题标题】");
        }

        [Fact]
        public void BuildProcessDetail_WhenNoSargWarnings_AddsSuccessMessage()
        {
            var service = new DeadlockSelectionDetailService();
            var process = new DeadlockProcess(
                "process2",
                "58",
                "app_user",
                "WEB02",
                "read committed",
                "running",
                "SELECT Id FROM Orders WHERE Id = @Id",
                new List<ExecutionFrame>());

            string detail = service.BuildProcessDetail(process);

            detail.Should().Contain("SQL 扫描通过");
        }

        [Fact]
        public void BuildResourceDetail_IncludesOwnersAndWaiters()
        {
            var service = new DeadlockSelectionDetailService();
            var resource = new LockResource(
                "keylock",
                "Sales.dbo.Orders",
                "IX_Orders_Customer",
                "720575940",
                "5",
                new List<LockOwner> { new("process1", "X") },
                new List<LockWaiter> { new("process2", "S", "wait") });

            string detail = service.BuildResourceDetail(resource);

            detail.Should().Contain("涉及资源 (KEYLOCK)");
            detail.Should().Contain("对象名称: Sales.dbo.Orders");
            detail.Should().Contain("关联索引: IX_Orders_Customer");
            detail.Should().Contain("标识 ID: process1   模式 (Mode): X");
            detail.Should().Contain("标识 ID: process2   请求模式 (Mode): S  类型: wait");
        }

        [Fact]
        public void BuildPatternDetail_ReturnsPatternSummary()
        {
            var service = new DeadlockSelectionDetailService();
            var pattern = new DeadlockPattern(
                "Key Lookup Deadlock",
                "High",
                "Description",
                "Cause",
                "Recommendation");

            string detail = service.BuildPatternDetail(pattern);

            detail.Should().Be(
                "类型: Key Lookup Deadlock\n\n" +
                "描述: Description\n\n" +
                "可能原因: Cause\n\n" +
                "推荐措施: Recommendation");
        }

        [Fact]
        public void BuildProcessDetail_WhenProcessIsNull_Throws()
        {
            var service = new DeadlockSelectionDetailService();

            Action act = () => service.BuildProcessDetail(null!);

            act.Should().Throw<ArgumentNullException>();
        }
    }
}
