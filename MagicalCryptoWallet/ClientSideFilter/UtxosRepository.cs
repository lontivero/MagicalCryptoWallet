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
		private UtxoStore _store;
		private CachedIndex _index;
		private Stack<uint256> _checkpoints;
		private uint256 _lastAddedKey;
		private int _deletedCount;

		private UtxosRepository(string folderPath, UtxoStore store, CachedIndex index)
		{
			_store = store;
			_index = index;
			_checkpoints = new Stack<uint256>();
			_lastAddedKey = uint256.Zero;
			_deletedCount = 0;
		}

		public static UtxosRepository Open(string folderPath)
		{
			var dataDirectory = new DirectoryInfo(folderPath);
			var filterStream =  new FileStream(Path.Combine(folderPath, "utxos.dat"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1024 * 64);
			var indexStream = new FileStream(Path.Combine(folderPath,  "utxos.idx"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1024 * 64);
			var utxoStore = new UtxoStore(filterStream);
			var indexStore = new Index(indexStream);
			var fastIndexStore = new CachedIndex(indexStore);
			var repo = new UtxosRepository(folderPath, utxoStore, fastIndexStore);
			return repo;
		}

		public IEnumerable<UtxoData> Get(OutPoint outpoint)
		{
			var key = new uint256( Hashes.SHA256( outpoint.ToBytes()) );
			var offset = _index.Get(key);
			if(offset >= 0)
			{
				var utxo = _store.GetFrom(offset).FirstOrDefault();
				if(utxo.OutPoint != UtxoData.DeletedUtxo)
				{
					return _store.GetFrom(offset);
				}
			}
			return Enumerable.Empty<UtxoData>();
		}

		public IEnumerable<UtxoData> GetAll()
		{
			return _store.GetFrom(0);
		}

		public void Append(UtxoData data)
		{
			var pos = _store.Append(data);
			var key = new uint256( Hashes.SHA256( data.OutPoint.ToBytes()) );
			_index.Append(key, pos);
			_lastAddedKey = key;
		}

		public void Delete(OutPoint outpoint)
		{
			var key = new uint256( Hashes.SHA256( outpoint.ToBytes()) );
			var offset = _index.Get(key);
			var utxo = _store.GetFrom(offset).First();
			var deleted = new UtxoData(UtxoData.DeletedUtxo, utxo.Script);
			_store.Write(offset, deleted);

			_deletedCount++;
			Pack();
		}

		public void Checkpoint()
		{
			if(_checkpoints.Count >= 1000){
				_checkpoints = new Stack<uint256>(_checkpoints.Take(200).Reverse());
			}
			_checkpoints.Push(_lastAddedKey);
			//_store.BaseStream.Flush();
		}

		public void RevertToCheckpoint()
		{
			var key = _checkpoints.Pop();
			var offset = _index.Get(key);
			var utxo = _store.GetFrom(offset).Skip(1).First();
			var keylen = new uint256( Hashes.SHA256( utxo.OutPoint.ToBytes()) );
			var len = _index.Get(keylen);
			_store.BaseStream.SetLength(len);
		}

		public void Pack()
		{
			if(_deletedCount < (100 * 1000))
				return;
			
			string copyStoreFilePath, copyIndexFilePath;

			var tempPath = Path.GetTempPath();
			using(var copy = Open(tempPath))
			{
				foreach(var utxo in this.GetAll())
				{
					copy.Append(utxo);
				}

				copyStoreFilePath = Path.Combine(tempPath, "utxos.dat");
				copyIndexFilePath = Path.Combine(tempPath,  "utxos.idx");
				
			}

			var folderPath = Path.GetDirectoryName(((FileStream)_store.BaseStream).Name);
			var storeFilePath = Path.Combine(folderPath, "utxos.dat");
			var indexFilePath = Path.Combine(folderPath,  "utxos.idx");

			((IDisposable)_store)?.Dispose();
			((IDisposable)_index)?.Dispose();

			File.Delete(storeFilePath);
			File.Delete(indexFilePath);
			File.Move(copyIndexFilePath, indexFilePath);
			File.Move(copyStoreFilePath, storeFilePath);

			var filterStream = File.Open(storeFilePath, FileMode.OpenOrCreate);
			var indexStream = File.Open(indexFilePath, FileMode.OpenOrCreate);
			_store = new UtxoStore(filterStream);
			_index = new CachedIndex(new Index(indexStream));

			_deletedCount=0;
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
