using System.Collections.Generic;
using System.Text;
using System.Threading;
using NBitcoin;
using NBitcoin.RPC;
using Xunit;
using System;
using System.Linq;
using System.Threading.Tasks.Dataflow;
using MagicalCryptoWallet.Backend;
using Microsoft.AspNetCore.TestHost;

namespace MagicalCryptoWallet.Tests
{
    public class FilterCreationTests
    {
		// CreateRealFilters is an end-to-end integration tests that performs the 
		// following tasks:
		//
		// 1) It hosts the backend web server, the one tha provides the API, in order
		//    to process the requests for creating new filters.
		//
		// 2) Downloads the Bitcoin Core node (for Linux or Windows).
		//
		// 3) Configures and runs the Bitcoin Core node with RegTest network
		//
		// 4) Mines blocks 110 blocks: 
		//    the first 100 blocks only contains the coinbase transactions and
		//    the rest 10 blocks with transactions that spend the previous 100
		//    blocks mined coins.
		//
		// 5) Connects to the Bitcoin Core node and broadcasts the 110 mined 
		//    blocks. Every time the Bitcoin Core node accepts one of these blocks
		//    it executes the command line:
		//
		//    $ curl http://localhost:37127/api/v1/btc/block/%s
		//
		//    Note that the %s suffix is replaced by the accepted block's hash 
		//    in every call.
		//
		// 6) The previous http request triggers the creation of the filter for
		//    blocks.
		//
		// 7) Finally, it verifies that the filters can match the spending 
		//    transactions settled in those blocks
		// 
        [Fact]
	    public async void CreateRealFilters()
        {
			var serverEndpoint = "http://localhost:37127";

			using(var server= new BackendServerMock(serverEndpoint))
			{
				await server.StartAsync();
				
				Console.WriteLine($"System date: {DateTimeOffset.UtcNow}");
				var scannerCommandLine = $"curl -v {serverEndpoint}/api/v1/btc/Blockchain/block/%s";

				using (var node = BitcoinCoreNode.Create("./node-1", $"blocknotify={scannerCommandLine}"))
				{
					node.Start();
					
					var now = DateTimeOffset.UtcNow;

					var minerSecret = new Key().GetBitcoinSecret(node.Network);
					var minerAddress = minerSecret.GetAddress();
					var generatedBlocks = new List<Block>();
					var destinationKeys = Enumerable.Range(0, 10).Select(x => new Key()).ToArray();
					var destinationIdx = 0;

					var curBlock = node.Network.GetGenesis();
					for (var i = 0; i < 110; i++)
					{
						var prevBlock = curBlock;
						var blockTime = now + TimeSpan.FromMinutes((i+1));
						curBlock = curBlock.CreateNextBlockWithCoinbase(minerAddress, i+1, blockTime);
						curBlock.Header.Bits = curBlock.Header.GetWorkRequired(node.Network, new ChainedBlock(prevBlock.Header,i+1));

						if (i >= 100)
						{
							var fundingTx = generatedBlocks[i-100].Transactions[0];
							var amount = fundingTx.Outputs[0].Value.ToUnit(MoneyUnit.BTC);

							var spending = new Transaction();
							spending.Inputs.Add(new TxIn(new OutPoint(fundingTx, 0)));
							var destinationKey = destinationKeys[destinationIdx++];
							var destinationScript = PayToWitPubKeyHashTemplate.Instance.GenerateScriptPubKey(destinationKey.PubKey); 							
							spending.Outputs.Add(new TxOut(Money.Coins(5.0m), destinationScript));
							amount -= 5;
							spending.Outputs.Add(new TxOut(Money.Coins(amount), minerAddress));

							var builder = new TransactionBuilder();
							builder.AddKeys(minerSecret);
							builder.AddCoins(fundingTx.Outputs.AsCoins());
							builder.SignTransactionInPlace(spending);

							curBlock.Transactions.Add(spending);
						}
						node.MineBlock(curBlock);
						generatedBlocks.Add(curBlock);
					}

					foreach (var block in generatedBlocks)
					{
						node.BroadcastBlock(block);
					}

					Thread.Sleep(1000);
					destinationIdx = 0;
					foreach (var block in generatedBlocks.Skip(100))
					{
						var key = block.Header.GetHash();
						var filter = Global.FilterRepository.Get(key);
						//Assert.Equal(1, filter.N); // Every block has only one witscript

						var destinationKey = destinationKeys[destinationIdx++];
						var destinationScript = PayToWitPubKeyHashTemplate.Instance.GenerateScriptPubKey(destinationKey.PubKey); 							
						var parameter = PayToWitPubKeyHashTemplate.Instance.ExtractScriptPubKeyParameters(destinationScript);
						Assert.True(filter.Match(parameter.ToBytes(), key.ToBytes()));
					}
				}
			}
	    }
    }
}
