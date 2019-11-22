﻿// -----------------------------------------------------------------------------
//                                    ILGPU
//                     Copyright (c) 2016-2019 Marcel Koester
//                                www.ilgpu.net
//
// File: CLCodeGenerator.Emitter.cs
//
// This file is part of ILGPU and is distributed under the University of
// Illinois Open Source License. See LICENSE.txt for details
// -----------------------------------------------------------------------------

using ILGPU.IR.Types;
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;

namespace ILGPU.Backends.OpenCL
{
    partial class CLCodeGenerator
    {
        #region Nested Types

        /// <summary>
        /// Generates OpenCL source statements.
        /// </summary>
        public struct StatementEmitter : IDisposable
        {
            #region Instance

            private readonly StringBuilder stringBuilder;
            private bool argMode;
            private int argumentCount;

            /// <summary>
            /// Constructs a new statement emitter using the given target.
            /// </summary>
            /// <param name="codeGenerator">The parent code generator.</param>
            /// <param name="indent">The current indentation.</param>
            internal StatementEmitter(CLCodeGenerator codeGenerator, int indent)
            {
                CodeGenerator = codeGenerator;
                stringBuilder = codeGenerator.Builder;
                argumentCount = 0;
                argMode = false;

                Indent = indent;
                stringBuilder.Append('\t', indent);
            }

            #endregion

            #region Properties

            /// <summary>
            /// Returns the associated codegenerator.
            /// </summary>
            public CLCodeGenerator CodeGenerator { get; }

            /// <summary>
            /// Returns the current indentation.
            /// </summary>
            public int Indent { get; }

            #endregion

            #region Methods

            /// <summary>
            /// Starts a target assignment.
            /// </summary>
            /// <param name="target">The target.</param>
            private void BeginAppendTarget(Variable target)
            {
                var variableType = CodeGenerator.GetVariableType(target);
                stringBuilder.Append(variableType);
                stringBuilder.Append(' ');
                stringBuilder.Append(target.ToString());
            }

            /// <summary>
            /// Appends a target.
            /// </summary>
            /// <param name="target">The target.</param>
            internal void AppendTarget(Variable target)
            {
                BeginAppendTarget(target);
                stringBuilder.Append(" = ");
            }

            /// <summary>
            /// Appends an indexer target.
            /// </summary>
            /// <param name="target">The target.</param>
            /// <param name="indexer">The indexer variable.</param>
            internal void AppendIndexedTarget(Variable target, Variable indexer)
            {
                BeginAppendTarget(target);
                AppendIndexer(indexer);
                stringBuilder.Append(" = ");
            }

            /// <summary>
            /// Appends a field target.
            /// </summary>
            /// <param name="target">The target.</param>
            /// <param name="fieldIndex">The field index.</param>
            internal void AppendFieldTarget(Variable target, int fieldIndex)
            {
                BeginAppendTarget(target);
                AppendField(fieldIndex);
                stringBuilder.Append(" = ");
            }

            /// <summary>
            /// Appends a field target via an access chain.
            /// </summary>
            /// <param name="target">The target.</param>
            /// <param name="accessChain">The field access chain.</param>
            internal void AppendFieldTarget(Variable target, ImmutableArray<int> accessChain)
            {
                BeginAppendTarget(target);
                foreach (var fieldIndex in accessChain)
                    AppendField(fieldIndex);
                stringBuilder.Append(" = ");
            }

            /// <summary>
            /// Appends an indexer.
            /// </summary>
            /// <param name="indexer">The indexer variable.</param>
            public void AppendIndexer(Variable indexer)
            {
                stringBuilder.Append('[');
                stringBuilder.Append(indexer.ToString());
                stringBuilder.Append(']');
            }

            /// <summary>
            /// Appends an unsafe pointer cast expression.
            /// </summary>
            /// <param name="typeExpression">The type expression.</param>
            public void AppendPointerCast(string typeExpression) =>
                AppendCast(typeExpression + CLInstructions.DereferenceOperation);

            /// <summary>
            /// Appends an unsafe cast expression.
            /// </summary>
            /// <param name="typeExpression">The type expression.</param>
            public void AppendCast(string typeExpression)
            {
                stringBuilder.Append('(');
                stringBuilder.Append(typeExpression);
                stringBuilder.Append(')');
            }

