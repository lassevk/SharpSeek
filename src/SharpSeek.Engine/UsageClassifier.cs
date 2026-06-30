using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace SharpSeek.Engine;

/// <summary>
/// Classifies a single reference from the syntax node and <see cref="IOperation"/> the reference
/// search already produced - no extra semantic queries. Covers read/write usage of data symbols
/// (field, property, local, parameter, event), the constant assigned at a write, and the syntactic
/// role the symbol was mentioned in (nameof/typeof/construction/attribute/invocation/method-group).
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
        SymbolKind symbolKind,
        CancellationToken cancellationToken)
    {
        SyntaxNode identifier = root.FindNode(span, getInnermostNodeForTie: true);
        SyntaxNode reference = AscendToReferenceExpression(identifier);

        ReferenceRole? role = ClassifyRole(reference, symbolKind);

        SymbolUsage? usage = null;
        AssignedConstant? constant = null;
        IOperation? operation = semanticModel.GetOperation(reference, cancellationToken);
        if (operation is not null && IsDataReference(operation))
        {
            (usage, constant) = ClassifyUsage(operation);
        }

        return new ReferenceUsage(usage, constant, role);
    }

    // ---- Syntactic role: how the symbol was mentioned ----

    // Detected from the syntax node alone (cheap, no semantic queries). Only the unambiguous roles
    // are reported; an ordinary reference (a plain read/write, or a type used as a variable type)
    // returns null. Richer roles (cref, cast, pattern, base-type, ...) are tracked separately.
    private static ReferenceRole? ClassifyRole(SyntaxNode reference, SymbolKind symbolKind)
    {
        if (IsInsideNameOf(reference))
        {
            return ReferenceRole.NameOf;
        }

        if (reference.FirstAncestorOrSelf<TypeOfExpressionSyntax>() is { } typeOf
            && typeOf.Type.Span.Contains(reference.Span))
        {
            return ReferenceRole.TypeOf;
        }

        if (reference.FirstAncestorOrSelf<AttributeSyntax>() is { } attribute
            && attribute.Name.Span.Contains(reference.Span))
        {
            return ReferenceRole.Attribute;
        }

        if (reference.FirstAncestorOrSelf<ObjectCreationExpressionSyntax>() is { } creation
            && creation.Type.Span.Contains(reference.Span))
        {
            return ReferenceRole.Construction;
        }

        if (symbolKind == SymbolKind.Method)
        {
            return reference.Parent is InvocationExpressionSyntax invocation
                && invocation.Expression == reference
                    ? ReferenceRole.Invocation
                    : ReferenceRole.MethodGroup;
        }

        return null;
    }

    // True when the reference sits inside a nameof(...) argument - a compile-time name, not a use.
    private static bool IsInsideNameOf(SyntaxNode reference) =>
        reference.FirstAncestorOrSelf<InvocationExpressionSyntax>() is
            { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } } invocation
        && invocation.ArgumentList.Span.Contains(reference.Span);

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

    // ---- Read/write usage of a data symbol ----

    private static (SymbolUsage Usage, AssignedConstant? Constant) ClassifyUsage(IOperation operation)
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
                (SymbolUsage.Write, ConstantOf(assignment.Value)),
            ICompoundAssignmentOperation compound when ReferenceEquals(compound.Target, reference) =>
                (SymbolUsage.ReadWrite, null),
            ICoalesceAssignmentOperation coalesce when ReferenceEquals(coalesce.Target, reference) =>
                (SymbolUsage.ReadWrite, null),
            IDeconstructionAssignmentOperation deconstruction
                when ReferenceEquals(deconstruction.Target, reference) =>
                (SymbolUsage.Write, null),
            IIncrementOrDecrementOperation => (SymbolUsage.ReadWrite, null),
            IArgumentOperation argument => (
                argument.Parameter?.RefKind switch
                {
                    RefKind.Out => SymbolUsage.Write,
                    RefKind.Ref => SymbolUsage.ReadWrite,
                    _ => SymbolUsage.Read,
                },
                null),
            _ => (SymbolUsage.Read, null),
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
/// The classification of a single reference: how it is used (read/write), the constant assigned at
/// a simple-assignment write, and the syntactic role it was mentioned in.
/// </summary>
internal readonly record struct ReferenceUsage(
    SymbolUsage? Usage,
    AssignedConstant? AssignedConstant,
    ReferenceRole? Role);
