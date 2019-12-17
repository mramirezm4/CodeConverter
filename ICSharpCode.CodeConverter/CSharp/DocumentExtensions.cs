﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.CodeConverter.Util;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Simplification;
using VBasic = Microsoft.CodeAnalysis.VisualBasic;
using VBSyntax = Microsoft.CodeAnalysis.VisualBasic.Syntax;
using CSSyntax = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ICSharpCode.CodeConverter.CSharp
{
    internal static class DocumentExtensions
    {
        public const string DoNotSimplifyAnnotation = "DoNotSimplify";
        public static async Task<Document> WithSimplifiedSyntaxRootAsync(this Document doc, SyntaxNode syntaxRoot = null)
        {
            var root = syntaxRoot  ?? await doc.GetSyntaxRootAsync();
            var withSyntaxRoot = doc.WithSyntaxRoot(root);
            try {
                return await Simplifier.ReduceAsync(withSyntaxRoot);
            } catch {
                return doc;
            }
        }

        public static async Task<Document> SimplifyStatements<TUsingDirectiveSyntax, TExpressionSyntax>(this Document convertedDocument, string unresolvedTypeDiagnosticId)
        where TUsingDirectiveSyntax : SyntaxNode where TExpressionSyntax : SyntaxNode
        {
            var originalRoot = await convertedDocument.GetSyntaxRootAsync();
            var nodesWithUnresolvedTypes = (await convertedDocument.GetSemanticModelAsync()).GetDiagnostics()
                .Where(d => d.Id == unresolvedTypeDiagnosticId && d.Location.IsInSource)
                .Select(d => originalRoot.FindNode(d.Location.SourceSpan).GetAncestor<TUsingDirectiveSyntax>())
                .ToLookup(d => (SyntaxNode)d);
            var annotatedNodesAndParents = originalRoot.GetAnnotatedNodes(DoNotSimplifyAnnotation)
                .SelectMany(x => x.AncestorsAndSelf())
                .Distinct()
                .ToLookup(x => x);
            var toSimplify = originalRoot
                .DescendantNodes(n => !(n is TExpressionSyntax) && !nodesWithUnresolvedTypes.Contains(n))
                .Where(n => !nodesWithUnresolvedTypes.Contains(n) && !annotatedNodesAndParents.Contains(n));
            var newRoot = originalRoot.ReplaceNodes(toSimplify, (orig, rewritten) =>
                rewritten.WithAdditionalAnnotations(Simplifier.Annotation)
                );

            var document = await convertedDocument.WithSimplifiedSyntaxRootAsync(newRoot);
            return document;
        }
    }
}