using MagicalCryptoWallet.Crypto;
using MagicalCryptoWallet.Logging;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore;
using Xunit;
using Microsoft.AspNetCore.Hosting;
using MagicalCryptoWallet.Backend;
using Microsoft.AspNetCore.TestHost;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using MagicalCryptoWallet.Backend.Models;
using NBitcoin;

namespace MagicalCryptoWallet.Tests
{
	public class BackendTests : IClassFixture<SharedFixture>
	{
        private readonly TestServer _server;
        private readonly HttpClient _client;
		private SharedFixture SharedFixture { get; }

		public BackendTests(SharedFixture fixture)
		{
			SharedFixture = fixture;
            Global.InitializeAsync().Wait();
            
            _server = new TestServer(new WebHostBuilder()
            .UseStartup<Startup>());
            _client = _server.CreateClient();
		}

        [Fact]
        public async void GetExchangeRatesAsyncTest()
        {
            var res = await _client.GetAsync("/api/v1/btc/Blockchain/exchange-rates");
            Assert.True(res.IsSuccessStatusCode);

            var exchangeRates = await res.ReadAsAsync<List<ExchangeRate>>();
            Assert.Equal(1, exchangeRates.Count);

            var rate = exchangeRates[0];
            Assert.Equal("USD", rate.Ticker);
            Assert.True(rate.Rate > 0);
        }

        [Fact]
        public async void BroadcastWithOutMinFeeTest()
        {
            var utxos = await Global.RpcClient.ListUnspentAsync();
            var utxo = utxos[0];
            var addr = await Global.RpcClient.GetNewAddressAsync();
            var tx=new Transaction();
            tx.Inputs.Add(new TxIn(utxo.OutPoint, Script.Empty));
            tx.Outputs.Add(new TxOut(utxo.Amount, addr));
            var signedTx = await Global.RpcClient.SignRawTransactionAsync(tx);

            var content=new  StringContent($"'{signedTx.ToHex()}'", Encoding.UTF8, "application/json");
            var res = await _client.PostAsync("/api/v1/btc/Blockchain/broadcast", content);
            Assert.False(res.IsSuccessStatusCode);
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, res.StatusCode);
        }

        [Fact]
        public async void BroadcastReplayTxTest()
        {
            var utxos = await Global.RpcClient.ListUnspentAsync();
            var utxo = utxos[0];
            var tx = await Global.RpcClient.GetRawTransactionAsync(utxo.OutPoint.Hash);
            var content=new  StringContent($"'{tx.ToHex()}'", Encoding.UTF8, "application/json");
            var res = await _client.PostAsync("/api/v1/btc/Blockchain/broadcast", content);
            Assert.True(res.IsSuccessStatusCode);
            Assert.Equal("\"Transaction is already in the blockchain.\"", await res.Content.ReadAsStringAsync());
        }
        
        [Fact]
        public async void BroadcastInvalidTxTest()
        {
            var content=new  StringContent($"''", Encoding.UTF8, "application/json");
            var res = await _client.PostAsync("/api/v1/btc/Blockchain/broadcast", content);
            Assert.False(res.IsSuccessStatusCode);
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, res.StatusCode);
            Assert.Equal("\"Invalid hex.\"", await res.Content.ReadAsStringAsync());
        }
    }

    static class HttpResponseMessageExtensions
    {
        public static async Task<T> ReadAsAsync<T>(this HttpResponseMessage me)
        {
            var jsonString =  await me.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<T>(jsonString);
        }
    }
}
