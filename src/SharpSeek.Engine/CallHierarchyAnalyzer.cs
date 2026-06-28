using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace SharpSeek.Engine;

/// <summary>A caller of a method and the call site location(s).</summary>
public sealed record IncomingCall(string Caller, IReadOnlyList<ReferenceLocationInfo> CallSites);

/// <summary>A method called by the analysed method, and the call site.</summary>
public sealed record OutgoingCall(string Callee, ReferenceLocationInfo CallSite);

/// <summary>The incoming and outgoing calls for a single method.</summary>
public sealed record CallHierarchy(
    string Method,
    IReadOnlyList<IncomingCall> Incoming,
    IReadOnlyList<OutgoingCall> Outgoing);

/// <summary>
/// Computes the call hierarchy of a method: who calls it (incoming) and what it calls (outgoing).
/// Incoming callers are found via <see cref="SymbolFinder"/>, so a method invoked from generated
/// code is reported with its call site mapped back to the original source.
/// </summary>
public sealed class CallHierarchyAnalyzer
{
    public async Task<IReadOnlyList<CallHierarchy>> AnalyzeAsync(
        Solution solution,
        string methodName,
        CancellationToken cancellationToken = default)
    {
        HashSet<string> handwrittenPaths = LocationDescriptor.HandwrittenPaths(solution);

        IEnumerable<ISymbol> symbols = await SymbolFinder
            .FindSourceDeclarationsAsync(solution, methodName, ignoreCase: false, cancellationToken)
            .ConfigureAwait(false);

        List<CallHierarchy> results = [];
        foreach (IMethodSymbol method in symbols.OfType<IMethodSymbol>())
        {
            IReadOnlyList<IncomingCall> incoming =
                await FindIncomingAsync(method, solution, handwrittenPaths, cancellationToken)
                    .ConfigureAwait(false);
            IReadOnlyList<OutgoingCall> outgoing =
                await FindOutgoingAsync(method, solution, handwrittenPaths, cancellationToken)
                    .ConfigureAwait(false);

            results.Add(new CallHierarchy(method.ToDisplayString(), incoming, outgoing));
        }

        return results;
    }

    private static async Task<IReadOnlyList<IncomingCall>> FindIncomingAsync(
        IMethodSymbol method,
        Solution solution,
        HashSet<string> handwrittenPaths,
        CancellationToken cancellationToken)
    {
        IEnumerable<SymbolCallerInfo> callers = await SymbolFinder
            .FindCallersAsync(method, solution, cancellationToken)
            .ConfigureAwait(false);

        List<IncomingCall> incoming = [];
        foreach (SymbolCallerInfo caller in callers)
        {
            List<ReferenceLocationInfo> sites = [];
            foreach (Location location in caller.Locations)
            {
                if (location is { IsInSource: true, SourceTree: { } tree })
                {
                    sites.Add(LocationDescriptor.Describe(tree, location.SourceSpan, handwrittenPaths));
                }
            }

            if (sites.Count > 0)
            {
                incoming.Add(new IncomingCall(caller.CallingSymbol.ToDisplayString(), sites));
            }
        }

        return incoming;
    }

    private static async Task<IReadOnlyList<OutgoingCall>> FindOutgoingAsync(
        IMethodSymbol method,
        Solution solution,
        HashSet<string> handwrittenPaths,
        CancellationToken cancellationToken)
    {
        List<OutgoingCall> outgoing = [];
        foreach (SyntaxReference reference in method.DeclaringSyntaxReferences)
        {
            SyntaxNode node = await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
            Document? document = solution.GetDocument(node.SyntaxTree);
            if (document is null)
            {
                continue;
            }

            SemanticModel? model = await document.GetSemanticModelAsync(cancellationToken)
                .ConfigureAwait(false);
            if (model is null)
            {
                continue;
            }

            foreach (InvocationExpressionSyntax invocation in node.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not { } called)
                {
                    continue;
                }

                Location location = invocation.GetLocation();
                if (location.SourceTree is { } tree)
                {
                    outgoing.Add(new OutgoingCall(
                        called.ToDisplayString(),
                        LocationDescriptor.Describe(tree, location.SourceSpan, handwrittenPaths)));
                }
            }
        }

        return outgoing;
    }
}
