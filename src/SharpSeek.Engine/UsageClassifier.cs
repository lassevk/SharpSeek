using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace SharpSeek.Engine;

/// <summary>
/// Classifies how a data symbol (field, property, local, parameter, event) is used at a single
/// reference - read, written, or both - using the Roslyn <see cref="IOperation"/> tree. Returns
/// <c>null</c> when read/write does not apply, for example a method invocation, a type reference,
/// or a <c>nameof</c> / <c>typeof</c> mention.
/// </summary>
internal static class UsageClassifier
{
    /// <summary>
    /// Classifies the reference at <paramref name="span"/> within <paramref name="root"/>.
    /// </summary>
    public static ReferenceUsage Classify(
        SemanticModel semanticModel,
        SyntaxNode root,
        TextSpan span,
        CancellationToken cancellationToken)
    {
        SyntaxNode identifier = root.FindNode(span, getInnermostNodeForTie: true);
        SyntaxNode reference = AscendToReferenceExpression(identifier);

        IOperation? operation = semanticModel.GetOperation(reference, cancellationToken);
        if (operation is null || !IsDataReference(operation))
        {
            return default;
        }

        return Classify(operation);
    }

    // The reference span points at the identifier token. For a member access such as `x.Prop`, the
    // operation we want is the whole `x.Prop` expression (the property/field reference), not the
    // bare `Prop` name, so climb past the enclosing member access.
    private static SyntaxNode AscendToReferenceExpression(SyntaxNode node)
    {
        while (node.Parent is { } parent)
        {
            switch (parent)
            {
                case MemberAccessExpressionSyntax memberAccess when memberAccess.Name == node:
                case MemberBindingExpressionSyntax memberBinding when memberBinding.Name == node:
                    node = parent;
                    continue;
                default:
                    return node;
            }
        }

        return node;
    }

    private static bool IsDataReference(IOperation operation) =>
        operation is IFieldReferenceOperation
            or IPropertyReferenceOperation
            or ILocalReferenceOperation
            or IParameterReferenceOperation
            or IEventReferenceOperation;

    private static ReferenceUsage Classify(IOperation operation)
    {
        IOperation reference = operation;
        IOperation? parent = operation.Parent;

        // See through transparent wrappers (implicit conversions, parentheses, tuples) so the
        // surrounding assignment/argument context is visible.
        while (parent is IConversionOperation or IParenthesizedOperation or ITupleOperation)
        {
            reference = parent;
            parent = parent.Parent;
        }

        return parent switch
        {
            // Only a plain assignment has a single, well-defined assigned value to report. Compound
            // assignment, increment, and ref/out modify in place, so no assigned-constant is given.
            ISimpleAssignmentOperation assignment when ReferenceEquals(assignment.Target, reference) =>
                new ReferenceUsage(SymbolUsage.Write, ConstantOf(assignment.Value)),
            ICompoundAssignmentOperation compound when ReferenceEquals(compound.Target, reference) =>
                new ReferenceUsage(SymbolUsage.ReadWrite, null),
            ICoalesceAssignmentOperation coalesce when ReferenceEquals(coalesce.Target, reference) =>
                new ReferenceUsage(SymbolUsage.ReadWrite, null),
            IDeconstructionAssignmentOperation deconstruction
                when ReferenceEquals(deconstruction.Target, reference) =>
                new ReferenceUsage(SymbolUsage.Write, null),
            IIncrementOrDecrementOperation => new ReferenceUsage(SymbolUsage.ReadWrite, null),
            IArgumentOperation argument => new ReferenceUsage(
                argument.Parameter?.RefKind switch
                {
                    RefKind.Out => SymbolUsage.Write,
                    RefKind.Ref => SymbolUsage.ReadWrite,
                    _ => SymbolUsage.Read,
                },
                null),
            _ => new ReferenceUsage(SymbolUsage.Read, null),
        };
    }

    // Reads the constant the compiler already resolved for the assigned expression. Returns null
    // (no constant) when the value is not a compile-time constant - deliberately distinct from a
    // constant whose value is null, so "unknown" is never reported as a written null.
    private static AssignedConstant? ConstantOf(IOperation value)
    {
        Optional<object?> constant = value.ConstantValue;
        return constant.HasValue ? new AssignedConstant(constant.Value) : null;
    }
}

/// <summary>
/// The classification of a single reference: how it is used and, for a simple-assignment write,
/// the constant value assigned (if the assigned expression is a compile-time constant).
/// </summary>
internal readonly record struct ReferenceUsage(SymbolUsage? Usage, AssignedConstant? AssignedConstant);
