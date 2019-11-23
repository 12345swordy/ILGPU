﻿// -----------------------------------------------------------------------------
//                                    ILGPU
//                     Copyright (c) 2016-2019 Marcel Koester
//                                www.ilgpu.net
//
// File: CLCodeGenerator.cs
//
// This file is part of ILGPU and is distributed under the University of
// Illinois Open Source License. See LICENSE.txt for details
// -----------------------------------------------------------------------------

using ILGPU.Backends.EntryPoints;
using ILGPU.IR;
using ILGPU.IR.Analyses;
using ILGPU.IR.Intrinsics;
using ILGPU.IR.Values;
using System;
using System.Collections.Generic;
using System.Text;

namespace ILGPU.Backends.OpenCL
{
    /// <summary>
    /// Generates OpenCL source code out of IR values.
    /// </summary>
    /// <remarks>The code needs to be prepared for this code generator.</remarks>
    public abstract partial class CLCodeGenerator :
        CLVariableAllocator,
        IValueVisitor,
        IBackendCodeGenerator<StringBuilder>
    {
        #region Nested Types

        /// <summary>
        /// Generation arguments for code-generator construction.
        /// </summary>
        public readonly struct GeneratorArgs
        {
            internal GeneratorArgs(
                CLBackend backend,
                CLTypeGenerator typeGenerator,
                SeparateViewEntryPoint entryPoint,
                ABI abi)
            {
                Backend = backend;
                TypeGenerator = typeGenerator;
                EntryPoint = entryPoint;
                ABI = abi;
            }

            /// <summary>
            /// Returns the underlying backend.
            /// </summary>
            public CLBackend Backend { get; }

            /// <summary>
            /// Returns the associated type generator.
            /// </summary>
            public CLTypeGenerator TypeGenerator { get; }

            /// <summary>
            /// Returns the current entry point.
            /// </summary>
            public SeparateViewEntryPoint EntryPoint { get; }

            /// <summary>
            /// Returns the associated ABI.
            /// </summary>
            public ABI ABI { get; }
        }

        /// <summary>
        /// Represents a parameter that is mapped to OpenCL.
        /// </summary>
        protected internal readonly struct MappedParameter
        {
            #region Instance

            /// <summary>
            /// Constructs a new mapped parameter.
            /// </summary>
            /// <param name="variable">The OpenCL variable.</param>
            /// <param name="clName">The name of the parameter in OpenCL code.</param>
            /// <param name="parameter">The source parameter.</param>
            public MappedParameter(
                Variable variable,
                string clName,
                Parameter parameter)
            {
                Variable = variable;
                CLName = clName;
                Parameter = parameter;
            }

            #endregion

            #region Properties

            /// <summary>
            /// Returns the associated OpenCL variable.
            /// </summary>
            public Variable Variable { get; }

            /// <summary>
            /// Returns the name of the parameter in OpenCL code.
            /// </summary>
            public string CLName { get; }

            /// <summary>
            /// Returns the source parameter.
            /// </summary>
            public Parameter Parameter { get; }

            #endregion
        }

        #endregion

        #region Static

        /// <summary>
        /// Returns the OpenCL function name for the given function.
        /// </summary>
        /// <param name="method">The method.</param>
        /// <returns>The resolved OpenCL function name.</returns>
        protected static string GetMethodName(Method method)
        {
            var handleName = method.Handle.Name;
            if (method.HasFlags(MethodFlags.External))
                return handleName;
            return handleName + "_" + method.Id;
        }

        /// <summary>
        /// Returns the OpenCL parameter name for the given parameter.
        /// </summary>
        /// <param name="parameter">The parameter.</param>
        /// <returns>The resolved OpenCL parameter name.</returns>
        protected static string GetParameterName(Parameter parameter) =>
            "_" + parameter.Name + "_" + parameter.Id.ToString();

        #endregion

        #region Instance

        private int labelCounter = 0;
        private readonly Dictionary<BasicBlock, string> blockLookup =
            new Dictionary<BasicBlock, string>();
        private readonly string labelPrefix;

        /// <summary>
        /// Constructs a new code generator.
        /// </summary>
        /// <param name="args">The generator arguments.</param>
        /// <param name="scope">The current scope.</param>
        /// <param name="allocas">All local allocas.</param>
        internal CLCodeGenerator(in GeneratorArgs args, Scope scope, Allocas allocas)
            : base(args.TypeGenerator)
        {
            Backend = args.Backend;
            Scope = scope;
            ImplementationProvider = Backend.IntrinsicProvider;
            Allocas = allocas;
            ABI = args.ABI;

            labelPrefix = "L_" + Method.Id.ToString();

            Builder = new StringBuilder();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the associated backend.
        /// </summary>
        public CLBackend Backend { get; }

        /// <summary>
        /// Returns the associated method.
        /// </summary>
        public Method Method => Scope.Method;

        /// <summary>
        /// Returns the current function scope.
        /// </summary>
        public Scope Scope { get; }

        /// <summary>
        /// Returns all local allocas.
        /// </summary>
        public Allocas Allocas { get; }

        /// <summary>
        /// Returns the current ABI.
        /// </summary>
        public ABI ABI { get; }

        /// <summary>
        /// Returns the current intrinsic provider for code-generation purposes.
        /// </summary>
        public IntrinsicImplementationProvider<CLIntrinsic.Handler> ImplementationProvider { get; }

        /// <summary>
        /// Returns the associated string builder.
        /// </summary>
        public StringBuilder Builder { get; }

        #endregion

        #region IBackendCodeGenerator

        /// <summary>
        /// Generates a function declaration in PTX code.
        /// </summary>
        public abstract void GenerateHeader(StringBuilder builder);

        /// <summary>
        /// Generates PTX code.
        /// </summary>
        public abstract void GenerateCode();

        /// <summary>
        /// Generates PTX constant declarations.
        /// </summary>
        /// <param name="builder">The target builder.</param>
        public void GenerateConstants(StringBuilder builder)
        {
            // No constants to emit
        }

        /// <summary cref="IBackendCodeGenerator{TKernelBuilder}.Merge(TKernelBuilder)"/>
        public void Merge(StringBuilder builder)
        {
            builder.Append(Builder.ToString());
        }

        #endregion

        #region General Code Generation

        /// <summary>
        /// Declares a new label.
        /// </summary>
        /// <returns>The declared label.</returns>
        private string DeclareLabel() => labelPrefix + labelCounter++;

        /// <summary>
        /// Marks the given label.
        /// </summary>
        /// <param name="label">The label to mark.</param>
        protected void MarkLabel(string label)
        {
            Builder.Append('\t');
            Builder.Append(label);
            Builder.AppendLine(": ;");
        }

        /// <summary>
        /// Generates parameter declarations by writing them to the
        /// target builder provided.
        /// </summary>
        /// <param name="target">The target builder to use.</param>
        /// <param name="paramOffset">The intrinsic parameter offset.</param>
        protected void GenerateParameters(StringBuilder target, int paramOffset)
        {
            for (int i = paramOffset, e = Method.NumParameters; i < e; ++i)
            {
                var param = Method.Parameters[i];
                Builder.Append('\t');
                Builder.Append(TypeGenerator[param.Type]);
                Builder.Append(' ');
                var variable = Allocate(param);
                Builder.Append(variable.VariableName);

                if (i + 1 < e)
                    Builder.AppendLine(",");
            }
        }

        /// <summary>
        /// Generates code for all basic blocks.
        /// </summary>
        protected void GenerateCodeInternal()
        {
            // Build branch targets
            foreach (var block in Scope)
                blockLookup.Add(block, DeclareLabel());

            // Find all phi nodes, allocate target registers and setup internal mapping
            var cfg = Scope.CreateCFG();
            var phiMapping = new Dictionary<BasicBlock, List<Variable>>(cfg.Count);
            var dominators = Dominators.Create(cfg);
            foreach (var node in cfg)
            {
                var phis = Phis.Create(node.Block);

                // Allocate all phis nodes and store them in the associated dominator
                foreach (var phi in phis)
                {
                    var targetNode = node;
                    foreach (var argument in phi)
                    {
                        targetNode = dominators.GetImmediateCommonDominator(
                            targetNode,
                            cfg[argument.BasicBlock]);
                    }

                    var variable = Allocate(phi);
                    if (!phiMapping.TryGetValue(targetNode.Block, out var phiVariables))
                    {
                        phiVariables = new List<Variable>();
                        phiMapping.Add(targetNode.Block, phiVariables);
                    }
                    phiVariables.Add(variable);
                }
            }

            // Generate code
            foreach (var block in Scope)
            {
                // Mark block label
                MarkLabel(blockLookup[block]);

                // Declare phi variables (if any)
                if (phiMapping.TryGetValue(block, out var phiVariables))
                {
                    foreach (var phiVariable in phiVariables)
                    {
                        // DeclareVariable(phiVariable);
                        using (var statement = BeginStatement(phiVariable)) { }
                    }
                }

                foreach (var value in block)
                {
                    // Check for intrinsic implementation
                    if (ImplementationProvider.TryGetCodeGenerator(
                        value,
                        out var intrinsicCodeGenerator))
                    {
                        // Generate specialized code for this intrinsic node
                        intrinsicCodeGenerator(Backend, this, value);
                    }
                    else
                    {
                        // Emit value
                        value.Accept(this);
                    }
                }

                // Build terminator
                block.Terminator.Accept(this);
                Builder.AppendLine();
            }
        }

        #endregion
    }
}
