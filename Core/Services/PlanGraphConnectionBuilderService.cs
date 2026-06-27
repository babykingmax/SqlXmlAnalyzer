using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record PlanGraphConnectionPair(
        XElement SourceRelOp,
        XElement TargetRelOp);

    public sealed class PlanGraphConnectionBuilderService
    {
        public IReadOnlyList<PlanGraphConnectionPair> BuildConnections(
            IReadOnlyList<XElement> relOps,
            XNamespace ns)
        {
            ArgumentNullException.ThrowIfNull(relOps);
            ArgumentNullException.ThrowIfNull(ns);

            var relOpSet = relOps.ToHashSet();
            var connections = new List<PlanGraphConnectionPair>();

            foreach (XElement relOp in relOps)
            {
                foreach (XElement child in PlanDiagnosticAnalyzer.GetDirectChildRelOps(relOp, ns))
                {
                    if (relOpSet.Contains(child))
                    {
                        connections.Add(new PlanGraphConnectionPair(child, relOp));
                    }
                }
            }

            return connections;
        }
    }
}
