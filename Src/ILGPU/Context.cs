﻿// -----------------------------------------------------------------------------
//                                    ILGPU
//                     Copyright (c) 2016-2017 Marcel Koester
//                                www.ilgpu.net
//
// File: Context.cs
//
// This file is part of ILGPU and is distributed under the University of
// Illinois Open Source License. See LICENSE.txt for details
// -----------------------------------------------------------------------------

using ILGPU.Backends;
using ILGPU.Compiler.DebugInformation;
using ILGPU.Compiler.Intrinsic;
using ILGPU.Util;
using LLVMSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace ILGPU
{
    /// <summary>
    /// Represents the main ILGPU context.
    /// </summary>
    /// <remarks>Members of this class are not thread safe.</remarks>
    public sealed class Context : DisposeBase
    {
        #region Static

        static Context()
        {
            NativeMethods.LoadLLVMLib();
        }

        #endregion

        #region Instance

        private int compileUnitCounter = 0;
        private readonly List<IDeviceFunctions> deviceFunctions = new List<IDeviceFunctions>(10);
        private readonly List<IDeviceTypes> deviceTypes = new List<IDeviceTypes>(10);
        private DebugInformationManager debugInformationManager = new DebugInformationManager();

        /// <summary>
        /// Constructs a new ILGPU main context
        /// </summary>
        public Context()
        {
            LLVMContext = LLVM.ContextCreate();

            OptimizeModulePassManager = LLVM.CreatePassManager();
            LLVM.AddPromoteMemoryToRegisterPass(OptimizeModulePassManager);
            LLVM.AddCFGSimplificationPass(OptimizeModulePassManager);
            LLVM.AddScalarReplAggregatesPassSSA(OptimizeModulePassManager);

            var passManagerBuilder = LLVM.PassManagerBuilderCreate();
            try
            {
                LLVM.PassManagerBuilderSetOptLevel(passManagerBuilder, 3);
                LLVM.PassManagerBuilderSetSizeLevel(passManagerBuilder, 1);

                KernelModulePassManager = LLVM.CreatePassManager();
                LLVM.AddAlwaysInlinerPass(KernelModulePassManager);
                LLVM.AddGlobalDCEPass(KernelModulePassManager);
                LLVM.AddPromoteMemoryToRegisterPass(KernelModulePassManager);
                LLVM.AddCFGSimplificationPass(KernelModulePassManager);
                LLVM.AddScalarReplAggregatesPassSSA(KernelModulePassManager);
                LLVM.PassManagerBuilderPopulateModulePassManager(passManagerBuilder, KernelModulePassManager);
            }
            finally
            {
                LLVM.PassManagerBuilderDispose(passManagerBuilder);
            }
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the main LLVM context.
        /// </summary>
        [CLSCompliant(false)]
        public LLVMContextRef LLVMContext { get; private set; }

        /// <summary>
        /// Returns an O3 optimized module-pass manager.
        /// </summary>
        internal LLVMPassManagerRef OptimizeModulePassManager { get; private set; }

        /// <summary>
        /// Returns a optimized module-pass manager for final kernel generation.
        /// </summary>
        internal LLVMPassManagerRef KernelModulePassManager { get; private set; }

        /// <summary>
        /// Returns the custom device-function handlers.
        /// </summary>
        public IReadOnlyList<IDeviceFunctions> DeviceFunctions => deviceFunctions;

        /// <summary>
        /// Returns the custom device-type handlers.
        /// </summary>
        public IReadOnlyList<IDeviceTypes> DeviceTypes => deviceTypes;

        /// <summary>
        /// Returns the encapsulated debug-information manager.
        /// </summary>
        public DebugInformationManager DebugInformationManager => debugInformationManager;

        #endregion

        #region Methods

        /// <summary>
        /// Registers the given device-function handlers.
        /// </summary>
        /// <param name="handler">The device-function handler to register.</param>
        public void RegisterDeviceFunctions(IDeviceFunctions handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            deviceFunctions.Add(handler);
        }

        /// <summary>
        /// Registers the given device-type handlers.
        /// </summary>
        /// <param name="handler">The device-type handler to register.</param>
        public void RegisterDeviceTypes(IDeviceTypes handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            deviceTypes.Add(handler);
        }

        /// <summary>
        /// Creates a new compile unit that targets the given backend.
        /// </summary>
        /// <param name="backend">The target backend.</param>
        /// <returns>The created compile unit.</returns>
        public CompileUnit CreateCompileUnit(Backend backend)
        {
            return CreateCompileUnit(backend, CompileUnitFlags.None);
        }

        /// <summary>
        /// Creates a new compile unit that targets the given backend.
        /// </summary>
        /// <param name="backend">The target backend.</param>
        /// <param name="unitFlags">The compile-unit flags.</param>
        /// <returns>The created compile unit.</returns>
        public CompileUnit CreateCompileUnit(Backend backend, CompileUnitFlags unitFlags)
        {
            return CreateCompileUnit(backend, unitFlags, $"ILGPUUnit{compileUnitCounter++}");
        }

        /// <summary>
        /// Creates a new compile unit that targets the given backend.
        /// </summary>
        /// <param name="backend">The target backend.</param>
        /// <param name="unitFlags">The compile-unit flags.</param>
        /// <param name="name">The name of the compile unit.</param>
        /// <returns>The created compile unit.</returns>
        public CompileUnit CreateCompileUnit(Backend backend, CompileUnitFlags unitFlags, string name)
        {
            if (backend == null)
                throw new ArgumentNullException(nameof(backend));
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentNullException(nameof(name));
            return new CompileUnit(this, name, backend, deviceFunctions, deviceTypes, unitFlags);
        }

        #endregion

        #region IDisposable

        /// <summary cref="DisposeBase.Dispose(bool)"/>
        [SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "debugInformationManager", Justification = "Dispose method will be invoked by a helper method")]
        protected override void Dispose(bool disposing)
        {
            if (OptimizeModulePassManager.Pointer != IntPtr.Zero)
            {
                LLVM.DisposePassManager(OptimizeModulePassManager);
                OptimizeModulePassManager = default(LLVMPassManagerRef);
            }

            if (KernelModulePassManager.Pointer != IntPtr.Zero)
            {
                LLVM.DisposePassManager(KernelModulePassManager);
                KernelModulePassManager = default(LLVMPassManagerRef);
            }

            if (LLVMContext.Pointer != IntPtr.Zero)
            {
                LLVM.ContextDispose(LLVMContext);
                LLVMContext = default(LLVMContextRef);
            }
            Dispose(ref debugInformationManager);
        }

        #endregion
    }
}
