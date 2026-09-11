using System;
using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

using SqlXmlAnalyzer.Core;

namespace SqlXmlAnalyzer.Refactoring.Rules
{
    /// <summary>Compatibility visitor; direct invocation must obey the proposal safety boundary.</summary>
    public class TableVariableVisitor : TSqlFragmentVisitor
    {
        private readonly RefactorContext _context;
        public bool Changed => false;

        public TableVariableVisitor(RefactorContext context) =>
            _context = context ?? throw new ArgumentNullException(nameof(context));

        public override void ExplicitVisit(DeclareTableVariableStatement node)
        {
            _context.SkipUnsafeRewrite("REF_RULE_002_TABLE_VAR", "UnprovenTableVariableEquivalence",
                "保留表变量；直接调用访问器也不能绕过提案审核。",
                "事务、作用域、临时对象命名及清理所有权尚未验证。");
            base.ExplicitVisit(node);
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
