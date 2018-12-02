﻿// -----------------------------------------------------------------------------
//                                    ILGPU
//                     Copyright (c) 2016-2018 Marcel Koester
//                                www.ilgpu.net
//
// File: Method.cs
//
// This file is part of ILGPU and is distributed under the University of
// Illinois Open Source License. See LICENSE.txt for details
// -----------------------------------------------------------------------------

using ILGPU.IR.Analyses;
using ILGPU.IR.Types;
using ILGPU.IR.Values;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace ILGPU.IR
{
    /// <summary>
    /// Represents custom function flags.
    /// </summary>
    [Flags]
    public enum MethodFlags : int
    {
        /// <summary>
        /// No flags (default).
        /// </summary>
        None = 0,

        /// <summary>
        /// This function should not be inlined.
        /// </summary>
        NoInlining = 1 << 0,

        /// <summary>
        /// This function should always be inlined.
        /// </summary>
        AggressiveInlining = 1 << 1,

        /// <summary>
        /// An external function declaration (without an implementation).
        /// </summary>
        ExternalDeclaration = 1 << 2,

        /// <summary>
        /// An external function reference (without an implementation).
        /// </summary>
        /// <remarks>Note that such a function is also marked as <see cref="NoInlining"/></remarks>
        External = ExternalDeclaration | NoInlining,
    }

    /// <summary>
    /// Represents transformation flags.
    /// </summary>
    [Flags]
    public enum MethodTransformationFlags : int
    {
        /// <summary>
        /// No flags (default).
        /// </summary>
        None = 0,

        /// <summary>
        /// This function has been modified since the last GC.
        /// </summary>
        Dirty = 1 << 0,

        /// <summary>
        /// This function has been transformed and does not require further
        /// transformation passes.
        /// </summary>
        Transformed = 1 << 1
    }

    /// <summary>
    /// Represents a function node within the IR.
    /// </summary>
    public sealed partial class Method : Node, IMethodMappingObject
    {
        #region Nested Types

        /// <summary>
        /// Represents a readonly view on all parameters.
        /// </summary>
        public readonly struct ParameterCollection : IReadOnlyCollection<Parameter>
        {
            #region Nested Types

            /// <summary>
            /// Enumerates all actual (not replaced) parameters.
            /// </summary>
            public struct Enumerator : IEnumerator<Parameter>
            {
                private readonly ImmutableArray<Parameter> parameters;
                private ImmutableArray<Parameter>.Enumerator enumerator;

                /// <summary>
                /// Constructs a new parameter enumerator.
                /// </summary>
                /// <param name="arguments">The parent source array.</param>
                internal Enumerator(ImmutableArray<Parameter> arguments)
                {
                    parameters = arguments;
                    enumerator = parameters.GetEnumerator();
                }

                /// <summary>
                /// Returns the current parameter.
                /// </summary>
                public Parameter Current => enumerator.Current;

                /// <summary cref="IEnumerator.Current"/>
                object IEnumerator.Current => Current;

                /// <summary cref="IDisposable.Dispose"/>
                public void Dispose() { }

                /// <summary cref="IEnumerator.MoveNext"/>
                public bool MoveNext() => enumerator.MoveNext();

                /// <summary cref="IEnumerator.Reset"/>
                void IEnumerator.Reset() => throw new InvalidOperationException();
            }

            #endregion

            #region Instance

            private readonly ImmutableArray<Parameter> parameters;

            /// <summary>
            /// Constructs a new parameter collection.
            /// </summary>
            /// <param name="nodeReferences">The source parameters.</param>
            internal ParameterCollection(ImmutableArray<Parameter> nodeReferences)
            {
                parameters = nodeReferences;
            }

            #endregion

            #region Properties

            /// <summary>
            /// Returns the number of attached parameters.
            /// </summary>
            public int Count => parameters.Length;

            /// <summary>
            /// Returns the i-th parameter.
            /// </summary>
            /// <param name="index">The parameter index.</param>
            /// <returns>The resolved parameter.</returns>
            public Parameter this[int index] => parameters[index];

            #endregion

            #region Methods

            /// <summary>
            /// Returns an enumerator to enumerate all actual (not replaced) parameters.
            /// </summary>
            /// <returns>The enumerator.</returns>
            public Enumerator GetEnumerator() => new Enumerator(parameters);

            /// <summary>
            /// Returns an enumerator to enumerator all actual (not replaced) parameters.
            /// </summary>
            /// <returns>The enumerator.</returns>
            IEnumerator<Parameter> IEnumerable<Parameter>.GetEnumerator() => GetEnumerator();

            /// <summary>
            /// Returns an enumerator to enumerator all actual (not replaced) parameters.
            /// </summary>
            /// <returns>The enumerator.</returns>
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            #endregion
        }

        /// <summary>
        /// Represents a parameter mapping.
        /// </summary>
        public readonly struct ParameterMapping
        {
            #region Instance

            /// <summary>
            /// Constructs a new parameter mapping.
            /// </summary>
            /// <param name="method">The associated method.</param>
            /// <param name="arguments">The parameter arguments.</param>
            internal ParameterMapping(
                Method method,
                ImmutableArray<ValueReference> arguments)
            {
                Debug.Assert(method != null, "Invalid method");
                Debug.Assert(arguments.Length == method.Parameters.Count, "Invalid arguments");

                Method = method;
                Arguments = arguments;
            }

            #endregion

            #region Properties

            /// <summary>
            /// Returns the associated method.
            /// </summary>
            public Method Method { get; }

            /// <summary>
            /// Returns the associated arguments.
            /// </summary>
            public ImmutableArray<ValueReference> Arguments { get; }

            /// <summary>
            /// Returns the value that is assigned to the given parameter.
            /// </summary>
            /// <param name="parameter">The parameter to map to a value.</param>
            /// <returns>The mapped value.</returns>
            public ValueReference this[Parameter parameter]
            {
                get
                {
                    Debug.Assert(parameter != null, "Invalid parameter");
                    Debug.Assert(parameter.Method == Method, "Invalid parameter");

                    return Arguments[parameter.Index];
                }
            }

            #endregion
        }

        /// <summary>
        /// Represents a method mapping.
        /// </summary>
        public readonly struct MethodMapping
        {
            #region Instance

            private readonly Dictionary<Method, Method> mapping;

            /// <summary>
            /// Constructs a new method mapping.
            /// </summary>
            /// <param name="methodMapping">The method mapping.</param>
            public MethodMapping(Dictionary<Method, Method> methodMapping)
            {
                mapping = methodMapping;
            }

            #endregion

            #region Properties

            /// <summary>
            /// Resolve the source method to a remapped target method.
            /// </summary>
            /// <param name="source">The source method.</param>
            /// <returns>The resolved target method.</returns>
            public Method this[Method source]
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    if (mapping != null && mapping.TryGetValue(source, out Method target))
                        return target;
                    return source;
                }
            }

            #endregion
        }

        #endregion

        #region Static

        /// <summary>
        /// Compares two methods according to their id.
        /// </summary>
        internal static readonly new Comparison<Method> Comparison =
            (first, second) => first.Id.CompareTo(second.Id);

        #endregion

        #region Instance

        /// <summary>
        /// Stores the internal transformation flags.
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private volatile MethodTransformationFlags transformationFlags =
            MethodTransformationFlags.None;

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private ImmutableArray<Parameter> parameters = ImmutableArray<Parameter>.Empty;

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private volatile Builder builder = null;

        /// <summary>
        /// Creates a new IRFunction instance.
        /// </summary>
        /// <param name="context">The context this function belongs to.</param>
        /// <param name="declaration">The associated declaration.</param>
        internal Method(
            IRContext context,
            in MethodDeclaration declaration)
        {
            Debug.Assert(context != null, "Invalid context");
            Debug.Assert(
                declaration.HasHandle && declaration.ReturnType != null,
                "Invalid declaration");
            Context = context;
            Declaration = declaration;
            Id = context.Context.CreateNodeId();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the associated IR context.
        /// </summary>
        public IRContext Context { get; }

        /// <summary>
        /// Returns the associated function name.
        /// </summary>
        public string Name => Declaration.Handle.ToString();

        /// <summary>
        /// Returns the associated function flags.
        /// </summary>
        public MethodFlags Flags => Declaration.Flags;

        /// <summary>
        /// Returns the associated function declaration.
        /// </summary>
        public MethodDeclaration Declaration { get; }

        /// <summary>
        /// Returns the associated function handle.
        /// </summary>
        public MethodHandle Handle => Declaration.Handle;

        /// <summary>
        /// Returns the original source method (may be null).
        /// </summary>
        public MethodBase Source => Declaration.Source;

        /// <summary>
        /// Returns the return-type of the method.
        /// </summary>
        public TypeNode ReturnType => Declaration.ReturnType;

        /// <summary>
        /// Returns true iff the return type of the method is void.
        /// </summary>
        public bool IsVoid => ReturnType.IsVoidType;

        /// <summary>
        /// Returns the current transformation flags.
        /// </summary>
        public MethodTransformationFlags TransformationFlags => transformationFlags;

        /// <summary>
        /// Returns all attached parameters.
        /// </summary>
        public ParameterCollection Parameters => new ParameterCollection(parameters);

        /// <summary>
        /// Returns the number of attached parameters.
        /// </summary>
        public int NumParameters => parameters.Length;

        /// <summary>
        /// Returns the associated entry block.
        /// </summary>
        public BasicBlock EntryBlock { get; private set; }

        /// <summary>
        /// Returns the current builder.
        /// </summary>
        public Builder MethodBuilder
        {
            get
            {
                Debug.Assert(builder != null, "Invalid builder");
                return builder;
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Performs an internal GC run.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void GC()
        {
            if (!HasTransformationFlags(MethodTransformationFlags.Dirty))
                return;

            var scope = CreateScope();
            foreach (var block in scope)
                block.GC();
        }

        /// <summary>
        /// Creates a new parameter mapping.
        /// </summary>
        /// <param name="arguments">The argument values.</param>
        /// <returns>The created parameter mapping.</returns>
        public ParameterMapping CreateParameterMapping(
            ImmutableArray<ValueReference> arguments)
        {
            Debug.Assert(arguments.Length == Parameters.Count, "Invalid number of arguments");
            return new ParameterMapping(this, arguments);
        }

        /// <summary>
        /// Creates a new method scope with default flags.
        /// </summary>
        /// <returns>A new method scope.</returns>
        public Scope CreateScope() => Scope.Create(this);

        /// <summary>
        /// Creates a new method scope with custom flags.
        /// </summary>
        /// <param name="scopeFlags">The scope flags.</param>
        /// <returns>A new method scope.</returns>
        public Scope CreateScope(ScopeFlags scopeFlags) => Scope.Create(this, scopeFlags);

        /// <summary>
        /// Dumps this method to the console output.
        /// </summary>
        public void DumpToConsole() =>
            Dump(Console.Out, false);

        /// <summary>
        /// Dumps this method to the console output.
        /// </summary>
        /// <param name="ignoreDeadValues">True, if dead values should be ignored.</param>
        public void DumpToConsole(bool ignoreDeadValues)
        {
            Dump(Console.Out, false);
        }

        /// <summary>
        /// Dumps this method to the given text writer.
        /// </summary>
        /// <param name="textWriter">The text writer.</param>
        /// <param name="ignoreDeadValues">True, if dead values should be ignored.</param>
        public void Dump(TextWriter textWriter, bool ignoreDeadValues)
        {
            if (textWriter == null)
                throw new ArgumentNullException(nameof(textWriter));
            var scope = CreateScope();

            textWriter.Write(ToString());
            // Dump parameters
            textWriter.Write('(');
            for (int i = 0, e = NumParameters; i < e; ++i)
            {
                textWriter.Write(Parameters[i].ToParameterString());
                if (i + 1 < e)
                    textWriter.Write(", ");
            }
            textWriter.WriteLine(')');
            // Dump blocks
            foreach (var block in scope)
                block.Dump(textWriter, ignoreDeadValues);
        }

        /// <summary>
        /// Seals the current parameters.
        /// </summary>
        /// <param name="parameterArray">The new parameters.</param>
        internal void SealParameters(ImmutableArray<Parameter> parameterArray)
        {
            Debug.Assert(parameters.IsDefaultOrEmpty, "Invalid sealing operation");
            parameters = parameterArray;
        }

        /// <summary>
        /// Creates a new builder for this function.
        /// </summary>
        /// <returns>The created builder.</returns>
        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope",
            Justification = "The created builder will be stored inside the object")]
        public Builder CreateBuilder()
        {
            var newBuilder = new Builder(this);
            if (Interlocked.CompareExchange(ref builder, newBuilder, null) != null)
                throw new InvalidOperationException();
            return newBuilder;
        }

        /// <summary>
        /// Releases the given builder.
        /// </summary>
        /// <param name="oldBuilder">The builder to release.</param>
        internal void ReleaseBuilder(Builder oldBuilder)
        {
            Debug.Assert(oldBuilder != null, "Invalid builder");
            if (Interlocked.CompareExchange(ref builder, null, oldBuilder) != oldBuilder)
                throw new InvalidOperationException();
        }

        /// <summary>
        /// Returns true if this funct4ion has the given function flags.
        /// </summary>
        /// <param name="flags">The flags to check.</param>
        /// <returns>True, if this function has the given function flags.</returns>
        public bool HasFlags(MethodFlags flags) =>
            (Flags & flags) == flags;

        /// <summary>
        /// Returns true iff this function has the given transformation flags.
        /// </summary>
        /// <param name="flags">The flags to check.</param>
        /// <returns>True, iff this function has the given transformation flags.</returns>
        public bool HasTransformationFlags(MethodTransformationFlags flags) =>
            (transformationFlags & flags) == flags;

        /// <summary>
        /// Adds the given flags to this function.
        /// </summary>
        /// <param name="flags">The flags to add.</param>
        public void AddTransformationFlags(MethodTransformationFlags flags) =>
            transformationFlags |= flags;

        /// <summary>
        /// Removes the given flags from this function.
        /// </summary>
        /// <param name="flags">The flags to remove.</param>
        public void RemoveTransformationFlags(MethodTransformationFlags flags) =>
            transformationFlags &= ~flags;

        #endregion

        #region Object

        /// <summary cref="Node.ToPrefixString"/>
        protected override string ToPrefixString() => Name;

        #endregion
    }
}
