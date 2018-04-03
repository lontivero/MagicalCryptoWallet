using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GMagicalCryptoWallet.Tests;
using MagicalCryptoWallet.Backend;
using NBitcoin;
using NBitcoin.Crypto;
using Xunit;

namespace MagicalCryptoWallet.Tests
{
	public class PersistenceTest : IClassFixture<SharedFixture>
	{
		private SharedFixture SharedFixture { get; }

		public PersistenceTest(SharedFixture sharedFixture)
		{
			SharedFixture = sharedFixture;
		}

		[Fact]
		public void CreateStoreTest()
		{
			const byte P = 20;
			const int blockCount = 100;
			const int maxBlockSize = 4 * 1000 * 1000;
			const int avgTxSize = 250; // Currently the average is around 1kb.
			const int txoutCountPerBlock = maxBlockSize / avgTxSize;
			const int avgTxoutPushDataSize = 20; // P2PKH scripts has 20 bytes.

			var key = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };

			// Generation of data to be added into the filter
			var random = new Random();
			var folderPath = Path.Combine(SharedFixture.DataDir, nameof(CreateStoreTest), $"Filters");

			var dataDirectory = new DirectoryInfo( folderPath );
			if (dataDirectory.Exists)
			{
				foreach (var fileInfo in dataDirectory.GetFiles())
				{
					fileInfo.Delete();
				}
			}
			else
			{
				dataDirectory.Create();
			}

			var blocks = new List<GolombRiceFilter>(blockCount);
			using (var repo = FilterRepository.Open(folderPath))
			{
				for (var i = 0; i < blockCount; i++)
				{
					var txouts = new List<byte[]>(txoutCountPerBlock);
					for (var j = 0; j < txoutCountPerBlock; j++)
					{
						var pushDataBuffer = new byte[avgTxoutPushDataSize];
						random.NextBytes(pushDataBuffer);
						txouts.Add(pushDataBuffer);
					}

					var filter = GolombRiceFilter.Build(key, txouts, P);
					blocks.Add(filter);
					repo.Append(Hashes.Hash256(filter.Data.ToByteArray()), filter);
				}
			}

			using (var repo = FilterRepository.Open(folderPath))
			{
				var blockIndexes = Enumerable.Range(0, blockCount).ToList();
				blockIndexes.Shuffle();

				foreach (var blkIndx in blockIndexes)
				{
					var block = blocks[blkIndx];
					var blockFilter = block;
					var blockFilterId = Hashes.Hash256(blockFilter.Data.ToByteArray());
					var savedFilter = repo.Get(blockFilterId).First();
					var savedFilterId = Hashes.Hash256(savedFilter.Data.ToByteArray());
					Assert.Equal(blockFilterId, savedFilterId);
				}
			}
		}

		[Fact]
		public void DeleteIndexTest()
		{
			const byte P = 20;
			const int filterCount = 100;

			var key = new byte[] {0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15};

			// Generation of data to be added into the filter
			var random = new Random();
			var folderPath = Path.Combine(SharedFixture.DataDir, nameof(DeleteIndexTest), $"Filters");
			var dataDirectory = new DirectoryInfo( folderPath );

			try{
			dataDirectory.Delete(recursive: true);
			}catch{}
			dataDirectory.Create();

			var filters = new List<GolombRiceFilter>(filterCount);
			using (var repo = FilterRepository.Open(folderPath))
			{
				for (var i = 0; i < filterCount; i++)
				{
					var size = random.Next(1, 10000);
					var pushDataBuffer = new byte[size];
					random.NextBytes(pushDataBuffer);

					var filter = GolombRiceFilter.Build(key, new []{ pushDataBuffer }, P);
					filters.Add(filter);
					repo.Append(Hashes.Hash256(filter.Data.ToByteArray()), filter);
				}

				var filterKey = Hashes.Hash256(filters[1].Data.ToByteArray());
				Assert.Equal(filters[1].Data.ToByteArray(), repo.Get(filterKey).First().Data.ToByteArray());

				repo.Delete(filterKey);
				Assert.Null(repo.Get(filterKey).FirstOrDefault());

				Assert.Equal(filterCount-1, repo.GetAll().Count());
			}
		}
	}
}
