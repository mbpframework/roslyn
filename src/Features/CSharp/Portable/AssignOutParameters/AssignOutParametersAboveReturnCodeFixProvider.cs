﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using System.Composition;
using System.Diagnostics;
using System.Threading;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.AssignOutParameters
{
    [ExportCodeFixProvider(LanguageNames.CSharp), Shared]
    internal class AssignOutParametersAboveReturnCodeFixProvider : AbstractAssignOutParametersCodeFixProvider
    {
        protected override void TryRegisterFix(CodeFixContext context, Document document, SyntaxNode container, SyntaxNode location)
        {
            context.RegisterCodeFix(new MyCodeAction(
               CSharpFeaturesResources.Assign_out_parameters,
               c => FixAsync(document, context.Diagnostics[0], c)), context.Diagnostics);
        }

        protected override void AssignOutParameters(
            SyntaxEditor editor, SyntaxNode container,
            MultiDictionary<SyntaxNode, (SyntaxNode exprOrStatement, ImmutableArray<IParameterSymbol> unassignedParameters)>.ValueSet values,
            CancellationToken cancellationToken)
        {
            foreach (var (exprOrStatement, unassignedParameters) in values)
            {
                var statements = GenerateAssignmentStatements(editor.Generator, unassignedParameters);
                AddAssignmentStatements(editor, exprOrStatement, statements);
            }
        }

        private static void AddAssignmentStatements(
            SyntaxEditor editor, SyntaxNode exprOrStatement, ImmutableArray<SyntaxNode> statements)
        {
            var generator = editor.Generator;

            var parent = exprOrStatement.Parent;
            if (parent.IsEmbeddedStatementOwner())
            {
                var newBody = editor.Generator.ScopeBlock(statements.Add(exprOrStatement));
                editor.ReplaceNode(exprOrStatement, newBody);
                editor.ReplaceNode(
                    exprOrStatement.Parent,
                    (c, _) => c.WithAdditionalAnnotations(Formatter.Annotation));
            }
            else if (parent is BlockSyntax || parent is SwitchSectionSyntax)
            {
                editor.InsertBefore(exprOrStatement, statements);
            }
            else if (parent is ArrowExpressionClauseSyntax)
            {
                statements = statements.Add(generator.ReturnStatement(exprOrStatement));
                editor.ReplaceNode(
                    parent.Parent,
                    generator.WithStatements(parent.Parent, statements));
            }
            else
            {
                var lambda = (LambdaExpressionSyntax)parent;
                var newBody = editor.Generator.ScopeBlock(statements.Add(generator.ReturnStatement(exprOrStatement)));
                editor.ReplaceNode(
                    lambda,
                    lambda.WithBody((CSharpSyntaxNode)newBody)
                          .WithAdditionalAnnotations(Formatter.Annotation));
            }
        }

        private static void ReplaceWithBlock(
            SyntaxEditor editor, SyntaxNode exprOrStatement, ImmutableArray<SyntaxNode> statements)
        {
            editor.ReplaceNode(
                exprOrStatement,
                editor.Generator.ScopeBlock(statements));
            editor.ReplaceNode(
                exprOrStatement.Parent,
                (c, _) => c.WithAdditionalAnnotations(Formatter.Annotation));
        }
    }
}
