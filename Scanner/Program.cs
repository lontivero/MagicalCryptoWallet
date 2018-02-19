using System;
using MagicalCryptoWallet.Backend;
using NBitcoin;
using NBitcoin.RPC;

namespace Scanner
{
    public class Runner
    {
	    private const int BadArgumentErrorCode = 1;
	    private const int InvalidBlockHashErrorCode = 2;

		public static void Main(string[] args)
	    {
		    var dataFolder = "./data";
		    var restClientEndpoint = "http://127.0.0.1:18332/";
		    uint256 blockhash=null;

			try
		    {
			    for (var i = 0; i < args.Length; i++)
			    {
				    var arg = args[i];
				    switch (arg)
				    {
					    case "--data":
						    dataFolder = args[++i];
						    break;
					    case "--help":
						    DisplayUsage();
							return;
					    case "--endpoint":
						    restClientEndpoint = args[++i];
						    if (!Uri.IsWellFormedUriString(restClientEndpoint, UriKind.Absolute))
						    {
							    Console.Error.WriteLine("Invalid rest API endpoint");
							    Environment.Exit(InvalidBlockHashErrorCode);
							}
							break;
						case "--blockhash":
							if (!uint256.TryParse(args[++i], out blockhash))
							{
								Console.Error.WriteLine("Invalid block hash argument");
								Environment.Exit(InvalidBlockHashErrorCode);
							}
							break;
				    }
			    }
		    }
		    catch (IndexOutOfRangeException)
		    {
			    DisplayUsage();
				Environment.Exit(BadArgumentErrorCode);
	        }
			if(blockhash == null){
				Console.Error.WriteLine("The blockhash argument is missing. Use --blockhash");
				Environment.Exit(InvalidBlockHashErrorCode);
			}
	        BuildFilter(blockhash, dataFolder, restClientEndpoint);
        }

	    private static void BuildFilter(uint256 blockhash, string dataFolder, string restClientEndpoint)
	    {
		    var restClient = new RestClient(new Uri(restClientEndpoint));
		    var block = restClient.GetBlock(blockhash);
		    var filter = BlockFilterBuilder.Build(block);
		    using (var filterRepository = GcsFilterRepository.Open(dataFolder))
		    {
				filterRepository.Put(blockhash, filter);
		    }
	    }

	    private static void DisplayUsage()
	    {
		    Console.WriteLine("Block Filter Creator v.0.0.1\n");
		    Console.WriteLine("Usage: BlockFilterTool [--data=data-folder-path] blockhash");
		    Console.WriteLine("   ex: BlockFilterTool 000000000000000001964cbfa3d33d2b7f6517125f43428a244b901fe289a551\n");
		    Console.WriteLine("--data       path to the folder containing the flters files and indexes\n");
		    Console.WriteLine("--endpoint   uri to the bitcoin core rest API\n");
		}
	}
}
