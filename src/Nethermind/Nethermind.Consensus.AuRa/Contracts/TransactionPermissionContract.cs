// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using Nethermind.Abi;
using Nethermind.Blockchain;
using Nethermind.Blockchain.Contracts;
using Nethermind.Core;
using Nethermind.Int256;

namespace Nethermind.Consensus.AuRa.Contracts
{
    public interface ITransactionPermissionContract : IVersionedContract
    {
        /// <summary>
        /// Returns the contract version number needed for node's engine.
        /// </summary>
        UInt256 Version { get; }

        /// <summary>
        /// Defines the allowed transaction types which may be initiated by the specified sender with
        /// the specified gas price and data. Used by node's engine each time a transaction is about to be
        /// included into a block.
        /// </summary>
        /// <param name="parentHeader"></param>
        /// <param name="tx"></param>
        /// <returns><see cref="TxPermissions"/>Set of allowed transactions types and <see cref="bool"/> If `true` is returned, the same permissions will be applied from the same sender without calling this contract again.</returns>
        (TxPermissions Permissions, bool ShouldCache, bool ContractExists) AllowedTxTypes(BlockHeader parentHeader, Transaction tx);

        [Flags]
        public enum TxPermissions : uint
        {
            /// <summary>
            /// No permissions
            /// </summary>
            None = 0x0,

            /// <summary>
            /// 0x01 - basic transaction (e.g. ether transferring to user wallet)
            /// </summary>
            Basic = 0b00000001,

            /// <summary>
            /// 0x02 - contract call
            /// </summary>
            Call = 0b00000010,

            /// <summary>
            /// 0x04 - contract creation
            /// </summary>
            Create = 0b00000100,

            /// <summary>
            /// 0x08 - private transaction
            /// </summary>
            Private = 0b00001000,

            All = 0xffffffff,
        }
    }

    public abstract class TransactionPermissionContract : Contract, ITransactionPermissionContract
    {
        public virtual UInt256 ContractVersion(BlockHeader blockHeader)
        {
            return Constant.Call<UInt256>(blockHeader, nameof(ContractVersion), Address.Zero);
        }

        /// <summary>
        /// Returns the contract version number needed for node's engine.
        /// </summary>
        public abstract UInt256 Version { get; }

        /// <summary>
        /// Defines the allowed transaction types which may be initiated by the specified sender with
        /// the specified gas price and data. Used by node's engine each time a transaction is about to be
        /// included into a block.
        /// </summary>
        /// <param name="parentHeader"></param>
        /// <param name="tx"></param>
        /// <returns><see cref="ITransactionPermissionContract.TxPermissions"/>Set of allowed transactions types and <see cref="bool"/> If `true` is returned, the same permissions will be applied from the same sender without calling this contract again.</returns>
        public (ITransactionPermissionContract.TxPermissions Permissions, bool ShouldCache, bool ContractExists) AllowedTxTypes(BlockHeader parentHeader, Transaction tx)
        {
            object[] parameters = GetAllowedTxTypesParameters(tx, parentHeader);
            PermissionConstantContract.PermissionCallInfo callInfo = new(parentHeader, nameof(AllowedTxTypes), Address.Zero, parameters, tx.To ?? Address.Zero);
            (ITransactionPermissionContract.TxPermissions, bool) result = CallAllowedTxTypes(callInfo);
            return (result.Item1, result.Item2, callInfo.ToIsContract);
        }

        protected virtual (ITransactionPermissionContract.TxPermissions, bool) CallAllowedTxTypes(PermissionConstantContract.PermissionCallInfo callInfo) =>
            Constant.Call<ITransactionPermissionContract.TxPermissions, bool>(callInfo);

        protected abstract object[] GetAllowedTxTypesParameters(Transaction tx, BlockHeader parentHeader);

        protected IConstantContract Constant { get; }

        protected TransactionPermissionContract(
            IAbiEncoder abiEncoder,
            Address contractAddress,
            IReadOnlyTxProcessorSource readOnlyTxProcessorSource)
            : base(abiEncoder, contractAddress)
        {
            Constant = new PermissionConstantContract(this, readOnlyTxProcessorSource);
        }

        protected class PermissionConstantContract : ConstantContract
        {
            public PermissionConstantContract(Contract contract, IReadOnlyTxProcessorSource readOnlyTxProcessorSource) : base(contract, readOnlyTxProcessorSource)
            {
            }

            protected override object[] CallRaw(CallInfo callInfo, IReadOnlyTxProcessingScope scope)
            {
                if (callInfo is PermissionCallInfo transactionPermissionCallInfo)
                {
                    transactionPermissionCallInfo.ToIsContract = scope.WorldState.IsContract(transactionPermissionCallInfo.To);
                }

                return base.CallRaw(callInfo, scope);
            }

            public class PermissionCallInfo : CallInfo
            {
                public Address To { get; }
                public bool ToIsContract { get; set; }

                public PermissionCallInfo(
                    BlockHeader parentHeader,
                    string functionName,
                    Address sender,
                    object[] arguments,
                    Address to)
                    : base(parentHeader, functionName, sender, arguments)
                {
                    To = to;
                }
            }
        }
    }
}
