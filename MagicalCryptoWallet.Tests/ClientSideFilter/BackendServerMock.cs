using System;
using MagicalCryptoWallet.Backend;
using Microsoft.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using MagicalCryptoWallet.Backend.Controllers;
using System.Threading.Tasks;

namespace MagicalCryptoWallet.Tests
{
	public class BackendServerMock : IDisposable
	{
		private readonly string _endpoint = "http://localhost:37127/";
		private IWebHost _host;

		public BackendServerMock(string endpoint){
			_endpoint = endpoint;
		}
        public void Start()
		{
			_host = WebHost.CreateDefaultBuilder()
					.UseStartup<Startup>()
					.UseUrls(_endpoint)
					.Build();
			_host.Start();
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

//	public class BackendController : BlockchainController{
//		
//	}
}
