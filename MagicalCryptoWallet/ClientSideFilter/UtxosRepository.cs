using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NBitcoin;
using NBitcoin.Crypto;

namespace MagicalCryptoWallet.Backend
{
	public class UtxoData{
		internal static OutPoint DeletedUtxo = new OutPoint(uint256.Zero, 0);

		public OutPoint OutPoint { get; }
		public Script Script { get; }

		public UtxoData(OutPoint outPoint, Script script)
		{
			OutPoint = outPoint;
			Script = script;
		}
	}

	public class UtxosRepository : IDisposable
	{
		private readonly UtxoStore _store;
		private readonly CachedIndex _index;

		public UtxosRepository(UtxoStore store, CachedIndex index)
		{
			_store = store;
			_index = index;
		}

		public static UtxosRepository Open(string folderPath)
		{
			var dataDirectory = new DirectoryInfo(folderPath);
			var filterStream = File.Open(Path.Combine(folderPath, "utxos.dat"), FileMode.OpenOrCreate);
			var indexStream = File.Open(Path.Combine(folderPath,  "utxos.idx"), FileMode.OpenOrCreate);
			var utxoStore = new UtxoStore(filterStream);
			var indexStore = new Index(indexStream);
			var fastIndexStore = new CachedIndex(indexStore);
			var repo = new UtxosRepository(utxoStore, fastIndexStore);
			return repo;
		}

		public IEnumerable<UtxoData> Get(OutPoint outpoint)
		{
			var key = new uint256( Hashes.SHA256( outpoint.ToBytes()) );
			var offset = _index.Get(key);
			var utxo = _store.GetFrom(offset).FirstOrDefault();
			if(utxo.OutPoint == UtxoData.DeletedUtxo){
				return Enumerable.Empty<UtxoData>();
			}
			return _store.GetFrom(offset);
		}

		public IEnumerable<UtxoData> Get()
		{
			return _store.GetFrom(0);
		}

		public void Append(UtxoData data)
		{
			var pos = _store.Append(data);
			var key = new uint256( Hashes.SHA256( data.OutPoint.ToBytes()) );
			_index.Append(key, pos);
		}

		public void Delete(OutPoint outpoint)
		{
			var key = new uint256( Hashes.SHA256( outpoint.ToBytes()) );
			var offset = _index.Get(key);
			var utxo = _store.GetFrom(offset).First();
			var deleted = new UtxoData(UtxoData.DeletedUtxo, utxo.Script);
			_store.Write(offset, deleted);
		}

		#region IDisposable Support
		private bool _disposedValue = false; 

		protected virtual void Dispose(bool disposing)
		{
			if (!_disposedValue)
			{
				((IDisposable)_store)?.Dispose();
				((IDisposable)_index)?.Dispose();
				_disposedValue = true;
			}
		}

		~UtxosRepository() {
			Dispose(false);
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}
		#endregion
	}

	public class UtxoStore : Store<UtxoData>
	{
		private const short MagicSeparatorNumber = 0x4691;

		public UtxoStore(Stream stream) 
			: base(stream)
		{
		}

		protected override UtxoData Read(BinaryReader reader)
		{
			var magic = reader.ReadInt16();
			if (magic != MagicSeparatorNumber)
				return null;
			var txid = new uint256(reader.ReadBytes(32));
			var n = reader.ReadUInt32();
			var size = reader.ReadInt32();
			var script = new Script( reader.ReadBytes(size) );
			return new UtxoData(new OutPoint(txid, n), script);
		}

		protected override void Write(BinaryWriter writer, UtxoData data)
		{
			var script = data.Script.ToBytes();

			writer.Write(MagicSeparatorNumber);
			writer.Write(data.OutPoint.Hash.ToBytes());
			writer.Write(data.OutPoint.N);
			writer.Write(script.Length);
			writer.Write(script);
			writer.Flush();
		}

		public IEnumerable<UtxoData> GetFrom(int offset)
		{
			return Enumerate(offset).Where(x=>x.OutPoint != UtxoData.DeletedUtxo);
		}
	}	
}
