using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NBitcoin;
using NBitcoin.Crypto;

namespace MagicalCryptoWallet
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
		private string _dataFilePath;

		private UtxosRepository(string dataFilePath, UtxoStore store, CachedIndex index)
		{
			_store = store;
			_index = index;
			_checkpoints = new Stack<uint256>();
			_lastAddedKey = uint256.Zero;
			_deletedCount = 0;
			_dataFilePath = dataFilePath;
		}

		public static UtxosRepository Read(string dataFilePath)
		{
			var dataFileName = Path.GetFileNameWithoutExtension(dataFilePath);
			var dataDirectory = Path.GetDirectoryName(dataFilePath);
			var filterFileName = Path.Combine(dataDirectory, $"{dataFileName}");
			var indexFileName = Path.Combine(dataDirectory, $"{dataFileName}");
			File.Copy($"{filterFileName}.dat", $"{filterFileName}-read.dat", true);
			File.Copy($"{indexFileName}.idx", $"{filterFileName}-read.idx", true);
			return Open($"{filterFileName}-read.dat", FileAccess.Read);
		}
		public static UtxosRepository Open(string dataFilePath)
		{
			return Open(dataFilePath, FileAccess.ReadWrite);
		}

		private static UtxosRepository Open(string dataFilePath, FileAccess fileAccess)
		{
			var dataFileName = Path.GetFileNameWithoutExtension(dataFilePath);
			var dataDirectory = Path.GetDirectoryName(dataFilePath);
			var filterStream = File.Open(Path.Combine(dataDirectory, $"{dataFileName}.dat"), FileMode.OpenOrCreate, fileAccess, FileShare.ReadWrite);
			var indexStream = File.Open(Path.Combine(dataDirectory, $"{dataFileName}.idx"), FileMode.OpenOrCreate, fileAccess, FileShare.ReadWrite);
			var utxoStore = new UtxoStore(filterStream);
			var indexStore = new Index(indexStream);
			var fastIndexStore = new CachedIndex(indexStore);
			var repo = new UtxosRepository(dataFilePath, utxoStore, fastIndexStore);
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

			if( _deletedCount++ >= (100 * 1000))
				Pack();
		}

		public void Checkpoint()
		{
			if(_checkpoints.Count >= 1000)
			{
				_checkpoints = new Stack<uint256>(_checkpoints.Take(200).Reverse());
			}

			var latest = _checkpoints.Count > 0 ? _checkpoints.Peek() : uint256.Zero;

			if(_lastAddedKey != latest)
			{
				_checkpoints.Push(_lastAddedKey);
				_store.BaseStream.Flush();
			}
		}

		public void RevertToCheckpoint()
		{
			if(_checkpoints.Count == 0)
				return;
			
			var key = _checkpoints.Pop();
			var offset = _index.Get(key);
			var utxo = _store.GetFrom(offset).FirstOrDefault();
			if(utxo == null) return;

			var utxoByteLen = 0;
			var mem = new MemoryStream();
			using(var writer = new BinaryWriter(mem)){
				_store.Write(writer, utxo);
				utxoByteLen = (int)mem.Length;
			}
			_store.BaseStream.SetLength(offset + utxoByteLen);
			Pack();
		}

		public void Pack()
		{
			if(_deletedCount <= 0) return;
			
			string copyStoreFilePath, copyIndexFilePath;

			var tempPath = Path.GetTempPath();
			using(var copy = Open(Path.Combine(tempPath, "utxos.dat")))
			{
				foreach(var utxo in this.GetAll())
				{
					copy.Append(utxo);
				}

				copyStoreFilePath = Path.Combine(tempPath, "utxos.dat");
				copyIndexFilePath = Path.Combine(tempPath,  "utxos.idx");
			}

			((IDisposable)_store)?.Dispose();
			((IDisposable)_index)?.Dispose();

			var dataFileName = Path.GetFileNameWithoutExtension(_dataFilePath);
			var dataDirectory = Path.GetDirectoryName(_dataFilePath);
			var storeFilePath= Path.Combine(dataDirectory, $"{dataFileName}.dat");
			var indexFilePath= Path.Combine(dataDirectory, $"{dataFileName}.idx");

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
			Pack();
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

		internal override void Write(BinaryWriter writer, UtxoData data)
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
