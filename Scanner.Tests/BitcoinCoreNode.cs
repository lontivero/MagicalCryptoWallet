using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading;
using NBitcoin;
using NBitcoin.Protocol;
using NBitcoin.RPC;

namespace Scanner.Tests
{
	internal class BitcoinCoreNode : IDisposable
	{
		public static BitcoinCoreNode Create(string folderPath, string config)
		{
			var targetFolder = Directory.CreateDirectory(folderPath);
			var templateFolder = new DirectoryInfo("./download");

			DownloadBitcoinCore(templateFolder);
			CreateBitcoinNodeFolder(templateFolder, targetFolder);

			var configEntries = new Dictionary<string, string>();
			foreach (var keyValueStr in config.Split(","))
			{
				var keyValue = keyValueStr.Split("=");
				configEntries.Add(keyValue[0], keyValue[1]);
			}
			return new BitcoinCoreNode(targetFolder, configEntries);
		}

		private static void DownloadBitcoinCore(DirectoryInfo templateFolder)
		{
			if(templateFolder.Exists) return;
			templateFolder.Create();

			var zipPath = Path.Combine(Path.GetTempPath(), "bitcoin-0.16.0rc3-win64.zip");
			if (!File.Exists(zipPath))
			{
				using (var webClient = new WebClient())
				{
					var url = "https://bitcoin.org/bin/bitcoin-core-0.16.0/test.rc3/bitcoin-0.16.0rc3-win64.zip";

					webClient.DownloadFile(url, zipPath);
				}
			}

			using (var zipFile = ZipFile.Open(zipPath, ZipArchiveMode.Read))
			{
				using (var compressed = zipFile.GetEntry("bitcoin-0.16.0/bin/bitcoind.exe").Open())
				{
					const int BufferSize = 8 * 1024;
					var buffer = new byte[BufferSize];
					var readed = 0;
					using (var uncompressed = File.Create(Path.Combine(templateFolder.FullName, "bitcoind.exe")))
					{
						do
						{
							readed = compressed.Read(buffer, 0, BufferSize);
							uncompressed.Write(buffer, 0, readed);
						} while (readed > 0);
					}
				}
			}
		}

		private static void CreateBitcoinNodeFolder(DirectoryInfo templateFolder, DirectoryInfo targetFolder)
		{
			var targetFolderPath = targetFolder.FullName;
			if (targetFolder.Exists)
				targetFolder.Delete(true);
			Directory.CreateDirectory(targetFolderPath);

			foreach (var file in templateFolder.GetFiles())
			{
				file.CopyTo(Path.Combine(targetFolder.FullName, file.Name));
			}
		}

		private Process _process;
		private Dictionary<string, string> _config;
		private readonly DirectoryInfo _folder;

		public Network Network => Network.RegTest;

		private BitcoinCoreNode(DirectoryInfo folder, Dictionary<string, string> config)
		{
			_folder = folder;
			_config = config;
		}

		public void Start()
		{
			var folder = _folder.FullName;
			var bitcoinCoreExe = Path.Combine(folder, "bitcoind.exe");
			var dataFolder = Path.Combine(folder, "data");
			var configFilePath = Path.Combine(folder, "bitcoin.conf");

			Directory.CreateDirectory(dataFolder);

			_config.TryAdd("regtes", "1");
			_config.TryAdd("rest", "1");
			_config.TryAdd("server", "1");
			_config.TryAdd("txindex", "1");
			_config.TryAdd("port", "8332");
			_config.TryAdd("rpcport", "18332");
			_config.TryAdd("printtoconsole", "1");
			_config.TryAdd("whitebind", "127.0.0.1:8332");
			_config.TryAdd("datadir", "configFilePath");

			var configLines = _config.Select(x=> $"{x.Key} = {x.Value}").Reverse();
			File.WriteAllLines(configFilePath, configLines);

			_process = Process.Start(bitcoinCoreExe, $"-conf={configFilePath}");
			Thread.Sleep(5);

			var restClient = new RestClient(new Uri("http://127.0.0.1:18332"));
			while (true)
			{
				try
				{
					var info = restClient.GetChainInfoAsync().Result;
					break;
				}
				catch
				{
					Thread.Sleep(5);
				}
			}
		}

		public void BroadcastBlock(Block block)
		{
			using (var node = Node.Connect(Network.RegTest, "127.0.0.1:8332"))
			{
				node.VersionHandshake();
				node.SendMessageAsync(new InvPayload(block));
				node.SendMessageAsync(new BlockPayload(block));
				node.PingPong();
			}
		}


		public void MineBlock(Block block)
		{
			block.UpdateMerkleRoot();
			uint nonce = 0;
			while (!block.CheckProofOfWork(Network.Consensus))
			{
				block.Header.Nonce = ++nonce;
			}
		}

		public void Dispose()
		{
			_process.Kill();
			_process.WaitForExit();
			_process.Dispose();
		}
	}
}