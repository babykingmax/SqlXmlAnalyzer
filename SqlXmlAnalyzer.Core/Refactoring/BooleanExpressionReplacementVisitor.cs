using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlXmlAnalyzer.Core.Refactoring
{
    public abstract class BooleanExpressionReplacementVisitor : TSqlFragmentVisitor
    {
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _propertyCache = new();
        protected readonly RefactorContext _context;
        public bool Changed { get; protected set; }

        protected BooleanExpressionReplacementVisitor(RefactorContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        private PropertyInfo[] GetBooleanExpressionProperties(Type type)
        {
            return _propertyCache.GetOrAdd(type, t =>
            {
                var list = new List<PropertyInfo>();
                foreach (var prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!prop.CanRead) continue;

                    if (typeof(BooleanExpression).IsAssignableFrom(prop.PropertyType) && prop.CanWrite)
                    {
                        list.Add(prop);
                    }
                    else
                    {
                        var listInterface = prop.PropertyType.IsGenericType && prop.PropertyType.GetGenericTypeDefinition() == typeof(IList<>)
                            ? prop.PropertyType
                            : prop.PropertyType.GetInterfaces()
                                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IList<>));

                        if (listInterface != null)
                        {
                            var genericArgs = listInterface.GetGenericArguments();
                            if (genericArgs.Length == 1 && typeof(BooleanExpression).IsAssignableFrom(genericArgs[0]))
                            {
                                list.Add(prop);
                            }
                        }
                    }
                }
                return list.ToArray();
            });
        }

        public override void Visit(TSqlFragment node)
        {
            if (node == null) return;

            var props = GetBooleanExpressionProperties(node.GetType());
            foreach (var prop in props)
            {
                if (typeof(BooleanExpression).IsAssignableFrom(prop.PropertyType))
                {
                    var val = prop.GetValue(node) as BooleanExpression;
                    if (val != null)
                    {
                        var replacement = ReplaceExpression(val);
                        if (replacement != val && replacement != null)
                        {
                            prop.SetValue(node, replacement);
                            Changed = true;
                            _context.Changed = true;
                        }
                    }
                }
                else
                {
                    if (prop.GetValue(node) is System.Collections.IList list)
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            if (list[i] is BooleanExpression expr)
                            {
                                var replacement = ReplaceExpression(expr);
                                if (replacement != expr && replacement != null)
                                {
                                    list[i] = replacement;
                                    Changed = true;
                                    _context.Changed = true;
                                }
                            }
                        }
                    }
                }
            }

            base.Visit(node);
        }

        protected abstract BooleanExpression ReplaceExpression(BooleanExpression expression);
    }
}

