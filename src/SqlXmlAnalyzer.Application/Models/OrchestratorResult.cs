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
    );
}
