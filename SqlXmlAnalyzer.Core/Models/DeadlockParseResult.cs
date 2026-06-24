using System;
using System.Collections.Generic;

namespace SqlXmlAnalyzer
{
    public sealed record DeadlockParseResult<T>(
        T? Value,
        IReadOnlyList<string> Errors,
        IReadOnlyList<string> Warnings)
    {
        public bool IsSuccess => Value is not null && Errors.Count == 0;

        public static DeadlockParseResult<T> Success(
            T value,
            IReadOnlyList<string>? warnings = null)
        {
            return new DeadlockParseResult<T>(
                value,
                Array.Empty<string>(),
                warnings ?? Array.Empty<string>());
        }

        public static DeadlockParseResult<T> Failure(params string[] errors)
        {
            return new DeadlockParseResult<T>(
                default,
                errors,
                Array.Empty<string>());
        }
    }

    public sealed record ParsedDeadlockGraphData(
        List<DeadlockProcess> Processes,
        List<LockResource> Resources,
        string VictimId);
}
