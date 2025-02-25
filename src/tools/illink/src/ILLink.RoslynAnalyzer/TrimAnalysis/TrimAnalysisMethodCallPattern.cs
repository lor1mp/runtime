// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using ILLink.Shared.DataFlow;
using ILLink.Shared.TrimAnalysis;
using ILLink.Shared.TypeSystemProxy;
using Microsoft.CodeAnalysis;
using MultiValue = ILLink.Shared.DataFlow.ValueSet<ILLink.Shared.DataFlow.SingleValue>;

namespace ILLink.RoslynAnalyzer.TrimAnalysis
{
	public readonly record struct TrimAnalysisMethodCallPattern
	{
		public IMethodSymbol CalledMethod { init; get; }
		public MultiValue Instance { init; get; }
		public ImmutableArray<MultiValue> Arguments { init; get; }
		public IOperation Operation { init; get; }
		public ISymbol OwningSymbol { init; get; }

		public TrimAnalysisMethodCallPattern (
			IMethodSymbol calledMethod,
			MultiValue instance,
			ImmutableArray<MultiValue> arguments,
			IOperation operation,
			ISymbol owningSymbol)
		{
			CalledMethod = calledMethod;
			Instance = instance.DeepCopy ();
			if (arguments.IsEmpty) {
				Arguments = ImmutableArray<MultiValue>.Empty;
			} else {
				var builder = ImmutableArray.CreateBuilder<MultiValue> ();
				foreach (var argument in arguments) {
					builder.Add (argument.DeepCopy ());
				}
				Arguments = builder.ToImmutableArray ();
			}
			Operation = operation;
			OwningSymbol = owningSymbol;
		}

		public TrimAnalysisMethodCallPattern Merge (ValueSetLattice<SingleValue> lattice, TrimAnalysisMethodCallPattern other)
		{
			Debug.Assert (Operation == other.Operation);
			Debug.Assert (SymbolEqualityComparer.Default.Equals (CalledMethod, other.CalledMethod));
			Debug.Assert (SymbolEqualityComparer.Default.Equals (OwningSymbol, other.OwningSymbol));
			Debug.Assert (Arguments.Length == other.Arguments.Length);

			var argumentsBuilder = ImmutableArray.CreateBuilder<MultiValue> ();
			for (int i = 0; i < Arguments.Length; i++) {
				argumentsBuilder.Add (lattice.Meet (Arguments[i], other.Arguments[i]));
			}

			return new TrimAnalysisMethodCallPattern (
				CalledMethod,
				lattice.Meet (Instance, other.Instance),
				argumentsBuilder.ToImmutable (),
				Operation,
				OwningSymbol);
		}

		public IEnumerable<Diagnostic> CollectDiagnostics (DataFlowAnalyzerContext context)
		{
			DiagnosticContext diagnosticContext = new (Operation.Syntax.GetLocation ());
			if (context.EnableTrimAnalyzer && !OwningSymbol.IsInRequiresUnreferencedCodeAttributeScope(out _))
			{
				TrimAnalysisVisitor.HandleCall(Operation, OwningSymbol, CalledMethod, Instance, Arguments, diagnosticContext, default, out var _);
			}

			foreach (var requiresAnalyzer in context.EnabledRequiresAnalyzers)
			{
				if (requiresAnalyzer.CheckAndCreateRequiresDiagnostic(Operation, CalledMethod, OwningSymbol, context, out Diagnostic? diag))
					diagnosticContext.AddDiagnostic(diag);
			}

			return diagnosticContext.Diagnostics;
		}
	}
}
