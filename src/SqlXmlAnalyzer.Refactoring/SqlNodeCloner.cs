using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlXmlAnalyzer.Refactoring.Rules
{
    /// <summary>
    /// Utility class to clone SQL AST nodes in ScriptDom using reflection to prevent node re-use corruption.
    /// </summary>
    public static class SqlNodeCloner
    {
        public static TSqlFragment? Clone(TSqlFragment? node)
        {
            if (node == null) return null;

            Type type = node.GetType();
            var clone = (TSqlFragment)Activator.CreateInstance(type)!;

            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                if (!prop.CanRead) continue;
                if (prop.GetIndexParameters().Length > 0) continue;

                // Skip system properties that link back to the parser/token stream or parent pointers
                if (prop.Name == "Parent" || 
                    prop.Name == "FirstTokenIndex" || 
                    prop.Name == "LastTokenIndex" || 
                    prop.Name == "ScriptTokenStream")
                {
                    continue;
                }

                if (prop.CanWrite)
                {
                    object? val = prop.GetValue(node);
                    if (val == null)
                    {
                        prop.SetValue(clone, null);
                    }
                    else if (val is TSqlFragment childFragment)
                    {
                        prop.SetValue(clone, Clone(childFragment));
                    }
                    else if (val is string || val.GetType().IsValueType)
                    {
                        prop.SetValue(clone, val);
                    }
                    else
                    {
                        prop.SetValue(clone, val);
                    }
                }
                else
                {
                    // Handle read-only collections/lists
                    object? val = prop.GetValue(node);
                    if (val is IList list)
                    {
                        object? cloneList = prop.GetValue(clone);
                        if (cloneList is IList targetList)
                        {
                            foreach (object item in list)
                            {
                                if (item is TSqlFragment itemFragment)
                                {
                                    targetList.Add(Clone(itemFragment));
                                }
                                else
                                {
                                    targetList.Add(item);
                                }
                            }
                        }
                    }
                }
            }

            return clone;
        }

        public static Identifier? CloneIdentifier(Identifier? ident)
        {
            return Clone(ident) as Identifier;
        }

        public static IdentifierOrValueExpression? CloneIdentifierOrValueExpression(IdentifierOrValueExpression? expr)
        {
            return Clone(expr) as IdentifierOrValueExpression;
        }
    }
}
