using System;
using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlXmlAnalyzer.Core.Refactoring.Visitors
{
    public class TableVariableVisitor : TSqlFragmentVisitor
    {
        private readonly RefactorContext _context;

        public TableVariableVisitor(RefactorContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public override void ExplicitVisit(TSqlBatch node)
        {
            var tableVars = new List<string>();
            var renameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
                renameMap[varName] = tempTableName;
            }

            if (tableVars.Count > 0)
            {
                _context.Changed = true;
                // 2. Recursively replace DECLARE TABLE statements with CREATE TABLE statements
                ReplaceStatements(node, statementsToReplace);

                // 3. Rename variable references in the batch statements
                var renameVisitor = new VariableRenameVisitor(renameMap);
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

                var val = prop.GetValue(node);
                if (val == null) continue;

                // 1. Single TSqlStatement property
                if (val is TSqlStatement stmt)
                {
                    if (replacementMap.TryGetValue(stmt, out var replacement))
                    {
                        if (prop.CanWrite)
                        {
                            prop.SetValue(node, replacement);
                        }
                    }
                    else
                    {
                        ReplaceStatements(stmt, replacementMap);
                    }
                }
                // 2. Collections of statements/fragments
                else if (val is System.Collections.IList list)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i] is TSqlStatement listStmt && replacementMap.TryGetValue(listStmt, out var listReplacement))
                        {
                            list[i] = listReplacement;
                        }
                        else if (list[i] is TSqlFragment child)
                        {
                            ReplaceStatements(child, replacementMap);
                        }
                    }
                }
                // 3. Any other TSqlFragment property to recurse
                else if (val is TSqlFragment fragmentChild)
                {
                    ReplaceStatements(fragmentChild, replacementMap);
                }
                // 4. Collections of TSqlFragment (fallback for read-only or non-IList collections)
                else if (val is System.Collections.IEnumerable enumerable && !(val is string))
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

    internal class TableVariableDeclarationCollector : TSqlFragmentVisitor
    {
        public List<DeclareTableVariableStatement> Declarations { get; } = new List<DeclareTableVariableStatement>();
        public HashSet<DeclareTableVariableStatement> InsideSchemaObjects { get; } = new HashSet<DeclareTableVariableStatement>();

        private bool _inSchemaObject = false;
        private bool _inFunctionOrView = false;

        public override void ExplicitVisit(CreateProcedureStatement node)
        {
            bool wasInSchemaObject = _inSchemaObject;
            _inSchemaObject = true;
            base.ExplicitVisit(node);
            _inSchemaObject = wasInSchemaObject;
        }

        public override void ExplicitVisit(AlterProcedureStatement node)
        {
            bool wasInSchemaObject = _inSchemaObject;
            _inSchemaObject = true;
            base.ExplicitVisit(node);
            _inSchemaObject = wasInSchemaObject;
        }

        public override void ExplicitVisit(CreateTriggerStatement node)
        {
            bool wasInSchemaObject = _inSchemaObject;
            _inSchemaObject = true;
            base.ExplicitVisit(node);
            _inSchemaObject = wasInSchemaObject;
        }

        public override void ExplicitVisit(AlterTriggerStatement node)
        {
            bool wasInSchemaObject = _inSchemaObject;
            _inSchemaObject = true;
            base.ExplicitVisit(node);
            _inSchemaObject = wasInSchemaObject;
        }

        public override void ExplicitVisit(CreateFunctionStatement node)
        {
            bool wasInSchemaObject = _inSchemaObject;
            bool wasInFunctionOrView = _inFunctionOrView;
            _inSchemaObject = true;
            _inFunctionOrView = true;
            base.ExplicitVisit(node);
            _inSchemaObject = wasInSchemaObject;
            _inFunctionOrView = wasInFunctionOrView;
        }

        public override void ExplicitVisit(AlterFunctionStatement node)
        {
            bool wasInSchemaObject = _inSchemaObject;
            bool wasInFunctionOrView = _inFunctionOrView;
            _inSchemaObject = true;
            _inFunctionOrView = true;
            base.ExplicitVisit(node);
            _inSchemaObject = wasInSchemaObject;
            _inFunctionOrView = wasInFunctionOrView;
        }

        public override void ExplicitVisit(CreateViewStatement node)
        {
            bool wasInSchemaObject = _inSchemaObject;
            bool wasInFunctionOrView = _inFunctionOrView;
            _inSchemaObject = true;
            _inFunctionOrView = true;
            base.ExplicitVisit(node);
            _inSchemaObject = wasInSchemaObject;
            _inFunctionOrView = wasInFunctionOrView;
        }

        public override void ExplicitVisit(AlterViewStatement node)
        {
            bool wasInSchemaObject = _inSchemaObject;
            bool wasInFunctionOrView = _inFunctionOrView;
            _inSchemaObject = true;
            _inFunctionOrView = true;
            base.ExplicitVisit(node);
            _inSchemaObject = wasInSchemaObject;
            _inFunctionOrView = wasInFunctionOrView;
        }

        public override void ExplicitVisit(DeclareTableVariableStatement node)
        {
            if (!_inFunctionOrView)
            {
                Declarations.Add(node);
                if (_inSchemaObject)
                {
                    InsideSchemaObjects.Add(node);
                }
            }
            base.ExplicitVisit(node);
        }
    }

    internal class VariableRenameVisitor : TSqlFragmentVisitor
    {
        private readonly Dictionary<string, string> _renameMap;

        public VariableRenameVisitor(Dictionary<string, string> renameMap)
        {
            _renameMap = new Dictionary<string, string>(renameMap, StringComparer.OrdinalIgnoreCase);
        }

        public override void ExplicitVisit(VariableReference node)
        {
            if (_renameMap.TryGetValue(node.Name, out var newName))
            {
                node.Name = newName;
            }
            base.ExplicitVisit(node);
        }
    }
}
