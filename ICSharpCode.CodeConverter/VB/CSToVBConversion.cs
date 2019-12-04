﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ICSharpCode.CodeConverter.CSharp;
using ICSharpCode.CodeConverter.Shared;
using ICSharpCode.CodeConverter.Util;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.VisualBasic;
using VBSyntaxFactory = Microsoft.CodeAnalysis.VisualBasic.SyntaxFactory;
using CSSyntax = Microsoft.CodeAnalysis.CSharp.Syntax;
using SyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using VBSyntax = Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace ICSharpCode.CodeConverter.VB
{
    /// <remarks>
    /// Can be used for multiple projects - does not persist state from one InitializeSource to the next
    /// </remarks>
    public class CSToVBConversion : ILanguageConversion
    {
        private const string UnresolvedNamespaceDiagnosticId = "BC40056";

        public string RootNamespace { get; set; }
        private CSToVBProjectContentsConverter _csToVbProjectContentsConverter;
        public VisualBasicCompilationOptions VBCompilationOptions { get; set; }
        public VisualBasicParseOptions VBParseOptions { get; set; }

        public async Task<Project> InitializeSource(Project project)
        {
            _csToVbProjectContentsConverter = new CSToVBProjectContentsConverter(RootNamespace, VBCompilationOptions, VBParseOptions);
            return await _csToVbProjectContentsConverter.InitializeSourceAsync(project);
        }

        public async Task<SyntaxNode> SingleFirstPass(Document document)
        {
            return await _csToVbProjectContentsConverter.SingleFirstPass(document);
        }
        string ILanguageConversion.LanguageVersion {
            get { return _csToVbProjectContentsConverter.LanguageVersion; }
        }

        public async Task<(Project project, List<(string Path, DocumentId DocId, string[] Errors)> firstPassDocIds)>
            GetConvertedProject((string Path, SyntaxNode Node, string[] Errors)[] firstPassResults)
        {
            return await _csToVbProjectContentsConverter.GetConvertedProject(firstPassResults);
        }

        public async Task<Document> SingleSecondPass(Document doc)
        {
            return await doc.SimplifyStatements<VBSyntax.ImportsStatementSyntax, VBSyntax.ExpressionSyntax>(UnresolvedNamespaceDiagnosticId);
        }

        public SyntaxNode GetSurroundedNode(IEnumerable<SyntaxNode> descendantNodes,
            bool surroundedWithMethod)
        {
            return surroundedWithMethod
                ? descendantNodes.OfType<CSSyntax.MethodDeclarationSyntax>().First<SyntaxNode>()
                : descendantNodes.OfType<CSSyntax.BaseTypeDeclarationSyntax>().First<SyntaxNode>();
        }

        public IReadOnlyCollection<(string, string)> GetProjectTypeGuidMappings()
        {
            return ProjectTypeGuids.VbToCsTypeGuids.Select((vbCs, i) => (vbCs.Item2, vbCs.Item1)).ToArray();
        }

        public IEnumerable<(string, string)> GetProjectFileReplacementRegexes()
        {
            return new[] {
                ("\\\\Microsoft\\.CSharp\\.targets", "\\Microsoft.VisualBasic.targets"),
                ("\\.cs\"", ".vb\""),
                ("\\.cs<", ".vb<")
            };
        }
        public string PostTransformProjectFile(string xml)
        {
            xml = ProjectFileTextEditor.WithUpdatedDefaultItemExcludes(xml, "vb", "cs");
            xml = TweakDefineConstantsSeparator(xml);
            xml = AddInfer(xml);
            return xml;
        }

        private string AddInfer(string xml)
        {
            if (xml.IndexOf("<OptionInfer>") > -1) return xml;

            string propertygroup = "<PropertyGroup>";
            var startOfFirstPropertyGroup = xml.IndexOf(propertygroup);
            if (startOfFirstPropertyGroup == -1) return xml;

            int endOfFirstPropertyGroupStartTag = startOfFirstPropertyGroup + propertygroup.Length;
            return xml.Substring(0, endOfFirstPropertyGroupStartTag) + Environment.NewLine + "    <OptionInfer>On</OptionInfer>" +
                   xml.Substring(endOfFirstPropertyGroupStartTag);
        }

        private static string TweakDefineConstantsSeparator(string s)
        {
            var startTag = "<DefineConstants>";
            var endTag = "</DefineConstants>";
            var defineConstantsStart = s.IndexOf(startTag);
            var defineConstantsEnd = s.IndexOf(endTag);
            if (defineConstantsStart == -1 || defineConstantsEnd == -1)
                return s;

            return s.Substring(0, defineConstantsStart) +
                   s.Substring(defineConstantsStart, defineConstantsEnd - defineConstantsStart).Replace(";", ",") +
                   s.Substring(defineConstantsEnd);
        }

        public string TargetLanguage { get; } = LanguageNames.VisualBasic;

        public bool CanBeContainedByMethod(SyntaxNode node)
        {
            return node is CSSyntax.IncompleteMemberSyntax ||
                   node is CSSyntax.StatementSyntax ||
                   node.ContainsSkippedText ||
                   node.IsMissing ||
                   ParsedAsFieldButCouldBeLocalVariableDeclaration(node); ;
        }

        public bool MustBeContainedByClass(SyntaxNode node)
        {
            return node is CSSyntax.BaseMethodDeclarationSyntax || node is CSSyntax.BaseFieldDeclarationSyntax ||
                   node is CSSyntax.BasePropertyDeclarationSyntax;
        }

        private static bool ParsedAsFieldButCouldBeLocalVariableDeclaration(SyntaxNode node)
        {
            return node is CSSyntax.FieldDeclarationSyntax f && f.Modifiers.All(m => m.IsKind(SyntaxKind.TypeVarKeyword));
        }

        public string WithSurroundingMethod(string text)
        {
            return $@"void SurroundingSub()
{{
{text}
}}";
        }

        public string WithSurroundingClass(string text)
        {
            var modifier = text.Contains("abstract ") ? "abstract " : "";
            return $@"{modifier}class SurroundingClass
{{
{text}
}}";
        }

        public List<SyntaxNode> FindSingleImportantChild(SyntaxNode annotatedNode)
        {
            var children = annotatedNode.ChildNodes().ToList();
            if (children.Count > 1) {
                switch (annotatedNode) {
                    case VBSyntax.TypeBlockSyntax typeBlock:
                        return typeBlock.Members.ToList<SyntaxNode>();
                    case VBSyntax.MethodBlockBaseSyntax methodBlock:
                        return methodBlock.Statements.ToList<SyntaxNode>();
                }
            }
            return children;
        }

        public SyntaxTree CreateTree(string text)
        {
            return new CSharpCompiler().CreateTree(text);
        }

        public Document CreateProjectDocumentFromTree(Workspace workspace, SyntaxTree tree,
            IEnumerable<MetadataReference> references)
        {
            return CSharpCompiler.CreateCompilationOptions().CreateProjectDocumentFromTree(workspace, tree, references, CSharpParseOptions.Default);
        }
    }
}