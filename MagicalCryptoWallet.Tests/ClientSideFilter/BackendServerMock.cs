using System;
using MagicalCryptoWallet.Backend;
using Microsoft.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using MagicalCryptoWallet.Backend.Controllers;
using System.Threading.Tasks;
using NBitcoin;
using System.IO;
using Newtonsoft.Json;
using System.Text;

namespace MagicalCryptoWallet.Tests
{
	public class BackendServerMock : IDisposable
	{
		private readonly string _endpoint = "http://localhost:37127/";
		private IWebHost _host;

		public BackendServerMock(string endpoint){
			_endpoint = endpoint;
		}

		public async Task StartAsync()
		{
			DeleteFiltersDirectory();
			await CreateConfigFileAsync();
			await Global.InitializeAsync(false);

			_host = WebHost.CreateDefaultBuilder()
					.UseStartup<Startup>()
					.UseUrls(_endpoint)
					.Build();
			_host.Start();
		}

        private async Task CreateConfigFileAsync()
        {
			var cfgPath = Path.Combine(Global.DataDir, "Config.json");
			var cfg = new {
				Network = "RegTest",
				BitcoinRpcUser = "user",
				BitcoinRpcPassword = "password",
				RestClientEndpoint = $"http://127.0.0.1:18555/"
			};

			string jsonString = JsonConvert.SerializeObject(cfg, Formatting.Indented);
			await File.WriteAllTextAsync(cfgPath, jsonString, Encoding.UTF8);
		}

		private void DeleteFiltersDirectory(){
			if(Directory.Exists(Global.FilterDirectory))
				Directory.Delete(Global.FilterDirectory, recursive: true);
		}
		
        public void Dispose()
		{
			_host?.StopAsync().Wait();
		}

	}

	public class StartupMock
	{
		public void ConfigureServices(IServiceCollection services)
		{
			services.AddMvc();
		}

		public void Configure(IApplicationBuilder app, IHostingEnvironment env)
		{
			app.UseMvc();
		}
	}
}
