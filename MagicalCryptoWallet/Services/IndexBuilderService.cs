﻿using MagicalCryptoWallet.Backend.Models;
using MagicalCryptoWallet.Helpers;
using MagicalCryptoWallet.Logging;
using MagicalCryptoWallet.Models;
using NBitcoin;
using NBitcoin.RPC;
using Nito.AsyncEx;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MagicalCryptoWallet.Services
{
	public class IndexBuilderService
	{
		public RPCClient RpcClient { get; }
		public string IndexFilePath { get; }
		public string Bech32UtxoSetFilePath { get; }

		private List<FilterModel> Index { get; }
		private AsyncLock IndexLock { get; }

		private Dictionary<OutPoint, Script> Bech32UtxoSet { get; }
		private ActionHistoryHelper Bech32UtxoSetHistory { get; }

		private class ActionHistoryHelper
		{
			public enum Operation
			{
				Add,
				Remove
			}

			private List<ActionItem> ActionHistory { get; }

			public ActionHistoryHelper()
			{
				ActionHistory = new List<ActionItem>();
			}

			public class ActionItem
			{
				public Operation Action { get; }
				public OutPoint OutPoint { get; }
				public Script Script { get; }

				public ActionItem(Operation action, OutPoint outPoint, Script script)
				{
					Action = action;
					OutPoint = outPoint;
					Script = script;
				}
			}

			public void ClearActionHistory()
			{
				ActionHistory.Clear();
			}

			public void StoreAction(ActionItem actionItem)
			{
				ActionHistory.Add(actionItem);
			}
			public void StoreAction(Operation action, OutPoint outpoint, Script script)
			{
				StoreAction(new ActionItem(action, outpoint, script));
			}

			public void Rollback(Dictionary<OutPoint, Script> toRollBack)
			{
				for (var i = ActionHistory.Count - 1; i >= 0; i--)
				{
					ActionItem act = ActionHistory[i];
					switch (act.Action)
					{
						case Operation.Add:
							toRollBack.Remove(act.OutPoint);
							break;
						case Operation.Remove:
							toRollBack.Add(act.OutPoint, act.Script);
							break;
						default:
							throw new ArgumentOutOfRangeException();
					}
				}
				ActionHistory.Clear();
			}
		}

		public Height StartingHeight // First possible bech32 transaction ever.
		{
			get
			{
				if (RpcClient.Network == Network.Main)
				{
					return new Height(481824);
				}
				else if (RpcClient.Network == Network.TestNet)
				{
					return new Height(828575);
				}
				else if (RpcClient.Network == Network.RegTest)
				{
					return new Height(0);
				}
				else
				{
					throw new NotSupportedException($"{RpcClient.Network} is not supported.");
				}
			}
		}

		private long _running;
		public bool IsRunning => Interlocked.Read(ref _running) == 1;

		public IndexBuilderService(RPCClient rpc, string indexFilePath, string bech32UtxoSetFilePath)
		{
			RpcClient = Guard.NotNull(nameof(rpc), rpc);
			IndexFilePath = Guard.NotNullOrEmptyOrWhitespace(nameof(indexFilePath), indexFilePath);
			Bech32UtxoSetFilePath = Guard.NotNullOrEmptyOrWhitespace(nameof(bech32UtxoSetFilePath), bech32UtxoSetFilePath);

			Bech32UtxoSet = new Dictionary<OutPoint, Script>();
			Bech32UtxoSetHistory = new ActionHistoryHelper();
			Index = new List<FilterModel>();
			IndexLock = new AsyncLock();

			_running = 0;

			var indexDir = Path.GetDirectoryName(IndexFilePath);
			Directory.CreateDirectory(indexDir);
			if (File.Exists(IndexFilePath))
			{
				if (RpcClient.Network == Network.RegTest)
				{
					File.Delete(IndexFilePath); // RegTest is not a global ledger, better to delete it.
				}
				else
				{
					int height = StartingHeight.Value;
					foreach (var line in File.ReadAllLines(IndexFilePath))
					{
						var filter = FilterModel.FromLine(line, new Height(height));
						height++;
						Index.Add(filter);
					}
				}
			}

			var utxoSetDir = Path.GetDirectoryName(bech32UtxoSetFilePath);
			Directory.CreateDirectory(utxoSetDir);
			if (File.Exists(bech32UtxoSetFilePath))
			{
				if (RpcClient.Network == Network.RegTest)
				{
					File.Delete(bech32UtxoSetFilePath); // RegTest is not a global ledger, better to delete it.
				}
				else
				{
					foreach (var line in File.ReadAllLines(Bech32UtxoSetFilePath))
					{
						var parts = line.Split(':');

						var txHash = new uint256(parts[0]);
						var nIn = int.Parse(parts[1]);
						var script = new Script(ByteHelpers.FromHex(parts[2]), true);
						Bech32UtxoSet.Add(new OutPoint(txHash, nIn), script);
					}
				}
			}
		}

		public void Syncronize()
		{
			Interlocked.Exchange(ref _running, 1);

			Task.Run(async () =>
			{
				using (var indexFile = File.Open(IndexFilePath, FileMode.OpenOrCreate))
				{
					indexFile.Seek(0, SeekOrigin.End);
					var lastPosition = indexFile.Position;

					while (IsRunning)
					{
						try
						{
							// If stop was requested return.
							if (IsRunning == false) return;

							await ProcessAsync(indexFile, lastPosition);
						}
						catch (Exception ex)
						{
							Logger.LogDebug<IndexBuilderService>(ex);
						}
					}
					await SaveBech32UtxoAsync();
					indexFile.Close();
				}
			});
		}

        private async Task SaveBech32UtxoAsync()
        {
			if (File.Exists(Bech32UtxoSetFilePath))
			{
				File.Delete(Bech32UtxoSetFilePath);
			}

			var utxoStr = Bech32UtxoSet.Select(entry
				=> entry.Key.Hash + ":" + entry.Key.N + ":" + ByteHelpers.ToHex(entry.Value.ToCompressedBytes()));

			await File.WriteAllLinesAsync(Bech32UtxoSetFilePath, utxoStr);
        }

        private async Task ProcessAsync(FileStream indexFile, long lastPosition)
		{
			int height = StartingHeight.Value;
			uint256 prevHash = null;

			using (await IndexLock.LockAsync())
			{
				if (Index.Count != 0)
				{
					var item = Index.Last(); 
					height = item.BlockHeight.Value + 1;
					prevHash = item.BlockHash;
				}
			}

			Block block = null;
			try
			{
				block = await RpcClient.GetBlockAsync(height);
			}
			catch (RPCException) // if the block didn't come yet
			{
				// ToDO: If this happens, we should do `waitforblock` RPC instead of periodically asking.
				// In that case we must also make sure the correct error message comes.
				await Task.Delay(1000);
				return;
			}

			if (prevHash != null)
			{
				// In case of reorg:
				if (prevHash != block.Header.HashPrevBlock)
				{
					Logger.LogInfo<IndexBuilderService>($"REORG Invalid Block: {prevHash}");
					// 1. Rollback index
					using (await IndexLock.LockAsync())
					{
						Index.RemoveAt(Index.Count - 1);
					}

					// 2. Serialize Index. (Remove last line.)
					await indexFile.FlushAsync();
					indexFile.Position = lastPosition;

					// 3. Rollback Bech32UtxoSet
					Bech32UtxoSetHistory.Rollback(Bech32UtxoSet); // The Bech32UtxoSet MUST be recovered to its previous state.

					// 4. Serialize Bech32UtxoSet.
					await SaveBech32UtxoAsync();

					// 5. Skip the current block.
					return;
				}
			}

			Bech32UtxoSetHistory.ClearActionHistory(); //reset history.

			var scripts = new HashSet<Script>();

			foreach (var tx in block.Transactions)
			{
				for (int i = 0; i < tx.Outputs.Count; i++)
				{
					var output = tx.Outputs[i];
					if (!output.ScriptPubKey.IsPayToScriptHash && output.ScriptPubKey.IsWitness)
					{
						var outpoint = new OutPoint(tx.GetHash(), i);
						Bech32UtxoSet.Add(outpoint, output.ScriptPubKey);
						Bech32UtxoSetHistory.StoreAction(ActionHistoryHelper.Operation.Add, outpoint, output.ScriptPubKey);
						scripts.Add(output.ScriptPubKey);
					}
				}

				foreach (var input in tx.Inputs)
				{
					var found = Bech32UtxoSet.ContainsKey(input.PrevOut);
					if (found)
					{
						Script val = Bech32UtxoSet[input.PrevOut];
						Bech32UtxoSet.Remove(input.PrevOut);
						Bech32UtxoSetHistory.StoreAction(ActionHistoryHelper.Operation.Remove, input.PrevOut, val);
						scripts.Add(val);
					}
				}
			}

			// https://github.com/bitcoin/bips/blob/master/bip-0158.mediawiki
			// The parameter k MUST be set to the first 16 bytes of the hash of the block for which the filter 
			// is constructed.This ensures the key is deterministic while still varying from block to block.
			var key = block.GetHash().ToBytes().SafeSubarray(0, 16);

			GolombRiceFilter filter = null;
			if (scripts.Count != 0)
			{
				filter = GolombRiceFilter.Build(key, scripts.Select(x => x.ToBytes()));
			}

			var filterModel = new FilterModel
			{
				BlockHash = block.GetHash(),
				BlockHeight = new Height(height),
				Filter = filter
			};

			lastPosition = indexFile.Position;
			var buff = Encoding.ASCII.GetBytes(filterModel.ToLine() + Environment.NewLine);
			await indexFile.WriteAsync(buff, 0, buff.Length);

			using (await IndexLock.LockAsync())
			{
				Index.Add(filterModel);
			}

			if(height % 10000 == 0)
				await SaveBech32UtxoAsync();

			Logger.LogInfo<IndexBuilderService>($"Created filter for block: {height}.");
		}

		public IEnumerable<string> GetFilters(uint256 bestKnownBlockHash)
		{
			using (IndexLock.Lock())
			{
				var found = false;
				foreach (var filter in Index)
				{
					if (found)
					{
						yield return filter.ToLine();
					}
					else
					{
						if (filter.BlockHash == bestKnownBlockHash)
						{
							found = true;
						}
					}
				}
			}
		}

		public void Stop()
		{
			Interlocked.Exchange(ref _running, 0);
		}

		private Dictionary<int, Task<Block>> _blockCache = new Dictionary<int, Task<Block>>();
		private async Task<Block> RpcClientGetBlockAsync(int height)
		{
			if (!_blockCache.ContainsKey(height))
			{
				_blockCache.Clear();
				var rpc = RpcClient.PrepareBatch();
				foreach(var h in Enumerable.Range(height, 24))
				{
					_blockCache.Add(h, rpc.GetBlockAsync(h));
				}
				await rpc.SendBatchAsync();
			}
			return await _blockCache[height];
		}
	}
}
