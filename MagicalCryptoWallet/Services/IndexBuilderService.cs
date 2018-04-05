using MagicalCryptoWallet.Backend;
using MagicalCryptoWallet.Backend.Models;
using MagicalCryptoWallet.Helpers;
using MagicalCryptoWallet.Logging;
using MagicalCryptoWallet.Models;
using NBitcoin;
using NBitcoin.RPC;
using Nito.AsyncEx;
using System;
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

		public Height StartingHeight => RpcClient.Network.GetStartingHeight();
		public static FilterModel GetStartingFilter(Network network) => IndexDownloader.GetStartingFilter(network);
		public FilterModel StartingFilter => GetStartingFilter(RpcClient.Network);

		/// <summary>
		/// 0: Not started, 1: Running, 2: Stopping, 3: Stopped
		/// </summary>
		private long _running;
		public bool IsRunning => Interlocked.Read(ref _running) == 1;
		public bool IsStopping => Interlocked.Read(ref _running) == 2;

		public IndexBuilderService(RPCClient rpc, string indexFilePath, string bech32UtxoSetFilePath)
		{
			RpcClient = Guard.NotNull(nameof(rpc), rpc);
			IndexFilePath = Guard.NotNullOrEmptyOrWhitespace(nameof(indexFilePath), indexFilePath);
			Bech32UtxoSetFilePath = Guard.NotNullOrEmptyOrWhitespace(nameof(bech32UtxoSetFilePath), bech32UtxoSetFilePath);

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
					using(var filters = FilterRepository.Open(IndexFilePath))
					{
						int height = StartingHeight.Value;
						foreach (var filter in filters.GetAll())
						{
							Index.Add(new FilterModel{ 
								Filter = filter, 
								BlockHeight = new Height(height)}
							);
							height++;
						}
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
			}
		}

		private static GolombRiceFilter EmptyFilter = new GolombRiceFilter(new FastBitArray(), 0);

		public void Synchronize()
		{
			Interlocked.Exchange(ref _running, 1);

			var indexDir = Path.GetDirectoryName(IndexFilePath);
			var utxoSetDir = Path.GetDirectoryName(Bech32UtxoSetFilePath);

			var filters = FilterRepository.Open(IndexFilePath);
			var utxos = UtxosRepository.Open(Bech32UtxoSetFilePath);

			Task.Run(async () =>
			{
				try
				{
					int height = 0;
					uint256 prevHash = null;

					while (IsRunning)
					{
						try
						{
							// If stop was requested return.
							if (IsRunning == false) return;

							height = StartingHeight.Value;
							using (await IndexLock.LockAsync())
							{
								if(Index.Count > 0)
								{
									var lastFilter = Index.Last(); 
									height = lastFilter.BlockHeight.Value + 1;
									prevHash = lastFilter.BlockHash;
								}
							}

							Block block = null;
							try
							{
								block = await RpcClient.GetBlockAsync(height);
							}
							catch (RPCException) // if the block didn't come yet
							{
								await Task.Delay(1000);
								continue;
							}

							// In case of reorg:
							if (prevHash != null && prevHash != block.Header.HashPrevBlock)
							{
								Logger.LogInfo<IndexBuilderService>($"REORG Invalid Block: {prevHash}");
								// 1. Rollback index
								using (await IndexLock.LockAsync())
								{
									var lastFilter = Index.Last(); 
									Index.RemoveAt(Index.Count - 1);
									filters.Delete(lastFilter.BlockHash);
								}

								// 3. Rollback Bech32UtxoSet
								utxos.RevertToCheckpoint();

								continue;
							}

							var scripts = new HashSet<Script>();

							foreach (var tx in block.Transactions)
							{
								for (int i = 0; i < tx.Outputs.Count; i++)
								{
									var output = tx.Outputs[i];
									if (!output.ScriptPubKey.IsPayToScriptHash && output.ScriptPubKey.IsWitness)
									{
										var outpoint = new OutPoint(tx.GetHash(), i);
										utxos.Append(new UtxoData(outpoint, output.ScriptPubKey));
										scripts.Add(output.ScriptPubKey);
									}
								}

								foreach (var input in tx.Inputs)
								{
									var val = utxos.Get(input.PrevOut).FirstOrDefault();
									if (val != null)
									{
										utxos.Delete(input.PrevOut);
										scripts.Add(val.Script);
									}
								}
							}

							// https://github.com/bitcoin/bips/blob/master/bip-0158.mediawiki
							// The parameter k MUST be set to the first 16 bytes of the hash of the block for which the filter 
							// is constructed.This ensures the key is deterministic while still varying from block to block.
							var key = block.GetHash().ToBytes().Take(16).ToArray();

							var filter = (scripts.Count != 0)
								? GolombRiceFilter.Build(key, scripts.Select(x => x.ToBytes()))
								: EmptyFilter
;
							var filterModel = new FilterModel
							{
								BlockHash = block.GetHash(),
								BlockHeight = new Height(height),
								Filter = filter
							};

							filters.Append(filterModel.BlockHash, filterModel.Filter);

							using (await IndexLock.LockAsync())
							{
								Index.Add(filterModel);
							}
							utxos.Checkpoint();
						}
						catch (Exception ex)
						{
							Logger.LogError<IndexBuilderService>(ex);
						}
					}
				}
				finally
				{
					if (IsStopping)
					{
						Interlocked.Exchange(ref _running, 3);
					}
				}
			});
		}

		public IEnumerable<string> GetFilterLinesExcluding(uint256 bestKnownBlockHash, out bool found)
		{
			using (IndexLock.Lock())
			{
				found = false;
				var filters = new List<string>();
				foreach (var filter in Index)
				{
					if (found)
					{
						filters.Add(filter.ToLine());
					}
					else
					{
						if (filter.BlockHash == bestKnownBlockHash)
						{
							found = true;
						}
					}
				}

				return filters;
			}
		}

		public async Task StopAsync()
		{
			if (IsRunning)
			{
				Interlocked.Exchange(ref _running, 2);
			}
			while (IsStopping)
			{
				await Task.Delay(50);
			}
		}
	}

	internal static class NetworkExtensions
	{
		// First possible bech32 transaction ever.
		public static Height GetStartingHeight(this Network network) 
		{
			if (network == Network.Main)
			{
				return new Height(481824);
			}
			else if (network == Network.TestNet)
			{
				return new Height(828575);
			}
			else if (network == Network.RegTest)
			{
				return new Height(0);
			}
			else
			{
				throw new NotSupportedException($"{network} is not supported.");
			}
		}
	}
}
