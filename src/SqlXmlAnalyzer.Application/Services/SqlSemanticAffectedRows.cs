namespace SqlXmlAnalyzer.Application.Services;

/// <summary>Combines SqlDataReader counts without treating the -1 sentinel as negative DML.</summary>
public static class SqlSemanticAffectedRows
{
    public static int Combine(int accumulated, int batch)
    {
        if (accumulated < -1 || batch < -1)
            throw new InvalidDataException("受影响行数无效，不能作为语义验证证据。");
        if (batch == -1) return accumulated;
        try { return checked(Math.Max(accumulated, 0) + batch); }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("累计受影响行数超过 Int32 验证范围，不能使用溢出或截断计数。", exception);
        }
    }
}
