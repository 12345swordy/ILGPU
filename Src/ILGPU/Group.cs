﻿// -----------------------------------------------------------------------------
//                                    ILGPU
//                     Copyright (c) 2016-2017 Marcel Koester
//                                www.ilgpu.net
//
// File: Group.cs
//
// This file is part of ILGPU and is distributed under the University of
// Illinois Open Source License. See LICENSE.txt for details
// -----------------------------------------------------------------------------

using ILGPU.Compiler.Intrinsic;
using ILGPU.Runtime.CPU;

namespace ILGPU
{
    /// <summary>
    /// Contains general grid functions.
    /// </summary>
    public static class Group
    {
        #region Properties

        /// <summary>
        /// Returns the dimension of the number of threads per group per grid element
        /// in the scheduled thread grid.
        /// </summary>
        /// <returns>The thread dimension for a single group.</returns>
        public static Index3 Dimension
        {
            [GridIntrinsic(GridIntrinsicKind.GetGroupDimension)]
            get;
            internal set;
        }

        #endregion

        #region Barriers

        /// <summary>
        /// Executes a thread barrier.
        /// </summary>
        [GroupIntrinsic(GroupIntrinsicKind.Barrier)]
        public static void Barrier()
        {
            CPURuntimeGroupContext.Current.Barrier();
        }

        /// <summary>
        /// Executes a thread barrier and returns the number of threads for which
        /// the predicate evaluated to true.
        /// </summary>
        /// <param name="predicate">The predicate to check.</param>
        /// <returns>The number of threads for which the predicate evaluated to true.</returns>
        [GroupIntrinsic(GroupIntrinsicKind.BarrierPopCount)]
        public static int BarrierPopCount(bool predicate)
        {
            return CPURuntimeGroupContext.Current.BarrierPopCount(predicate);
        }

        /// <summary>
        /// Executes a thread barrier and returns true iff all threads in a block
        /// fullfills the predicate.
        /// </summary>
        /// <param name="predicate">The predicate to check.</param>
        /// <returns>True, iff all threads in a block fullfills the predicate.</returns>
        [GroupIntrinsic(GroupIntrinsicKind.BarrierAnd)]
        public static bool BarrierAnd(bool predicate)
        {
            return CPURuntimeGroupContext.Current.BarrierAnd(predicate);
        }

        /// <summary>
        /// Executes a thread barrier and returns true iff any thread in a block
        /// fullfills the predicate.
        /// </summary>
        /// <param name="predicate">The predicate to check.</param>
        /// <returns>True, iff any thread in a block fullfills the predicate.</returns>
        [GroupIntrinsic(GroupIntrinsicKind.BarrierOr)]
        public static bool BarrierOr(bool predicate)
        {
            return CPURuntimeGroupContext.Current.BarrierOr(predicate);
        }

        #endregion
    }
}
