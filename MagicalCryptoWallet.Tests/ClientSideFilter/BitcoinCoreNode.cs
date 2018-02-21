using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using NBitcoin;
using NBitcoin.Protocol;
using NBitcoin.RPC;

namespace MagicalCryptoWallet.Tests
{
	internal class BitcoinCoreNode : IDisposable
	{
		public static bool IsWindows;
		public static bool IsUnix;

		static BitcoinCoreNode()
		{
			var os = System.Environment.OSVersion.VersionString;
			Console.WriteLine($"OS {os}");
			IsWindows |= os.Contains("Windows");
			IsUnix |= os.Contains("Unix");
		}

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

			string platform="";
			if(IsWindows){
				platform = "bitcoin-0.16.0rc3-win64.zip";
			}
			else if(IsUnix){
				platform = "bitcoin-0.16.0rc3-x86_64-linux-gnu.tar.gz";
			}

			var compressedFilePath = Path.Combine(Path.GetTempPath(), platform);
			if (!File.Exists(compressedFilePath))
			{
				using (var webClient = new WebClient())
				{
					var url = $"https://bitcoin.org/bin/bitcoin-core-0.16.0/test.rc3/{platform}";
					Console.WriteLine($"Downloading from: {url}");

					webClient.DownloadFile(url, compressedFilePath);
				}
			}

			var bitcoindFileName = "bitcoind" + (IsWindows ? ".exe" : "");
			var uncompressedFilePath =Path.Combine(templateFolder.FullName, bitcoindFileName);

			if(IsUnix){
				Bash($"tar --strip-components=2 -xvzf {compressedFilePath} -C {templateFolder.FullName} bitcoin-0.16.0/bin/{bitcoindFileName}");
			}
			else
			{
				using (var zipFile = ZipFile.Open(compressedFilePath, ZipArchiveMode.Read))
				{
					using (var compressed = zipFile.GetEntry($"bitcoin-0.16.0/bin/{bitcoindFileName}").Open())
					{
						const int BufferSize = 8 * 1024;
						var buffer = new byte[BufferSize];
						var readed = 0;
						using (var uncompressed = File.Create(uncompressedFilePath))
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
			var bitcoindFileName = "bitcoind" + (IsWindows ? ".exe" : "");
			var bitcoinCoreExe = Path.Combine(folder, bitcoindFileName);
			var dataFolder = Path.Combine(folder, "data");
			var configFilePath = Path.Combine(folder, "bitcoin.conf");

			Directory.CreateDirectory(dataFolder);

			_config.TryAdd("regtes", "1");
			_config.TryAdd("rest", "1");
			_config.TryAdd("server", "1");
			_config.TryAdd("txindex", "1");
			_config.TryAdd("port", "8555");
			_config.TryAdd("rpcport", "18555");
			_config.TryAdd("printtoconsole", "1");
			_config.TryAdd("whitebind", "127.0.0.1:8555");

			var configLines = _config.Select(x=> $"{x.Key}={x.Value}").Reverse();
			File.WriteAllLines(configFilePath, configLines);

			var bitcoindStartInfo = new ProcessStartInfo{
				Arguments=$"-datadir={folder} -regtest -conf={configFilePath}",
				WorkingDirectory=folder,
				FileName=bitcoinCoreExe,
				CreateNoWindow=true,
				WindowStyle=ProcessWindowStyle.Hidden
			};
			_process = Process.Start(bitcoindStartInfo);

			var restClient = new RestClient(new Uri("http://127.0.0.1:18555"));
			while (true)
			{
				try
				{
					var info = restClient.GetChainInfoAsync().Result;
					break;
				}
				catch
				{
				}
			}
		}

		public void BroadcastBlock(Block block)
		{
			using (var node = Node.Connect(Network.RegTest, "127.0.0.1:8555"))
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

		private static void Bash(string commandLine){
			using(var bash = Process.Start("/bin/bash", $"-c \"{commandLine}\"")){
				bash.WaitForExit();
			}
		}
	}
}