            /// <summary>
            /// Appends a pointer cast to an intrinsic atomic pointer type.
            /// </summary>
            /// <param name="type">The arithmetic type to use.</param>
            public void AppendAtomicCast(ArithmeticBasicValueType type)
            {
                var typeExpression = CLTypeGenerator.GetAtomicType(type);
                if (typeExpression == null)
                    return;
                AppendCast(typeExpression + CLInstructions.DereferenceOperation);
            }

            /// <summary>
            /// Appends a cast to the given basic value type.
            /// </summary>
            /// <param name="type">The target type.</param>
            public void AppendCast(BasicValueType type)
            {
                var typeExpression = CLTypeGenerator.GetBasicValueType(type);
                AppendCast(typeExpression);
            }

            /// <summary>
            /// Appends a cast to the given arithmetic basic value type.
            /// </summary>
            /// <param name="type">The target type.</param>
            public void AppendCast(ArithmeticBasicValueType type)
            {
                var typeExpression = CLTypeGenerator.GetBasicValueType(type);
                AppendCast(typeExpression);
            }

            /// <summary>
            /// Appends a cast to the given type.
            /// </summary>
            /// <param name="type">The target type.</param>
            public void AppendCast(TypeNode type)
            {
                var typeExpression = CodeGenerator.TypeGenerator[type];
                AppendCast(typeExpression);
            }

            /// <summary>
            /// Appends the given raw command.
            /// </summary>
            /// <param name="command">The command to append.</param>
            public void AppendCommand(string command)
            {
                stringBuilder.Append(' ');
                stringBuilder.Append(command);
                stringBuilder.Append(' ');
            }

            /// <summary>
            /// Tries to append a pointer-field accessor (if possible).
            /// </summary>
            /// <param name="variable">The variable.</param>
            public void TryAppendViewPointerField(Variable variable)
            {
                // Test for a view variable
                if (variable is ViewImplementationVariable viewVariable)
                {
                    // Add the field suffix
                    AppendField(viewVariable.PointerFieldIndex);
                }
                else
                {
                    // Do not append a field access in this case
                }
            }

            /// <summary>
            /// Appends the referenced field accessor.
            /// </summary>
            /// <param name="fieldIndex">The field index.</param>
            public void AppendField(int fieldIndex)
            {
                var fieldName = string.Format(
                    CLTypeGenerator.FieldNameFormat,
                    fieldIndex.ToString());
                stringBuilder.Append('.');
                stringBuilder.Append(fieldName);
            }

            /// <summary>
            /// Opens a parenthesis.
            /// </summary>
            public void OpenParen() =>
                stringBuilder.Append('(');

            /// <summary>
            /// Closes a parenthesis.
            /// </summary>
            public void CloseParen() =>
                stringBuilder.Append(')');

            /// <summary>
            /// Starts a function-call argument list.
            /// </summary>
            public void BeginArguments()
            {
                argMode = true;
                OpenParen();
            }

            /// <summary>
            /// Ends a function-call argument list.
            /// </summary>
            public void EndArguments()
            {
                CloseParen();
                argMode = false;
            }

            /// <summary>
            /// Appends code to finish an appended argument.
            /// </summary>
            public void AppendArgument()
            {
                if (!argMode)
                    stringBuilder.Append(' ');
                else
                {
                    if (argumentCount > 0)
                        stringBuilder.Append(", ");
                    ++argumentCount;
                }
            }

            /// <summary>
            /// Appends the given variable directly.
            /// </summary>
            /// <param name="variable">The variable to append.</param>
            public void Append(Variable variable)
            {
                stringBuilder.Append(variable.ToString());
            }

            /// <summary>
            /// Appends the given register argument.
            /// </summary>
            /// <param name="argument">The argument to append.</param>
            public void AppendArgument(Variable argument)
            {
                AppendArgument();
                Append(argument);
            }

            /// <summary>
            /// Append ths given operation.
            /// </summary>
            /// <param name="operation">The operation to append.</param>
            public void AppendOperation(string operation)
            {
                stringBuilder.Append(operation);
            }

