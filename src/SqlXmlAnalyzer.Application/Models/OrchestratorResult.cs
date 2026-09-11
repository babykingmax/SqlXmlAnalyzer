using System;
using System.Collections.Generic;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Application.Models
{
    public record OrchestratorResult(
        RefactorResult? Result,
        bool IsSuccess,
        string? ErrorMessage,
        Exception? ErrorException,
        IReadOnlyList<string> Warnings
    )
    {
        public SqlWritebackResult? Writeback { get; init; }
        public Core.Rules.PlanDiagnosticReport? Diagnostics { get; init; }
    }
}
