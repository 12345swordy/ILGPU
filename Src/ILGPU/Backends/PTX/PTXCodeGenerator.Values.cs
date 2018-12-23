﻿// -----------------------------------------------------------------------------
//                                    ILGPU
//                     Copyright (c) 2016-2018 Marcel Koester
//                                www.ilgpu.net
//
// File: PTXCodeGenerator.Values.cs
//
// This file is part of ILGPU and is distributed under the University of
// Illinois Open Source License. See LICENSE.txt for details
// -----------------------------------------------------------------------------

using ILGPU.IR;
using ILGPU.IR.Types;
using ILGPU.IR.Values;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ILGPU.Backends.PTX
{
    partial class PTXCodeGenerator
    {
        /// <summary cref="IValueVisitor.Visit(MethodCall)"/>
        public void Visit(MethodCall call)
        {
            const string ReturnValueName = "callRetVal";
            const string CallParamName = "callParam";

            var target = call.Target;

            // Create call sequence
            Builder.AppendLine();
            Builder.AppendLine("\t{");

            Builder.AppendLine("\t.reg .b32 temp_param_reg;");

            for (int i = 0, e = call.NumArguments; i < e; ++i)
            {
                var argument = call.Nodes[i];
                var paramName = CallParamName + i;
                Builder.Append("\t");
                AppendParamDeclaration(argument.Type, paramName);
                Builder.AppendLine(";");

                // Emit store param command
                var argumentRegister = Load(argument);
                EmitStoreParam(paramName, argumentRegister);
            }

            // Reserve a sufficient amount of memory
            var returnType = target.ReturnType;
            if (!returnType.IsVoidType)
            {
                Builder.Append("\t");
                AppendParamDeclaration(returnType, ReturnValueName);
                Builder.AppendLine(";");
                Builder.Append("\tcall ");
                Builder.Append("(");
                Builder.Append(ReturnValueName);
                Builder.Append("), ");
            }
            else
            {
                Builder.Append("\tcall ");
            }
            Builder.Append(GetMethodName(target));
            Builder.AppendLine(", (");
            for (int i = 0, e = call.NumArguments; i < e; ++i)
            {
                Builder.Append("\t\t");
                Builder.Append(CallParamName);
                Builder.Append(i);
                if (i + 1 < e)
                    Builder.AppendLine(",");
                else
                    Builder.AppendLine();
            }
            Builder.AppendLine("\t);");

            if (!returnType.IsVoidType)
            {
                // Allocate target register for the return type and load the data
                var returnRegister = Allocate(call);
                EmitLoadParam(ReturnValueName, returnRegister);
            }
            Builder.AppendLine("\t}");
            Builder.AppendLine();
        }

        /// <summary cref="IValueVisitor.Visit(Parameter)"/>
        public void Visit(Parameter parameter)
        {
            // Parameters are already assigned to registers
#if DEBUG
            Load(parameter);
#endif
        }

        /// <summary cref="IValueVisitor.Visit(PhiValue)"/>
        public void Visit(PhiValue phiValue)
        {
            // Phi values are already assigned to registers
#if DEBUG
            Load(phiValue);
#endif
        }

        /// <summary cref="IValueVisitor.Visit(UnaryArithmeticValue)"/>
        public void Visit(UnaryArithmeticValue value)
        {
            var argument = LoadPrimitive(value.Value);
            var ptxType = PTXType.GetPTXType(value.BasicValueType);
            var targetRegister = Allocate(value, ptxType.RegisterKind);

            var commandString = Instructions.GetArithmeticOperation(
                value.Kind,
                value.ArithmeticBasicValueType,
                FastMath);
            switch (value.Kind)
            {
                case UnaryArithmeticKind.IsInfF:
                case UnaryArithmeticKind.IsNaNF:
                    using (var predicateScope = new PredicateScope(this))
                    {
                        using (var command = BeginCommand(commandString))
                        {
                            command.AppendArgument(predicateScope.PredicateRegister);
                            command.AppendArgument(argument);
                        }

                        predicateScope.ConvertToValue(this, targetRegister);
                    }
                    break;
                default:
                    using (var command = BeginCommand(commandString))
                    {
                        command.AppendArgument(targetRegister);
                        command.AppendArgument(argument);
                    }
                    break;
            }
        }

        /// <summary cref="IValueVisitor.Visit(BinaryArithmeticValue)"/>
        public void Visit(BinaryArithmeticValue value)
        {
            var left = LoadPrimitive(value.Left);
            var right = LoadPrimitive(value.Right);

            var targetRegister = Allocate(value, left.Kind);
            using (var command = BeginCommand(
                Instructions.GetArithmeticOperation(
                    value.Kind,
                    value.ArithmeticBasicValueType,
                    FastMath)))
            {
                command.AppendArgument(targetRegister);
                command.AppendArgument(left);
                command.AppendArgument(right);
            }
        }

        /// <summary cref="IValueVisitor.Visit(TernaryArithmeticValue)"/>
        public void Visit(TernaryArithmeticValue value)
        {
            var first = LoadPrimitive(value.First);
            var second = LoadPrimitive(value.Second);
            var third = LoadPrimitive(value.Third);


            var targetRegister = Allocate(value, first.Kind);
            using (var command = BeginCommand(
                Instructions.GetArithmeticOperation(
                    value.Kind,
                    value.ArithmeticBasicValueType)))
            {
                command.AppendArgument(targetRegister);
                command.AppendArgument(first);
                command.AppendArgument(second);
                command.AppendArgument(third);
            }
        }

        /// <summary cref="IValueVisitor.Visit(CompareValue)"/>
        public void Visit(CompareValue value)
        {
            var left = LoadPrimitive(value.Left);
            var right = LoadPrimitive(value.Right);

            var targetRegister = Allocate(value) as PrimitiveRegister;
            using (var predicateScope = new PredicateScope(this))
            {
                using (var command = BeginCommand(
                    Instructions.GetCompareOperation(
                        value.Kind,
                        value.CompareType)))
                {
                    command.AppendArgument(predicateScope.PredicateRegister);
                    command.AppendArgument(left);
                    command.AppendArgument(right);
                }

                predicateScope.ConvertToValue(this, targetRegister);
            }
        }

        /// <summary cref="IValueVisitor.Visit(ConvertValue)"/>
        public void Visit(ConvertValue value)
        {
            var sourceValue = LoadPrimitive(value.Value);

            var convertOperation = Instructions.GetConvertOperation(
                value.SourceType,
                value.TargetType);

            var ptxType = PTXType.GetPTXType(value.Type, ABI);
            var targetRegister = Allocate(value, ptxType.RegisterKind);
            using (var command = BeginCommand(convertOperation))
            {
                command.AppendArgument(targetRegister);
                command.AppendArgument(sourceValue);
            }
        }

        /// <summary cref="IValueVisitor.Visit(PointerCast)"/>
        public void Visit(PointerCast value)
        {
            Alias(value, value.Value);
        }

        /// <summary cref="IValueVisitor.Visit(FloatAsIntCast)"/>
        public void Visit(FloatAsIntCast value)
        {
            var source = LoadPrimitive(value.Value);
            Debug.Assert(
                source.Kind == PTXRegisterKind.Float32 ||
                source.Kind == PTXRegisterKind.Float64);

            var registerType = PTXType.GetPTXType(value.BasicValueType);
            var targetRegister = Allocate(value, registerType.RegisterKind);
            Debug.Assert(
                targetRegister.Kind == PTXRegisterKind.Int32 ||
                targetRegister.Kind == PTXRegisterKind.Int64);

            Move(source, targetRegister);
        }

        /// <summary cref="IValueVisitor.Visit(IntAsFloatCast)"/>
        public void Visit(IntAsFloatCast value)
        {
            var source = LoadPrimitive(value.Value);
            Debug.Assert(
                source.Kind == PTXRegisterKind.Int32 ||
                source.Kind == PTXRegisterKind.Int64);

            var registerType = PTXType.GetPTXType(value.BasicValueType);
            var targetRegister = Allocate(value, registerType.RegisterKind);
            Debug.Assert(
                targetRegister.Kind == PTXRegisterKind.Float32 ||
                targetRegister.Kind == PTXRegisterKind.Float64);

            Move(source, targetRegister);
        }

        /// <summary>
        /// Emits complex predicate instructions.
        /// </summary>
        private readonly struct PredicateEmitter : IComplexCommandEmitter
        {
            public PredicateEmitter(PrimitiveRegister predicateRegister)
            {
                PredicateRegister = predicateRegister;
            }

            /// <summary>
            /// The current source type.
            /// </summary>
            public PrimitiveRegister PredicateRegister { get; }

            /// <summary cref="IComplexCommandEmitter.Emit(CommandEmitter, RegisterAllocator{PTXRegisterKind}.PrimitiveRegister[])"/>
            public void Emit(CommandEmitter commandEmitter, PrimitiveRegister[] registers)
            {
                commandEmitter.AppendArgument(registers[0]);
                commandEmitter.AppendArgument(registers[1]);
                commandEmitter.AppendArgument(registers[2]);
                commandEmitter.AppendArgument(PredicateRegister);
            }
        }

        /// <summary cref="IValueVisitor.Visit(Predicate)"/>
        public void Visit(Predicate predicate)
        {
            var condition = LoadPrimitive(predicate.Condition);
            var trueValue = Load(predicate.TrueValue);
            var falseValue = Load(predicate.FalseValue);

            using (var predicateScope = ConvertToPredicateScope(condition))
            {
                var targetRegister = Allocate(predicate);
                EmitComplexCommand(
                    Instructions.GetSelectValueOperation(predicate.BasicValueType),
                    new PredicateEmitter(predicateScope.PredicateRegister),
                    targetRegister,
                    trueValue,
                    falseValue);
            }
        }

        /// <summary cref="IValueVisitor.Visit(GenericAtomic)"/>
        public void Visit(GenericAtomic atomic)
        {
            var target = LoadPrimitive(atomic.Target);
            var value = LoadPrimitive(atomic.Value);

            var requiresResult = atomic.Uses.HasAny || atomic.Kind == AtomicKind.Exchange;
            var atomicOperation = Instructions.GetAtomicOperation(
                atomic.Kind,
                requiresResult);
            var type = Instructions.GetAtomicOperationPostfix(
                atomic.Kind,
                atomic.ArithmeticBasicValueType);

            var ptxType = PTXType.GetPTXType(atomic.BasicValueType);
            var targetRegister = requiresResult ? Allocate(atomic, ptxType.RegisterKind) : default;
            using (var command = BeginCommand(atomicOperation))
            {
                command.AppendNonLocalAddressSpace(
                    (atomic.Target.Type as AddressSpaceType).AddressSpace);
                command.AppendPostFix(type);
                if (requiresResult)
                    command.AppendArgument(targetRegister);
                command.AppendArgumentValue(target);
                command.AppendArgument(value);
            }
        }

        /// <summary cref="IValueVisitor.Visit(AtomicCAS)"/>
        public void Visit(AtomicCAS atomicCAS)
        {
            var target = LoadPrimitive(atomicCAS.Target);
            var value = LoadPrimitive(atomicCAS.Value);
            var compare = LoadPrimitive(atomicCAS.CompareValue);

            var type = PTXType.GetPTXType(atomicCAS.BasicValueType);
            var targetRegister = Allocate(atomicCAS, type.RegisterKind);

            using (var command = BeginCommand(Instructions.AtomicCASOperation))
            {
                command.AppendNonLocalAddressSpace(
                    (atomicCAS.Target.Type as AddressSpaceType).AddressSpace);
                command.AppendPostFix(type);
                command.AppendArgument(targetRegister);
                command.AppendArgumentValue(target);
                command.AppendArgument(value);
                command.AppendArgument(compare);
            }
        }

        /// <summary cref="IValueVisitor.Visit(Alloca)"/>
        public void Visit(Alloca alloca)
        {
            // Ignore alloca
        }

        /// <summary cref="IValueVisitor.Visit(MemoryBarrier)"/>
        public void Visit(MemoryBarrier barrier)
        {
            var command = Instructions.GetMemoryBarrier(barrier.Kind);
            Command(command, null);
        }

        /// <summary>
        /// Emits complex load instructions.
        /// </summary>
        private readonly struct LoadEmitter : IComplexCommandEmitterWithOffsets
        {
            public LoadEmitter(
                PointerType sourceType,
                PrimitiveRegister addressRegister)
            {
                SourceType = sourceType;
                AddressRegister = addressRegister;
            }

            /// <summary>
            /// The current source type.
            /// </summary>
            public PointerType SourceType { get; }

            /// <summary>
            /// Returns the associated address register.
            /// </summary>
            public PrimitiveRegister AddressRegister { get; }

            /// <summary cref="IComplexCommandEmitterWithOffsets.Emit(CommandEmitter, RegisterAllocator{PTXRegisterKind}.PrimitiveRegister, int)"/>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Emit(CommandEmitter commandEmitter, PrimitiveRegister register, int offset)
            {
                var type = PTXType.GetPTXType(register.Kind);

                commandEmitter.AppendAddressSpace(SourceType.AddressSpace);
                commandEmitter.AppendPostFix(type);
                commandEmitter.AppendArgument(register);
                commandEmitter.AppendArgumentValue(AddressRegister, offset);
            }
        }

        /// <summary cref="IValueVisitor.Visit(Load)"/>
        public void Visit(Load load)
        {
            var address = LoadPrimitive(load.Source);
            var sourceType = load.Source.Type as PointerType;
            var targetRegister = Allocate(load);

            EmitComplexCommandWithOffsets(
                Instructions.LoadOperation,
                new LoadEmitter(sourceType, address),
                targetRegister);
        }

        /// <summary>
        /// Emits complex store instructions.
        /// </summary>
        private readonly struct StoreEmitter : IComplexCommandEmitterWithOffsets
        {
            public StoreEmitter(
                PointerType targetType,
                PrimitiveRegister addressRegister)
            {
                TargetType = targetType;
                AddressRegister = addressRegister;
            }

            /// <summary>
            /// The current source type.
            /// </summary>
            public PointerType TargetType { get; }

            /// <summary>
            /// Returns the associated address register.
            /// </summary>
            public PrimitiveRegister AddressRegister { get; }

            /// <summary cref="IComplexCommandEmitterWithOffsets.Emit(CommandEmitter, RegisterAllocator{PTXRegisterKind}.PrimitiveRegister, int)"/>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Emit(CommandEmitter commandEmitter, PrimitiveRegister register, int offset)
            {
                var type = PTXType.GetPTXType(register.Kind);

                commandEmitter.AppendAddressSpace(TargetType.AddressSpace);
                commandEmitter.AppendPostFix(type);
                commandEmitter.AppendArgumentValue(AddressRegister, offset);
                commandEmitter.AppendArgument(register);
            }
        }

        /// <summary cref="IValueVisitor.Visit(Store)"/>
        public void Visit(Store store)
        {
            var address = LoadPrimitive(store.Target);
            var targetType = store.Target.Type as PointerType;
            var value = Load(store.Value);

            EmitComplexCommandWithOffsets(
                Instructions.StoreOperation,
                new StoreEmitter(targetType, address),
                value);
        }

        /// <summary cref="IValueVisitor.Visit(LoadFieldAddress)"/>
        public void Visit(LoadFieldAddress value)
        {
            var source = LoadPrimitive(value.Source);
            var fieldOffset = ABI.GetOffsetOf(value.StructureType, value.FieldIndex);

            if (fieldOffset != 0)
            {
                var targetRegister = AllocatePlatformRegister(value, out PTXType _);
                using (var command = BeginCommand(
                    Instructions.GetArithmeticOperation(
                        BinaryArithmeticKind.Add,
                        ABI.PointerArithmeticType,
                        false)))
                {
                    command.AppendArgument(targetRegister);
                    command.AppendArgument(source);
                    command.AppendConstant(fieldOffset);
                }
            }
            else
                Alias(value, value.Source);
        }

        /// <summary cref="IValueVisitor.Visit(PrimitiveValue)"/>
        public void Visit(PrimitiveValue value)
        {
            if (value.Uses.TryGetSingleUse(out Use use) && use.Resolve() is Alloca)
                return;

            var basicValueType = value.BasicValueType;

            var type = PTXType.GetPTXType(basicValueType);
            var register = Allocate(value, type.RegisterKind);

            using (var command = BeginCommand(
                Instructions.MoveOperation,
                type))
            {
                command.AppendArgument(register);

                switch (basicValueType)
                {
                    case BasicValueType.Int1:
                    case BasicValueType.Int8:
                        command.AppendConstant(value.UInt8Value);
                        break;
                    case BasicValueType.Int16:
                        command.AppendConstant(value.UInt16Value);
                        break;
                    case BasicValueType.Int32:
                        command.AppendConstant(value.UInt32Value);
                        break;
                    case BasicValueType.Int64:
                        command.AppendConstant(value.UInt64Value);
                        break;
                    case BasicValueType.Float32:
                        command.AppendConstant(value.Float32Value);
                        break;
                    case BasicValueType.Float64:
                        command.AppendConstant(value.Float64Value);
                        break;
                    default:
                        throw new InvalidCodeGenerationException();
                }
            }
        }

        /// <summary cref="IValueVisitor.Visit(StringValue)"/>
        public void Visit(StringValue value)
        {
            // Check for already existing global constant
            if (!stringConstants.TryGetValue(value.String, out string stringBinding))
            {
                stringBinding = "__strconst" + value.Id;
                stringConstants.Add(value.String, stringBinding);
            }

            var register = AllocatePlatformRegister(value, out PTXType postFix);
            using (var command = BeginCommand(
                Instructions.MoveOperation,
                PTXType.GetPTXType(ABI.PointerArithmeticType)))
            {
                command.AppendArgument(register);
                command.AppendRawValueReference(stringBinding);
            }
        }

        /// <summary>
        /// Emits complex null values.
        /// </summary>
        private readonly struct NullEmitter : IComplexCommandEmitter
        {
            /// <summary cref="IComplexCommandEmitter.Emit(CommandEmitter, RegisterAllocator{PTXRegisterKind}.PrimitiveRegister[])"/>
            public void Emit(CommandEmitter commandEmitter, PrimitiveRegister[] registers)
            {
                var type = PTXType.GetPTXType(registers[0].Kind);

                commandEmitter.AppendPostFix(type);
                commandEmitter.AppendArgument(registers[0]);
                commandEmitter.AppendConstant(0);
            }
        }

        /// <summary cref="IValueVisitor.Visit(NullValue)"/>
        public void Visit(NullValue value)
        {
            switch (value.Type)
            {
                case VoidType _:
                    // Ignore void type nulls
                    break;
                case ViewType viewType:
                    MakeNullView(value, viewType);
                    break;
                default:
                    var targetRegister = Allocate(value);
                    EmitComplexCommand(
                        Instructions.MoveOperation,
                        new NullEmitter(),
                        targetRegister);
                    break;
            }
        }

        /// <summary cref="IValueVisitor.Visit(SizeOfValue)"/>
        public void Visit(SizeOfValue value) => throw new InvalidCodeGenerationException();

        /// <summary cref="IValueVisitor.Visit(GetField)"/>
        public void Visit(GetField value)
        {
            var source = LoadAs<StructureRegister>(value.StructValue);
            Bind(value, source.Children[value.FieldIndex]);
        }

        /// <summary cref="IValueVisitor.Visit(SetField)"/>
        public void Visit(SetField value)
        {
            var source = LoadAs<StructureRegister>(value.StructValue);
            var storeValue = Load(value.Value);

            var targetChildren = source.Children.SetItem(value.FieldIndex, storeValue);
            var targetRegister = new StructureRegister(
                value.StructureType,
                targetChildren);
            Bind(value, targetRegister);
        }

        /// <summary cref="IValueVisitor.Visit(GridDimensionValue)"/>
        public void Visit(GridDimensionValue value)
        {
            var target = Allocate(value, PTXRegisterKind.Int32);
            Move(new PrimitiveRegister(PTXRegisterKind.NctaId, (int)value.Dimension),
                target);
        }

        /// <summary cref="IValueVisitor.Visit(GroupDimensionValue)"/>
        public void Visit(GroupDimensionValue value)
        {
            var target = Allocate(value, PTXRegisterKind.Int32);
            Move(
                new PrimitiveRegister(PTXRegisterKind.NtId, (int)value.Dimension),
                target);
        }

        /// <summary cref="IValueVisitor.Visit(WarpSizeValue)"/>
        public void Visit(WarpSizeValue value) => throw new InvalidCodeGenerationException();

        /// <summary cref="IValueVisitor.Visit(LaneIdxValue)"/>
        public void Visit(LaneIdxValue value)
        {
            var target = Allocate(value, PTXRegisterKind.Int32);
            Move(
                new PrimitiveRegister(PTXRegisterKind.LaneId, 0),
                target);
        }

        /// <summary cref="IValueVisitor.Visit(PredicateBarrier)"/>
        public void Visit(PredicateBarrier barrier)
        {
            var targetRegister = Allocate(barrier, PTXRegisterKind.Int32);
            var sourcePredicate = LoadPrimitive(barrier.Predicate);
            using (var predciateScope = ConvertToPredicateScope(sourcePredicate))
            {
                switch (barrier.Kind)
                {
                    case PredicateBarrierKind.And:
                    case PredicateBarrierKind.Or:
                        using (var targetPredicateScope = new PredicateScope(this))
                        {
                            using (var command = BeginCommand(
                                Instructions.GetPredicateBarrier(barrier.Kind)))
                            {
                                command.AppendArgument(targetPredicateScope.PredicateRegister);
                                command.AppendConstant(0);
                                command.AppendArgument(predciateScope.PredicateRegister);
                            }
                            targetPredicateScope.ConvertToValue(this, targetRegister);
                        }
                        break;
                    case PredicateBarrierKind.PopCount:
                        using (var command = BeginCommand(
                            Instructions.GetPredicateBarrier(barrier.Kind)))
                        {
                            command.AppendArgument(targetRegister);
                            command.AppendConstant(0);
                            command.AppendArgument(predciateScope.PredicateRegister);
                        }
                        break;
                    default:
                        throw new InvalidCodeGenerationException();
                }
            }
        }

        /// <summary cref="IValueVisitor.Visit(Barrier)"/>
        public void Visit(Barrier barrier)
        {
            using (var command = BeginCommand(
                Instructions.GetBarrier(barrier.Kind)))
            {
                switch (barrier.Kind)
                {
                    case BarrierKind.WarpLevel:
                        command.AppendConstant(Instructions.AllThreadsInAWarpMemberMask);
                        break;
                    case BarrierKind.GroupLevel:
                        command.AppendConstant(0);
                        break;
                    default:
                        throw new InvalidCodeGenerationException();
                }
            }
        }

        /// <summary>
        /// Represents an abstract emitter of warp shuffle masks.
        /// </summary>
        private interface IShuffleEmitter
        {
            /// <summary>
            /// Emits a new warp mask.
            /// </summary>
            /// <param name="commandEmitter">The current command emitter.</param>
            void EmitWarpMask(CommandEmitter commandEmitter);
        }

        /// <summary>
        /// Creates a new shuffle operation.
        /// </summary>
        /// <typeparam name="TShuffleEmitter">The emitter type.</typeparam>
        /// <param name="shuffle">The current shuffle operation.</param>
        /// <param name="shuffleEmitter">The shuffle emitter.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EmitShuffleOperation<TShuffleEmitter>(
            ShuffleOperation shuffle,
            in TShuffleEmitter shuffleEmitter)
            where TShuffleEmitter : IShuffleEmitter
        {
            var ptxType = PTXType.GetPTXType(shuffle.Variable.BasicValueType);
            var targetRegister = Allocate(shuffle, ptxType.RegisterKind);

            var variable = LoadPrimitive(shuffle.Variable);
            var delta = LoadPrimitive(shuffle.Origin);

            var shuffleOperation = Instructions.GetShuffleOperation(shuffle.Kind);
            using (var command = BeginCommand(shuffleOperation))
            {
                command.AppendArgument(targetRegister);
                command.AppendArgument(variable);
                command.AppendArgument(delta);

                // Invoke the shuffle emitter
                shuffleEmitter.EmitWarpMask(command);

                command.AppendConstant(Instructions.AllThreadsInAWarpMemberMask);
            }
        }

        /// <summary>
        /// Emits warp masks of <see cref="WarpShuffle"/> operations.
        /// </summary>
        private readonly struct WarpShuffleEmitter : IShuffleEmitter
        {
            /// <summary>
            /// The basic mask that has be combined with an 'or' command
            /// in case of a <see cref="ShuffleKind.Xor"/> or a <see cref="ShuffleKind.Down"/>
            /// shuffle instruction.
            /// </summary>
            public const int XorDownMask = 0x1f;

            /// <summary>
            /// The amount of bits the basic mask has to be shifted to
            /// the left.
            /// </summary>
            public const int BaseMaskShiftAmount = 8;

            /// <summary>
            /// Constructs a new shuffle emitter.
            /// </summary>
            /// <param name="shuffleKind">The current shuffle kind.</param>
            public WarpShuffleEmitter(ShuffleKind shuffleKind)
            {
                ShuffleKind = shuffleKind;
            }

            /// <summary>
            /// The shuffle kind.
            /// </summary>
            public ShuffleKind ShuffleKind { get; }

            /// <summary cref="IShuffleEmitter.EmitWarpMask(CommandEmitter)"/>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void EmitWarpMask(CommandEmitter commandEmitter)
            {
                if (ShuffleKind == ShuffleKind.Up)
                    commandEmitter.AppendConstant(0);
                else
                    commandEmitter.AppendConstant(XorDownMask);
            }
        }

        /// <summary cref="IValueVisitor.Visit(WarpShuffle)"/>
        public void Visit(WarpShuffle shuffle)
        {
            EmitShuffleOperation(
                shuffle,
                new WarpShuffleEmitter(shuffle.Kind));
        }

        /// <summary>
        /// Emits warp masks of <see cref="SubWarpShuffle"/> operations.
        /// </summary>
        private readonly struct SubWarpShuffleEmitter : IShuffleEmitter
        {
            /// <summary>
            /// Constructs a new shuffle emitter.
            /// </summary>
            /// <param name="warpMaskRegister">The current mask register.</param>
            public SubWarpShuffleEmitter(PrimitiveRegister warpMaskRegister)
            {
                WarpMaskRegister = warpMaskRegister;
            }

            /// <summary>
            /// Returns the current mask register.
            /// </summary>
            public PrimitiveRegister WarpMaskRegister { get; }

            /// <summary cref="IShuffleEmitter.EmitWarpMask(CommandEmitter)"/>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void EmitWarpMask(CommandEmitter commandEmitter)
            {
                commandEmitter.AppendArgument(WarpMaskRegister);
            }
        }

        /// <summary cref="IValueVisitor.Visit(SubWarpShuffle)"/>
        public void Visit(SubWarpShuffle shuffle)
        {
            // Compute the actual warp mask
            var width = LoadPrimitive(shuffle.Width);
            var ptxType = PTXType.GetPTXType(width.Kind);

            // Create basic mask
            var baseRegister = AllocateRegister(ptxType.RegisterKind);
            using (var command = BeginCommand(
                Instructions.GetArithmeticOperation(
                    BinaryArithmeticKind.Sub,
                    ArithmeticBasicValueType.UInt32,
                    false)))
            {
                command.AppendArgument(baseRegister);
                command.AppendConstant(PTXBackend.WarpSize);
                command.AppendArgument(width);
            }

            // Shift mask
            var maskRegister = AllocateRegister(ptxType.RegisterKind);
            using (var command = BeginCommand(
                Instructions.GetArithmeticOperation(
                    BinaryArithmeticKind.Shl,
                    ArithmeticBasicValueType.UInt32,
                    false)))
            {
                command.AppendArgument(maskRegister);
                command.AppendArgument(baseRegister);
                command.AppendConstant(WarpShuffleEmitter.BaseMaskShiftAmount);
            }
            FreeRegister(baseRegister);

            // Adjust mask register
            if (shuffle.Kind != ShuffleKind.Up)
            {
                var adjustedMaskRegister = AllocateRegister(ptxType.RegisterKind);
                using (var command = BeginCommand(
                    Instructions.GetArithmeticOperation(
                        BinaryArithmeticKind.Or,
                        ArithmeticBasicValueType.UInt32,
                        false)))
                {
                    command.AppendArgument(adjustedMaskRegister);
                    command.AppendArgument(maskRegister);
                    command.AppendConstant(WarpShuffleEmitter.XorDownMask);
                }

                FreeRegister(maskRegister);
                maskRegister = adjustedMaskRegister;
            }

            EmitShuffleOperation(
                shuffle,
                new SubWarpShuffleEmitter(maskRegister));
            FreeRegister(maskRegister);
        }

        /// <summary cref="IValueVisitor.Visit(DebugAssertFailed)"/>
        public void Visit(DebugAssertFailed assert)
        {
            Debug.Assert(false, "Invalid assert node -> should have been removed");
        }

        /// <summary cref="IValueVisitor.Visit(DebugTrace)"/>
        public void Visit(DebugTrace trace)
        {
            Debug.Assert(false, "Invalid trace node -> should have been removed");
        }
    }
}
