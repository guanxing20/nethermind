// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Core.Crypto;
using Nethermind.Int256;
using Nethermind.Serialization.Rlp;
using System;

namespace Nethermind.Taiko;

public class L1OriginDecoder : IRlpStreamDecoder<L1Origin>
{
    const int BuildPayloadArgsIdLength = 8;

    public L1Origin Decode(RlpStream rlpStream, RlpBehaviors rlpBehaviors = RlpBehaviors.None)
    {
        (int _, int contentLength) = rlpStream.ReadPrefixAndContentLength();
        int itemsCount = rlpStream.PeekNumberOfItemsRemaining(maxSearch: contentLength);

        UInt256 blockId = rlpStream.DecodeUInt256();
        Hash256? l2BlockHash = rlpStream.DecodeKeccak();
        var l1BlockHeight = rlpStream.DecodeLong();
        Hash256 l1BlockHash = rlpStream.DecodeKeccak() ?? throw new RlpException("L1BlockHash is null");
        int[]? buildPayloadArgsId = itemsCount == 4 ? null : Array.ConvertAll(rlpStream.DecodeByteArray(), Convert.ToInt32);

        return new(blockId, l2BlockHash, l1BlockHeight, l1BlockHash, buildPayloadArgsId);
    }

    public Rlp Encode(L1Origin? item, RlpBehaviors rlpBehaviors = RlpBehaviors.None)
    {
        if (item is null)
            return Rlp.OfEmptySequence;

        RlpStream rlpStream = new(GetLength(item, rlpBehaviors));
        Encode(rlpStream, item, rlpBehaviors);
        return new(rlpStream.Data.ToArray()!);
    }

    public void Encode(RlpStream stream, L1Origin item, RlpBehaviors rlpBehaviors = RlpBehaviors.None)
    {
        stream.StartSequence(GetLength(item, rlpBehaviors));

        stream.Encode(item.BlockId);
        stream.Encode(item.L2BlockHash);
        stream.Encode(item.L1BlockHeight);
        stream.Encode(item.L1BlockHash);
        if (item.BuildPayloadArgsId is not null)
        {
            if (item.BuildPayloadArgsId.Length is not BuildPayloadArgsIdLength)
            {
                throw new RlpException($"{nameof(item.BuildPayloadArgsId)} should be exactly {BuildPayloadArgsIdLength}");
            }

            stream.Encode(Array.ConvertAll(item.BuildPayloadArgsId, Convert.ToByte));
        }
    }

    public int GetLength(L1Origin item, RlpBehaviors rlpBehaviors)
    {
        return Rlp.LengthOfSequence(
            Rlp.LengthOf(item.BlockId)
            + Rlp.LengthOf(item.L2BlockHash)
            + Rlp.LengthOf(item.L1BlockHeight)
            + Rlp.LengthOf(item.L1BlockHash)
            + (item.BuildPayloadArgsId is null ? 0 : Rlp.LengthOfByteString(BuildPayloadArgsIdLength, 0))
        );
    }
}
