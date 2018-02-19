using System;
using System.Collections.Generic;
using NBitcoin;
using NBitcoin.RPC;

namespace MagicalCryptoWallet.Backend
{
	public static class BlockFilterBuilder
	{
		private const int P = 20;
		private static readonly PayToWitPubKeyHashTemplate P2wpkh = PayToWitPubKeyHashTemplate.Instance;

		public static GolombRiceFilter Build(Block block)
		{
			var key = block.GetHash().ToBytes();

			var buffer = new List<byte[]>();
			buffer.Add(key);

			foreach (var tx in block.Transactions)
			{
				foreach (var txOutput in tx.Outputs)
				{
					var isValidPayToWitness = P2wpkh.CheckScriptPubKey(txOutput.ScriptPubKey);

					if (isValidPayToWitness)
					{
						var witKeyId = P2wpkh.ExtractScriptPubKeyParameters(txOutput.ScriptPubKey);
						buffer.Add(witKeyId.ToBytes());
					}
				}
			}

			return GolombRiceFilter.Build(key, P, buffer);
		}
	}
}
