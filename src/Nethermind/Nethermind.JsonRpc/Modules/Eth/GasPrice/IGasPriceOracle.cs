// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Threading.Tasks;
using Nethermind.Int256;

namespace Nethermind.JsonRpc.Modules.Eth.GasPrice
{
    public interface IGasPriceOracle
    {
        ValueTask<UInt256> GetGasPriceEstimate();
        UInt256 GetMaxPriorityGasFeeEstimate();
    }
}
