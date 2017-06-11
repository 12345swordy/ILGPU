﻿// -----------------------------------------------------------------------------
//                                    ILGPU
//                     Copyright (c) 2016-2017 Marcel Koester
//                                www.ilgpu.net
//
// File: CompiledKernel.cs
//
// This file is part of ILGPU and is distributed under the University of
// Illinois Open Source License. See LICENSE.txt for details
// -----------------------------------------------------------------------------

using ILGPU.Backends;
using System.Reflection;

namespace ILGPU.Compiler
{
    /// <summary>
    /// Represents a compiled kernel that encapsulates
    /// the emitted binary code.
    /// </summary>
    public sealed class CompiledKernel
    {
        #region Instance

        private readonly byte[] buffer;

        /// <summary>
        /// Constructs a new compiled kernel.
        /// </summary>
        /// <param name="context">The associated context.</param>
        /// <param name="sourceMethod">The source method.</param>
        /// <param name="buffer">The binary buffer.</param>
        /// <param name="entryName">The entry name.</param>
        /// <param name="entryPoint">The entry point.</param>
        internal CompiledKernel(
            Context context,
            MethodInfo sourceMethod,
            byte[] buffer,
            string entryName,
            EntryPoint entryPoint)
        {
            Context = context;
            SourceMethod = sourceMethod;
            this.buffer = buffer;
            EntryName = entryName;
            EntryPoint = entryPoint;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the associated context.
        /// </summary>
        public Context Context { get; }

        /// <summary>
        /// Represents the source method.
        /// </summary>
        public MethodInfo SourceMethod { get; }

        /// <summary>
        /// Returns the name of the entry point.
        /// </summary>
        public string EntryName { get; }

        /// <summary>
        /// Returns the index type of the entry point.
        /// </summary>
        public IndexType IndexType => EntryPoint.Type;

        /// <summary>
        /// Returns the internally used entry point.
        /// </summary>
        internal EntryPoint EntryPoint { get; }

        #endregion

        #region Methods

        /// <summary>
        /// Returns the internal buffer that contains the
        /// emitted binary code.
        /// </summary>
        public byte[] GetBuffer()
        {
            return buffer;
        }

        #endregion

        #region Object

        /// <summary>
        /// Returns true iff the given object is equal to the current kernel.
        /// </summary>
        /// <param name="obj">The other object.</param>
        /// <returns>True, iff the given object is equal to the current kernel.</returns>
        public override bool Equals(object obj)
        {
            var other = obj as CompiledKernel;
            if (other == null)
                return false;
            if (Context != other.Context ||
                SourceMethod != other.SourceMethod ||
                EntryName != other.EntryName ||
                buffer.Length != other.buffer.Length)
                return false;
            for (int i = 0, e = buffer.Length; i < e; ++i)
            {
                if (buffer[i] != other.buffer[i])
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Returns the hash code of this kernel.
        /// </summary>
        /// <returns>The hash code of this kernel.</returns>
        public override int GetHashCode()
        {
            return Context.GetHashCode() ^
                SourceMethod.GetHashCode() ^
                EntryName.GetHashCode() ^
                buffer.Length.GetHashCode();
        }

        /// <summary>
        /// Returns the string representation of this kernel.
        /// </summary>
        /// <returns>The string representation of this kernel.</returns>
        public override string ToString()
        {
            return $"{EntryName}[Length: {buffer.Length}]";
        }

        #endregion
    }
}
