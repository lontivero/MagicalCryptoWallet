using System.Collections.Generic;
using System.Text;
using System.Threading;
using NBitcoin;
using NBitcoin.RPC;
using Xunit;
using System;
using System.Linq;
using System.Threading.Tasks.Dataflow;

namespace Scanner.Tests
{
    public class FilterCreationTests
    {
        [Fact]
	    public void CanGetChainInfo()
        {
			Console.WriteLine($"System date: {DateTimeOffset.UtcNow}");
	        var scannerCommandLine = "curl x --blockhash %s";

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

					if (i > 100)
				    {
					    var fundingTx = generatedBlocks[i-100].Transactions[0];
					    var amount = fundingTx.Outputs[0].Value.ToUnit(MoneyUnit.BTC);

						var spending = new Transaction();
						spending.Inputs.Add(new TxIn(new OutPoint(fundingTx, 0)));
						spending.Outputs.Add(new TxOut(Money.Coins(5.0m), destinationKeys[destinationIdx++]));
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
			    Thread.Sleep(5000);
			}
	    }
    }
}
