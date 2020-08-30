﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.QuickInfo
{
    [ExportQuickInfoProvider(QuickInfoProviderNames.DiagnosticAnalyzer, LanguageNames.CSharp), Shared]
    [ExtensionOrder(Before = QuickInfoProviderNames.Semantic)]
    internal class CSharpDiagnosticAnalyzerQuickInfoProvider : CommonQuickInfoProvider
    {
        private readonly IDiagnosticAnalyzerService _diagnosticAnalyzerService;

        [ImportingConstructor]
        [SuppressMessage("RoslynDiagnosticsReliability", "RS0033:Importing constructor should be [Obsolete]", Justification = "Used in test code: https://github.com/dotnet/roslyn/issues/42814")]
        public CSharpDiagnosticAnalyzerQuickInfoProvider(IDiagnosticAnalyzerService diagnosticAnalyzerService)
        {
            _diagnosticAnalyzerService = diagnosticAnalyzerService;
        }

        protected override async Task<QuickInfoItem?> BuildQuickInfoAsync(
            Document document,
            SyntaxToken token,
            CancellationToken cancellationToken)
        {
            return GetQuickinfoForPragmaWarning(document, token) ??
                (await GetQuickInfoForSupressMessageAttributeAsync(document, token, cancellationToken).ConfigureAwait(false));
        }

        private QuickInfoItem? GetQuickinfoForPragmaWarning(Document document, SyntaxToken token)
        {
            var errorCode = token.Parent switch
            {
                PragmaWarningDirectiveTriviaSyntax directive
                    => token.IsKind(SyntaxKind.EndOfDirectiveToken)
                        ? directive.ErrorCodes.LastOrDefault() as IdentifierNameSyntax
                        : directive.ErrorCodes.FirstOrDefault() as IdentifierNameSyntax,
                IdentifierNameSyntax { Parent: PragmaWarningDirectiveTriviaSyntax _ } identifier
                    => identifier,
                _ => null,
            };

            if (errorCode != null)
            {
                return GetQuickInfoFromSupportedDiagnosticsOfProjectAnalyzers(document, errorCode.Identifier.ValueText, errorCode.Span);
            }

            return null;
        }

        private async Task<QuickInfoItem?> GetQuickInfoForSupressMessageAttributeAsync(
            Document document,
            SyntaxToken token,
            CancellationToken cancellationToken)
        {
            var supressMessageCheckIdArgument = token.GetAncestor<AttributeArgumentSyntax>() switch
            {
                AttributeArgumentSyntax
                {
                    Parent: AttributeArgumentListSyntax
                    {
                        Arguments: var arguments,
                        Parent: AttributeSyntax
                        {
                            Name: var name
                        } _
                    } _
                } argument when
                    name.IsSuppressMessageAttribute() &&
                    (argument.NameColon?.Name.Identifier.ValueText == "checkId" ||
                    arguments.IndexOf(argument) == 1) => argument,
                _ => null,
            };

            if (supressMessageCheckIdArgument != null)
            {
                var semanticModel = await document.GetRequiredSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                var checkIdObject = semanticModel.GetConstantValue(supressMessageCheckIdArgument.Expression, cancellationToken).GetValueOrNull();
                if (checkIdObject is string checkId)
                {
                    var position = checkId.IndexOf(':');
                    var errorCode = position == -1
                        ? checkId
                        : checkId.Substring(0, position);
                    errorCode = errorCode.Trim();
                    return GetQuickInfoFromSupportedDiagnosticsOfProjectAnalyzers(document, errorCode, supressMessageCheckIdArgument.Span);
                }
            }

            return null;
        }

        private QuickInfoItem? GetQuickInfoFromSupportedDiagnosticsOfProjectAnalyzers(Document document,
            string errorCode, TextSpan location)
        {
            var infoCache = _diagnosticAnalyzerService.AnalyzerInfoCache;
            var hostAnalyzers = document.Project.Solution.State.Analyzers;
            var groupedDiagnostics = hostAnalyzers.GetDiagnosticDescriptorsPerReference(infoCache, document.Project).Values;
            var supportedDiagnostics = groupedDiagnostics.SelectMany(d => d);
            var diagnosticDescriptor = supportedDiagnostics.FirstOrDefault(d => d.Id == errorCode);
            if (diagnosticDescriptor != null)
            {
                var description =
                    diagnosticDescriptor.Title.ToStringOrNull() ??
                    diagnosticDescriptor.Description.ToStringOrNull() ??
                    diagnosticDescriptor.MessageFormat.ToStringOrNull() ??
                    diagnosticDescriptor.Id;

                return CreateQuickInfo(location, description);
            }

            return null;
        }

        private static QuickInfoItem CreateQuickInfo(TextSpan location, string description,
            params TextSpan[] relatedSpans)
        {
            return QuickInfoItem.Create(location, sections: new[]
                {
                    QuickInfoSection.Create(QuickInfoSectionKinds.Description, new[]
                    {
                        new TaggedText(TextTags.Text, description)
                    }.ToImmutableArray())
                }.ToImmutableArray(), relatedSpans: relatedSpans.ToImmutableArray());
        }
    }

    internal static class HelperExtensions
    {
        public static string? ToStringOrNull(this LocalizableString @this)
        {
            var result = @this.ToString();
            if (string.IsNullOrWhiteSpace(result))
            {
                return null;
            }

            return result;
        }

        public static bool IsSuppressMessageAttribute(this NameSyntax? name)
        {
            if (name == null)
            {
                return false;
            }

            var nameValue = name.GetNameToken().ValueText;
            var stringComparer = StringComparer.Ordinal;
            return
                stringComparer.Equals(nameValue, nameof(SuppressMessageAttribute)) ||
                stringComparer.Equals(nameValue, "SuppressMessage");
        }
    }
}
