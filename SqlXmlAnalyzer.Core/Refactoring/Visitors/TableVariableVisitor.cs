using System;
using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlXmlAnalyzer.Core.Refactoring.Visitors
{
    public class TableVariableVisitor : TSqlFragmentVisitor
    {
        public override void ExplicitVisit(TSqlBatch node)
        {
            var tableVars = new List<string>();
            var statementsToReplace = new Dictionary<TSqlStatement, TSqlStatement>();

            // 1. Collect all table variable declarations recursively
            var collector = new TableVariableDeclarationCollector();
            node.Accept(collector);

            foreach (var declareStmt in collector.Declarations)
            {
                var varName = declareStmt.Body.VariableName.Value; // e.g. "@MyTable"
                tableVars.Add(varName);

                // Construct CREATE TABLE statement
                var createStmt = new CreateTableStatement();
                var tempTableName = varName.Replace("@", "#");

                var name = new SchemaObjectName();
                name.Identifiers.Add(new Identifier { Value = tempTableName });
                createStmt.SchemaObjectName = name;
                createStmt.Definition = declareStmt.Body.Definition;

                statementsToReplace[declareStmt] = createStmt;
            }

            if (tableVars.Count > 0)
            {
                // 2. Recursively replace DECLARE TABLE statements with CREATE TABLE statements
                ReplaceStatements(node, statementsToReplace);

                // 3. Rename variable references in the batch statements
                var renameVisitor = new VariableRenameVisitor(tableVars);
                node.Accept(renameVisitor);

                // 4. Append safe conditional DROP TABLE statements at the end of the batch
                // Only for table variables that are NOT declared inside stored procedures/functions/views/triggers
                foreach (var declareStmt in collector.Declarations)
                {
                    if (collector.InsideSchemaObjects.Contains(declareStmt))
                    {
                        continue;
                    }

                    var varName = declareStmt.Body.VariableName.Value;
                    var tempTableName = varName.Replace("@", "#");

                    var dropStmt = new DropTableStatement();
                    var name = new SchemaObjectName();
                    name.Identifiers.Add(new Identifier { Value = tempTableName });
                    dropStmt.Objects.Add(name);

                    // Function: OBJECT_ID('tempdb..#TableName')
                    var funcCall = new FunctionCall();
                    funcCall.FunctionName = new Identifier { Value = "OBJECT_ID" };
                    funcCall.Parameters.Add(new StringLiteral { Value = $"tempdb..{tempTableName}" });

                    // Predicate: OBJECT_ID(...) IS NOT NULL
                    var predicate = new BooleanIsNullExpression();
                    predicate.Expression = funcCall;
                    predicate.IsNot = true; // meaning "IS NOT NULL"

                    // IF ... DROP TABLE ...
                    var ifStmt = new IfStatement();
                    ifStmt.Predicate = predicate;
                    ifStmt.ThenStatement = dropStmt;

                    node.Statements.Add(ifStmt);
                }
            }

            base.ExplicitVisit(node);
        }

        private void ReplaceStatements(TSqlFragment? node, Dictionary<TSqlStatement, TSqlStatement> replacementMap)
        {
            if (node == null) return;

            var props = node.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            foreach (var prop in props)
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;

                // 1. Single TSqlStatement property
                if (typeof(TSqlStatement).IsAssignableFrom(prop.PropertyType))
                {
                    if (prop.CanWrite && prop.GetValue(node) is TSqlStatement stmt && replacementMap.TryGetValue(stmt, out var replacement))
                    {
                        prop.SetValue(node, replacement);
                    }
                    else if (prop.GetValue(node) is TSqlFragment child)
                    {
                        ReplaceStatements(child, replacementMap);
                    }
                }
                // 2. IList<TSqlStatement> property
                else if (typeof(IList<TSqlStatement>).IsAssignableFrom(prop.PropertyType))
                {
                    if (prop.GetValue(node) is IList<TSqlStatement> list)
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            if (list[i] != null && replacementMap.TryGetValue(list[i], out var replacement))
                            {
                                list[i] = replacement;
                            }
                            else
                            {
                                ReplaceStatements(list[i], replacementMap);
                            }
                        }
                    }
                }
                // 3. StatementList property
                else if (typeof(StatementList).IsAssignableFrom(prop.PropertyType))
                {
                    if (prop.GetValue(node) is StatementList stmtList)
                    {
                        ReplaceStatements(stmtList, replacementMap);
                    }
                }
                // 4. Any other TSqlFragment property to recurse
                else if (typeof(TSqlFragment).IsAssignableFrom(prop.PropertyType))
                {
                    if (prop.GetValue(node) is TSqlFragment child)
                    {
                        ReplaceStatements(child, replacementMap);
                    }
                }
                // 5. Collections of TSqlFragment
                else if (typeof(System.Collections.IEnumerable).IsAssignableFrom(prop.PropertyType) && prop.PropertyType != typeof(string))
                {
                    if (prop.GetValue(node) is System.Collections.IEnumerable enumerable)
                    {
                        foreach (var item in enumerable)
                        {
                            if (item is TSqlFragment child)
                            {
                                ReplaceStatements(child, replacementMap);
                            }
                        }
                    }
                }
            }
        }
    }

    internal class TableVariableDeclarationCollector : TSqlFragmentVisitor
    {
        public List<DeclareTableVariableStatement> Declarations { get; } = new List<DeclareTableVariableStatement>();
        public HashSet<DeclareTableVariableStatement> InsideSchemaObjects { get; } = new HashSet<DeclareTableVariableStatement>();

        private bool _inSchemaObject = false;

        public override void Visit(TSqlFragment node)
        {
            bool wasInSchemaObject = _inSchemaObject;
            if (node is CreateProcedureStatement || node is AlterProcedureStatement ||
                node is CreateFunctionStatement || node is AlterFunctionStatement ||
                node is CreateTriggerStatement || node is AlterTriggerStatement ||
                node is CreateViewStatement || node is AlterViewStatement)
            {
                _inSchemaObject = true;
            }

            base.Visit(node);

            _inSchemaObject = wasInSchemaObject;
        }

        public override void ExplicitVisit(DeclareTableVariableStatement node)
        {
            Declarations.Add(node);
            if (_inSchemaObject)
            {
                InsideSchemaObjects.Add(node);
            }
            base.ExplicitVisit(node);
        }
    }

    internal class VariableRenameVisitor : TSqlFragmentVisitor
    {
        private readonly HashSet<string> _targetVariables;

        public VariableRenameVisitor(IEnumerable<string> targetVariables)
        {
            _targetVariables = new HashSet<string>(targetVariables, StringComparer.OrdinalIgnoreCase);
        }

        public override void ExplicitVisit(VariableReference node)
        {
            if (_targetVariables.Contains(node.Name))
            {
                node.Name = node.Name.Replace("@", "#");
            }
            base.ExplicitVisit(node);
        }
    }
}