            /// <summary>
            /// Appends a constant.
            /// </summary>
            /// <param name="value">The constant to append.</param>
            public void AppendConstant(long value)
            {
                AppendArgument();
                stringBuilder.Append(value);
            }

            /// <summary>
            /// Appends a constant.
            /// </summary>
            /// <param name="value">The constant to append.</param>
            [CLSCompliant(false)]
            public void AppendConstant(ulong value)
            {
                AppendArgument();
                stringBuilder.Append(value);
            }

            /// <summary>
            /// Appends a constant.
            /// </summary>
            /// <param name="value">The constant to append.</param>
            public void AppendConstant(float value)
            {
                AppendArgument();
                stringBuilder.Append(value);
            }

            /// <summary>
            /// Appends a constant.
            /// </summary>
            /// <param name="value">The constant to append.</param>
            public void AppendConstant(double value)
            {
                AppendArgument();
                stringBuilder.Append(value);
            }

            #endregion

            #region IDisposable

            /// <summary cref="IDisposable.Dispose"/>
            public void Dispose()
            {
                stringBuilder.Append(';');
                stringBuilder.AppendLine();
            }

            #endregion
        }

        #endregion

        #region Properties

        /// <summary>
        /// The current indentation level.
        /// </summary>
        public int Indent { get; private set; }

        #endregion

        #region Methods

        /// <summary>
        /// Increases the current indentation level.
        /// </summary>
        public void PushIndent()
        {
            ++Indent;
        }

        /// <summary>
        /// Decreases the current indentation level.
        /// </summary>
        public void PopIndent()
        {
            Debug.Assert(Indent > 0);
            --Indent;
        }

        /// <summary>
        /// Begins a new statement.
        /// </summary>
        /// <param name="target">The target variable to assign to.</param>
        /// <returns>The created statement emitter.</returns>
        public StatementEmitter BeginStatement(Variable target)
        {
            var emitter = new StatementEmitter(this, Indent);
            emitter.AppendTarget(target);
            return emitter;
        }

        /// <summary>
        /// Begins a new statement.
        /// </summary>
        /// <param name="target">The target variable to assign to.</param>
        /// <param name="fieldIndex">The field index to use.</param>
        /// <returns>The created statement emitter.</returns>
        public StatementEmitter BeginStatement(Variable target, int fieldIndex)
        {
            var emitter = new StatementEmitter(this, Indent);
            emitter.AppendFieldTarget(target, fieldIndex);
            return emitter;
        }

        /// <summary>
        /// Begins a new statement.
        /// </summary>
        /// <param name="target">The target variable to assign to.</param>
        /// <param name="fieldIndices">The field indices to use.</param>
        /// <returns>The created statement emitter.</returns>
        public StatementEmitter BeginStatement(Variable target, ImmutableArray<int> fieldIndices)
        {
            var emitter = new StatementEmitter(this, Indent);
            emitter.AppendFieldTarget(target, fieldIndices);
            return emitter;
        }

        /// <summary>
        /// Begins a new statement.
        /// </summary>
        /// <param name="target">The target variable to assign to.</param>
        /// <param name="indexer">The indexer variable to use.</param>
        /// <returns>The created statement emitter.</returns>
        public StatementEmitter BeginStatement(Variable target, Variable indexer)
        {
            var emitter = new StatementEmitter(this, Indent);
            emitter.AppendIndexedTarget(target, indexer);
            return emitter;
        }

        /// <summary>
        /// Begins a new statement.
        /// </summary>
        /// <param name="target">The target variable to assign to.</param>
        /// <param name="command">The initial command to emit.</param>
        /// <returns>The created statement emitter.</returns>
        public StatementEmitter BeginStatement(Variable target, string command)
        {
            var emitter = BeginStatement(target);
            emitter.AppendCommand(command);
            return emitter;
        }

        /// <summary>
        /// Begins a new statement.
        /// </summary>
        /// <param name="command">The initial command to emit.</param>
        /// <returns>The created statement emitter.</returns>
        public StatementEmitter BeginStatement(string command)
        {
            var emitter = new StatementEmitter(this, Indent);
            emitter.AppendCommand(command);
            return emitter;
        }

        #endregion
    }
}
