﻿// -----------------------------------------------------------------------------
//                                    ILGPU
//                     Copyright (c) 2016-2017 Marcel Koester
//                                www.ilgpu.net
//
// File: CudaMemoryBuffer.cs
//
// This file is part of ILGPU and is distributed under the University of
// Illinois Open Source License. See LICENSE.txt for details
// -----------------------------------------------------------------------------

using ILGPU.Resources;
using ILGPU.Util;
using System;

namespace ILGPU.Runtime.Cuda
{
    /// <summary>
    /// Represents an unmanaged Cuda buffer.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TIndex">The index type.</typeparam>
    public sealed class CudaMemoryBuffer<T, TIndex> : MemoryBuffer<T, TIndex>
        where T : struct
        where TIndex : struct, IIndex, IGenericIndex<TIndex>
    {
        #region Instance

        /// <summary>
        /// Constructs a new cuda buffer.
        /// </summary>
        /// <param name="accelerator">The accelerator.</param>
        /// <param name="extent">The extent.</param>
        internal CudaMemoryBuffer(CudaAccelerator accelerator, TIndex extent)
            : base(accelerator, extent)
        {
            CudaException.ThrowIfFailed(
                CudaNativeMethods.cuMemAlloc_v2(
                    out IntPtr resultPtr,
                    new IntPtr(extent.Size * ElementSize)));
            Pointer = resultPtr;
        }

        #endregion

        #region Methods

        /// <summary cref="MemoryBuffer{T, TIndex}.CopyToViewInternal(ArrayView{T, Index}, AcceleratorType, TIndex, AcceleratorStream)"/>
        protected internal override void CopyToViewInternal(
            ArrayView<T, Index> target,
            AcceleratorType acceleratorType,
            TIndex sourceOffset,
            AcceleratorStream stream)
        {
            switch (acceleratorType)
            {
                case AcceleratorType.CPU:
                    CudaException.ThrowIfFailed(CudaNativeMethods.cuMemcpyDtoH(
                        target.Pointer,
                        GetSubView(sourceOffset).Pointer,
                        new IntPtr(target.LengthInBytes),
                        stream));
                    break;
                case AcceleratorType.Cuda:
                    CudaException.ThrowIfFailed(CudaNativeMethods.cuMemcpyDtoD(
                        target.Pointer,
                        GetSubView(sourceOffset).Pointer,
                        new IntPtr(target.LengthInBytes),
                        stream));
                    break;
                default:
                    throw new NotSupportedException(RuntimeErrorMessages.NotSupportedTargetAccelerator);
            }
        }

        /// <summary cref="MemoryBuffer{T, TIndex}.CopyFromViewInternal(ArrayView{T, Index}, AcceleratorType, TIndex, AcceleratorStream)"/>
        protected internal override void CopyFromViewInternal(
            ArrayView<T, Index> source,
            AcceleratorType acceleratorType,
            TIndex targetOffset,
            AcceleratorStream stream)
        {
            switch (acceleratorType)
            {
                case AcceleratorType.CPU:
                    CudaException.ThrowIfFailed(CudaNativeMethods.cuMemcpyHtoD(
                        GetSubView(targetOffset).Pointer,
                        source.Pointer,
                        new IntPtr(source.LengthInBytes),
                        stream));
                    break;
                case AcceleratorType.Cuda:
                    CudaException.ThrowIfFailed(CudaNativeMethods.cuMemcpyDtoD(
                        GetSubView(targetOffset).Pointer,
                        source.Pointer,
                        new IntPtr(source.LengthInBytes),
                        stream));
                    break;
                default:
                    throw new NotSupportedException(RuntimeErrorMessages.NotSupportedTargetAccelerator);
            }
        }

        /// <summary cref="MemoryBuffer.MemSetToZero(AcceleratorStream)"/>
        public override void MemSetToZero(AcceleratorStream stream)
        {
            CudaNativeMethods.cuMemsetD8_v2(Pointer, 0, new IntPtr(LengthInBytes));
        }

        #endregion

        #region IDisposable

        /// <summary cref="DisposeBase.Dispose(bool)"/>
        protected override void Dispose(bool disposing)
        {
            if (Pointer == IntPtr.Zero)
                return;

            CudaException.ThrowIfFailed(CudaNativeMethods.cuMemFree_v2(Pointer));
            Pointer = IntPtr.Zero;
        }

        #endregion
    }
}
